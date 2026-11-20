
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

internal enum TacticalBattalionContactStatus
{
    CurrentLocalized,
    CurrentSuspected,
    Stale
}

/// <summary>
/// Observation transmise au QG.
///
/// ObserverCompanyId identifie une compagnie amie.
/// Contact ne contient aucune reference a la
/// compagnie ennemie reelle.
/// </summary>
internal sealed record TacticalBattalionContactReport(
    int ObserverCompanyId,
    TacticalEnemyContactReport Contact,
    TacticalBattalionContactStatus Status,
    long AgeHours);


/// <summary>
/// Photographie de renseignement limitee aux
/// informations communicables au QG.
///
/// Aucun objet militaire vivant n'est expose.
/// BattalionId est local a la division.
/// </summary>
internal sealed record TacticalBattalionIntelligenceSnapshot(
    int BattalionId,
    string BattalionName,
    string FriendlyDivisionName,
    long Hour,
    IReadOnlyList<TacticalBattalionContactReport> Contacts);

/// <summary>
/// Regroupe les rapports des propres compagnies
/// du bataillon.
///
/// Ne consulte ni les deploiements ennemis
/// ni la correspondance interne piste-cible.
/// </summary>
internal sealed class TacticalBattalionIntelligenceSystem
{
    private readonly TacticalBattle _battle;
    private readonly TacticalPerceptionState _perception;

    internal TacticalBattalionIntelligenceSystem(
        TacticalBattle battle,
        TacticalPerceptionState perception)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));

        _perception = perception
            ?? throw new ArgumentNullException(nameof(perception));

        if (!ReferenceEquals(perception.Battle, battle))
        {
            throw new InvalidOperationException(
                "Intelligence services belong to different battles.");
        }
    }

    /// <summary>
    /// maxAgeHours limite la conservation des pistes.
    /// Zero conserve seulement les observations
    /// de l'heure courante.
    ///
    /// Cette methode ne fait pas de nouvelle detection.
    /// </summary>
    internal TacticalBattalionIntelligenceSnapshot Capture(
        Battalion battalion,
        long hour,
        long maxAgeHours)
    {
        ArgumentNullException.ThrowIfNull(battalion);

        if (maxAgeHours < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAgeHours));
        }

        if (hour < _battle.Engagement.StartHour
            || _perception.LastUpdateHour != hour)
        {
            throw new InvalidOperationException(
                "Perception is not current for the requested hour.");
        }

        var deployments =
            _battle.GetBattalionDeployments(battalion);

        if (deployments.Count == 0
            || deployments.Count != battalion.Companies.Count)
        {
            throw new InvalidOperationException(
                "Battalion must be fully deployed in this battle.");
        }

        // Verifier que tous les observateurs sont
        // bien des compagnies de ce bataillon.
        var represented = new HashSet<Company>(
            ReferenceEqualityComparer.Instance);

        foreach (var deployment in deployments)
        {
            if (!_battle.ContainsDeployment(deployment)
                || !ReferenceEquals(
                    deployment.Company.ParentBattalion,
                    battalion)
                || !represented.Add(deployment.Company))
            {
                throw new InvalidOperationException(
                    "Invalid battalion observer deployment.");
            }
        }

        var contacts =
            new List<TacticalBattalionContactReport>();

        // Ordre deterministe des observateurs,
        // puis des pistes de chaque observateur.
        foreach (var deployment in deployments
            .OrderBy(d => d.Company.Id))
        {
            foreach (var report in
                _perception.GetReports(deployment))
            {
                if (report.LastObservedHour > hour)
                {
                    throw new InvalidOperationException(
                        "Intelligence contact is from the future.");
                }

                long age = hour - report.LastObservedHour;

                if (age > maxAgeHours)
                    continue;

                bool observedNow =
                    report.IsCurrentlyObserved
                    && report.LastObservedHour == hour;

                bool locatedNow =
                    observedNow
                    && report.CurrentObservationLevel
                        >= TacticalKnowledgeLevel.Located
                    && report.LastLocatedHour == hour
                    && report.LastKnownPosition is not null;

                var status = !observedNow
                    ? TacticalBattalionContactStatus.Stale
                    : locatedNow
                        ? TacticalBattalionContactStatus
                            .CurrentLocalized
                        : TacticalBattalionContactStatus
                            .CurrentSuspected;

                // Aucune resolution du TrackId vers
                // l'identite ennemie reelle.
                contacts.Add(
                    new TacticalBattalionContactReport(
                        ObserverCompanyId:
                            deployment.Company.Id,
                        Contact: report,
                        Status: status,
                        AgeHours: age));
            }
        }


        var division = battalion.ParentDivision
            ?? throw new InvalidOperationException(
                "Battalion has no parent division.");

        return new TacticalBattalionIntelligenceSnapshot(
            BattalionId: battalion.Id,
            BattalionName: battalion.Name,
            FriendlyDivisionName: division.Name,
            Hour: hour,
            Contacts: Array.AsReadOnly(contacts.ToArray()));

    }
}
