
using WoF.Simulation.Military;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Copie d'un renseignement parvenu a un echelon.
///
/// SourceBattalionId et SourceCompanyId identifient
/// la formation amie ayant produit le rapport.
///
/// Contact ne contient aucune reference vivante
/// a la formation ennemie reelle.
/// </summary>
public sealed record TacticalRelayedContactReport(
    int SourceBattalionId,
    int SourceCompanyId,
    TacticalEnemyContactReport Contact,
    long ReceivedHour);

public sealed class TacticalIntelligenceRelay
{
    private enum Destination
    {
        Battalion,
        Division
    }

    private sealed record Transit(
        CompanyDeployment Source,
        Battalion Battalion,
        Division Division,
        TacticalEnemyContactReport Contact,
        long DueHour,
        Destination Destination);

    private readonly TacticalBattle _battle;
    private readonly TacticalPerceptionState _perception;
    private readonly TacticalCommunicationDelays _delays;

    private readonly PriorityQueue<
        Transit,
        (long DueHour, long Sequence)> _pending = new();

    // Evite de publier deux fois la meme observation.
    private readonly HashSet<
        (CompanyDeployment Source, int TrackId, long Hour)>
        _published = new();

    // Cle interne : origine de la piste + numero local.
    // Deux compagnies rapportant la meme cible conservent
    // leurs renseignements distincts.
    private readonly Dictionary<
        Battalion,
        Dictionary<
            (CompanyDeployment Source, int TrackId),
            TacticalRelayedContactReport>>
        _battalionReports =
            new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<
        Division,
        Dictionary<
            (CompanyDeployment Source, int TrackId),
            TacticalRelayedContactReport>>
        _divisionReports =
            new(ReferenceEqualityComparer.Instance);

    private long _nextSequence;
    private long? _lastStepHour;

    public TacticalIntelligenceRelay(
        TacticalBattle battle,
        TacticalPerceptionState perception,
        TacticalCommunicationDelays delays)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(perception);
        ArgumentNullException.ThrowIfNull(delays);

        if (!ReferenceEquals(perception.Battle, battle))
            throw new InvalidOperationException(
                "Perception belongs to another battle.");

        _battle = battle;
        _perception = perception;
        _delays = delays;
    }

    /// <summary>
    /// Avance le relais a une heure donnee.
    ///
    /// A appeler APRES la mise a jour de perception
    /// correspondant a cette heure.
    ///
    /// Les messages sont livres dans l'ordre de
    /// leurs dates de reception simulees.
    /// </summary>
    public void Step(long currentHour)
    {
        if (currentHour < _battle.Engagement.StartHour)
            throw new ArgumentOutOfRangeException(
                nameof(currentHour));

        if (_lastStepHour is long previous
            && currentHour < previous)
        {
            throw new InvalidOperationException(
                "Intelligence relay time cannot move backwards.");
        }

        // Reserve une marge pour les deux relais.
        // Evite le debordement lors des additions.
        long totalDelay = checked(
            _delays.CompanyToBattalionHours
            + _delays.BattalionToDivisionHours);

        if (currentHour > long.MaxValue - totalDelay)
            throw new ArgumentOutOfRangeException(
                nameof(currentHour));

        PublishNewCompanyReports(currentHour);

        // L'ordre chronologique reste correct meme si
        // Step saute plusieurs heures de simulation.
        while (_pending.TryPeek(
            out var next,
            out var priority)
            && priority.DueHour <= currentHour)
        {
            _pending.Dequeue();

            if (next.Destination == Destination.Battalion)
            {
                bool accepted = StoreBattalion(next);

                if (accepted)
                {
                    long divisionDue = checked(
                        next.DueHour
                        + _delays.BattalionToDivisionHours);

                    Schedule(new Transit(
                        next.Source,
                        next.Battalion,
                        next.Division,
                        next.Contact,
                        divisionDue,
                        Destination.Division));
                }
            }
            else
            {
                StoreDivision(next);
            }
        }

        _lastStepHour = currentHour;
    }

    /// <summary>
    /// Capture uniquement les rapports que la compagnie
    /// possede comme observation actuelle de cette heure.
    /// </summary>
    private void PublishNewCompanyReports(long hour)
    {
        foreach (var source in _battle.Deployments)
        {
            Battalion battalion =
                source.Company.ParentBattalion
                ?? throw new InvalidOperationException(
                    "Deployed company has no battalion.");

            Division division =
                battalion.ParentDivision
                ?? throw new InvalidOperationException(
                    "Battalion has no division.");

            foreach (var report in _perception.GetReports(source))
            {
                if (!report.IsCurrentlyObserved
                    || report.LastObservedHour != hour)
                {
                    continue;
                }

                var key = (
                    Source: source,
                    TrackId: report.TrackId,
                    Hour: report.LastObservedHour);

                if (!_published.Add(key))
                    continue;

                // Une copie transmise ne doit jamais
                // pretendre etre une observation directe
                // actuellement realisee par le recepteur.
                var transmitted = report with
                {
                    IsCurrentlyObserved = false,
                    CurrentObservationLevel = null,
                    CurrentObservedSectorId = null
                };

                long dueHour = checked(
                    hour + _delays.CompanyToBattalionHours);

                Schedule(new Transit(
                    source,
                    battalion,
                    division,
                    transmitted,
                    dueHour,
                    Destination.Battalion));
            }
        }
    }

    private void Schedule(Transit message)
    {
        _pending.Enqueue(
            message,
            (message.DueHour, _nextSequence++));
    }

    /// <summary>
    /// Renvoie true seulement lorsqu'un renseignement
    /// plus recent a ete accepte par le bataillon.
    /// </summary>
    private bool StoreBattalion(Transit message)
    {
        if (!_battalionReports.TryGetValue(
            message.Battalion,
            out var reports))
        {
            reports = new Dictionary<
                (CompanyDeployment Source, int TrackId),
                TacticalRelayedContactReport>();

            _battalionReports.Add(
                message.Battalion,
                reports);
        }

        var key = (
            Source: message.Source,
            TrackId: message.Contact.TrackId);

        if (reports.TryGetValue(key, out var previous)
            && previous.Contact.LastObservedHour
                >= message.Contact.LastObservedHour)
        {
            return false;
        }

        reports[key] = CreateReceivedReport(message);
        return true;
    }

    private void StoreDivision(Transit message)
    {
        if (!_divisionReports.TryGetValue(
            message.Division,
            out var reports))
        {
            reports = new Dictionary<
                (CompanyDeployment Source, int TrackId),
                TacticalRelayedContactReport>();

            _divisionReports.Add(
                message.Division,
                reports);
        }

        var key = (
            Source: message.Source,
            TrackId: message.Contact.TrackId);

        if (reports.TryGetValue(key, out var previous)
            && previous.Contact.LastObservedHour
                >= message.Contact.LastObservedHour)
        {
            return;
        }

        reports[key] = CreateReceivedReport(message);
    }

    private static TacticalRelayedContactReport
        CreateReceivedReport(Transit message)
    {
        return new TacticalRelayedContactReport(
            message.Battalion.Id,
            message.Source.Company.Id,
            message.Contact,
            message.DueHour);
    }

    public IReadOnlyList<TacticalRelayedContactReport>
        GetBattalionReports(Battalion battalion)
    {
        ArgumentNullException.ThrowIfNull(battalion);

        Division? division = battalion.ParentDivision;

        if (division is null
            || !_battle.Engagement.Participants.Contains(division)
            || !division.Battalions.Contains(battalion))
        {
            throw new InvalidOperationException(
                "Battalion is outside this battle.");
        }

        if (!_battalionReports.TryGetValue(
            battalion,
            out var reports))
        {
            return Array.Empty<TacticalRelayedContactReport>();
        }

        return Array.AsReadOnly(
            reports.Values
                .OrderBy(r => r.SourceCompanyId)
                .ThenBy(r => r.Contact.TrackId)
                .ToArray());
    }

    public IReadOnlyList<TacticalRelayedContactReport>
        GetDivisionReports(Division division)
    {
        ArgumentNullException.ThrowIfNull(division);

        if (!_battle.Engagement.Participants.Contains(division))
            throw new InvalidOperationException(
                "Division is outside this battle.");

        if (!_divisionReports.TryGetValue(
            division,
            out var reports))
        {
            return Array.Empty<TacticalRelayedContactReport>();
        }

        return Array.AsReadOnly(
            reports.Values
                .OrderBy(r => r.SourceBattalionId)
                .ThenBy(r => r.SourceCompanyId)
                .ThenBy(r => r.Contact.TrackId)
                .ToArray());
    }
}
