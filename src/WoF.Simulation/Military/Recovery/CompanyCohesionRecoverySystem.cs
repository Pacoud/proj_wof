
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Recovery;

/// <summary>
/// Gere la recuperation horaire de cohesion
/// des compagnies hors combat.
///
/// La cohesion appartient a Company.
/// Ce systeme ne modifie ni les effectifs
/// ni l'organisation divisionnaire.
/// </summary>
internal sealed class CompanyCohesionRecoverySystem
{
    private readonly DiplomacySystem _diplomacy;
    private readonly double _recoveryPerHour;

    private long? _lastProcessedHour;

    internal CompanyCohesionRecoverySystem(
        DiplomacySystem diplomacy,
        double recoveryPerHour)
    {
        _diplomacy = diplomacy
            ?? throw new ArgumentNullException(
                nameof(diplomacy));

        if (!double.IsFinite(recoveryPerHour)
            || recoveryPerHour < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recoveryPerHour));
        }

        _recoveryPerHour = recoveryPerHour;
    }

    internal void ProcessRecovery(
        long hour,
        IEnumerable<Division> divisions,
        IReadOnlyCollection<Engagement> endedThisTick)
    {
        ArgumentNullException.ThrowIfNull(divisions);
        ArgumentNullException.ThrowIfNull(endedThisTick);

        if (hour < 0)
            throw new ArgumentOutOfRangeException(nameof(hour));

        // Une heure ne peut etre traitee
        // qu'une seule fois.
        if (_lastProcessedHour is long previous
            && hour <= previous)
        {
            throw new InvalidOperationException(
                "Company cohesion recovery hour " +
                "already processed.");
        }

        // Comportement inactif par defaut.
        // On conserve la garde horaire.
        if (_recoveryPerHour == 0)
        {
            _lastProcessedHour = hour;
            return;
        }

        var allDivisions = divisions.ToList();

        if (allDivisions.Any(d => d is null))
        {
            throw new ArgumentException(
                "Division collection contains null.",
                nameof(divisions));
        }

        // Exclure tous les participants d'une
        // bataille terminee pendant cette heure.
        var excluded = new HashSet<Division>(
            ReferenceEqualityComparer.Instance);

        foreach (var engagement in endedThisTick)
        {
            if (engagement is null)
            {
                throw new ArgumentException(
                    "Ended engagement collection contains null.",
                    nameof(endedThisTick));
            }

            foreach (var participant in engagement.Participants)
                excluded.Add(participant);
        }

        // Preparer tous les changements avant
        // de modifier la premiere compagnie.
        var pending = new List<(
            Company Company,
            double NewCohesion)>();

        var visited = new HashSet<Division>(
            ReferenceEqualityComparer.Instance);

        foreach (var division in allDivisions)
        {
            if (!visited.Add(division))
                continue;

            if (excluded.Contains(division))
                continue;

            // Les compagnies n'ont pas les stocks
            // actifs en mode Legacy.
            if (division.ResourceMode
                != DivisionResourceMode.Hierarchical)
            {
                continue;
            }

            if (!DivisionRecoveryEligibility
                .CanRecoverInPlace(
                    division,
                    allDivisions,
                    _diplomacy))
            {
                continue;
            }

            foreach (var battalion in division.Battalions)
            {
                foreach (var company in battalion.Companies)
                {
                    var manpower =
                        company.Composition?.Manpower;

                    // Une compagnie sans personnel
                    // disponible ne se reorganise pas.
                    if (manpower is null
                        || manpower.Current <= 0)
                    {
                        continue;
                    }

                    double before = company.Cohesion;

                    if (before >= 100)
                        continue;

                    double recovered = Math.Min(
                        _recoveryPerHour,
                        100.0 - before);

                    double after = before + recovered;

                    if (!double.IsFinite(after)
                        || after < 0
                        || after > 100)
                    {
                        throw new InvalidOperationException(
                            "Invalid recovered cohesion.");
                    }

                    pending.Add((company, after));
                }
            }
        }

        // Application uniquement apres
        // preparation et validation completes.
        foreach (var change in pending)
        {
            change.Company.Cohesion = change.NewCohesion;
        }

        _lastProcessedHour = hour;
    }
}
