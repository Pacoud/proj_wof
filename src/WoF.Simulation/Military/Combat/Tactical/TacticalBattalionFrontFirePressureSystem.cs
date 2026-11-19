
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Bilan du feu effectivement recu par les compagnies
/// de premiere ligne d'un bataillon dans un secteur.
/// </summary>
internal sealed record TacticalBattalionSectorFirePressureReport(
    TacticalBattalionSectorAssessment Sector,
    int CompaniesUnderThreat,
    double ExpectedThreateningRounds,
    double AppliedSuppressionPoints,
    double MaximumCurrentSuppression);

internal sealed record TacticalBattalionFrontFirePressureReport(
    long Hour,
    TacticalBattalionFrontContinuityReport Continuity,
    IReadOnlyList<TacticalBattalionSectorFirePressureReport> Sectors);

/// <summary>
/// Agrege les resultats de suppression deja produits
/// par le moteur de tir, sans recreer de tirs
/// ni appliquer de nouvelles pertes.
///
/// Diagnostic physique interne, non renseignement
/// directement disponible au commandement.
/// </summary>
internal sealed class TacticalBattalionFrontFirePressureSystem
{
    private readonly TacticalBattle _battle;

    internal TacticalBattalionFrontFirePressureSystem(
        TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    internal TacticalBattalionFrontFirePressureReport Evaluate(
        Battalion battalion,
        TacticalBattalionAssessmentProfile assessmentProfile,
        IReadOnlyList<BattleSector> responsibilitySectors,
        TacticalSuppressionProfile suppressionProfile,
        long hour)
    {
        ArgumentNullException.ThrowIfNull(battalion);
        ArgumentNullException.ThrowIfNull(assessmentProfile);
        ArgumentNullException.ThrowIfNull(responsibilitySectors);
        ArgumentNullException.ThrowIfNull(suppressionProfile);

        // Exiger une phase de tir et de suppression
        // resolue pour l'heure precise demandee.
        var fire = _battle.DirectFire.LastPhase;
        var suppression = _battle.DirectFire.LastSuppressionPhase;

        if (hour < _battle.Engagement.StartHour
            || fire is null
            || suppression is null
            || fire.Hour != hour
            || suppression.Hour != hour)
        {
            throw new InvalidOperationException(
                "No completed fire and suppression phase " +
                "for the requested hour.");
        }

        // Les secteurs defendus et leur occupation
        // physique proviennent de l'analyse 9d.
        var continuity =
            new TacticalBattalionFrontContinuitySystem(
                _battle).Evaluate(
                    battalion,
                    assessmentProfile,
                    responsibilitySectors);

        // Rapports publies par la vraie phase de tir.
        // Une compagnie apparait au plus une fois.
        var byTarget = new Dictionary<
            CompanyDeployment,
            TacticalSuppressionTargetReport>(
                ReferenceEqualityComparer.Instance);

        foreach (var report in suppression.Targets)
        {
            if (report.Target is null
                || !_battle.ContainsDeployment(report.Target)
                || !double.IsFinite(
                    report.ExpectedThreateningRounds)
                || report.ExpectedThreateningRounds < 0
                || !double.IsFinite(report.PressureAdded)
                || report.PressureAdded < 0
                || !double.IsFinite(report.PressureAfter)
                || report.PressureAfter < 0
                || report.PressureAfter > 100
                || !byTarget.TryAdd(report.Target, report))
            {
                throw new InvalidOperationException(
                    "Invalid suppression target report.");
            }
        }

        // Regrouper uniquement les compagnies de
        // premiere ligne stationnaires de ce bataillon.
        var bySector = new Dictionary<
            BattleSector,
            List<CompanyDeployment>>(
                ReferenceEqualityComparer.Instance);

        foreach (var sector in continuity.Sectors)
        {
            bySector.Add(
                sector.Sector,
                new List<CompanyDeployment>());
        }

        foreach (var deployment in
            _battle.GetBattalionDeployments(battalion))
        {
            if (deployment.Role !=
                    CompanyDeploymentRole.FrontLine
                || deployment.IsInTransit)
            {
                continue;
            }

            if (bySector.TryGetValue(
                deployment.Sector, out var companies))
            {
                companies.Add(deployment);
            }
        }

        var results =
            new List<TacticalBattalionSectorFirePressureReport>(
                continuity.Sectors.Count);

        foreach (var sector in continuity.Sectors)
        {
            int threatened = 0;
            double threateningRounds = 0;
            double pressureAdded = 0;
            double maxSuppression = 0;

            foreach (var deployment in bySector[sector.Sector])
            {
                // Inclure la suppression residuelle,
                // meme si aucun nouveau tir n'a touche
                // cette compagnie durant cette heure.
                double current =
                    _battle.Suppression.GetProjectedPressure(
                        deployment,
                        hour,
                        suppressionProfile);

                maxSuppression = Math.Max(
                    maxSuppression,
                    current);

                if (!byTarget.TryGetValue(
                    deployment, out var report))
                {
                    continue;
                }

                if (report.ExpectedThreateningRounds > 0)
                    threatened++;

                threateningRounds +=
                    report.ExpectedThreateningRounds;

                pressureAdded += report.PressureAdded;
            }

            results.Add(
                new TacticalBattalionSectorFirePressureReport(
                    Sector: sector,
                    CompaniesUnderThreat: threatened,
                    ExpectedThreateningRounds: threateningRounds,
                    AppliedSuppressionPoints: pressureAdded,
                    MaximumCurrentSuppression: maxSuppression));
        }

        return new TacticalBattalionFrontFirePressureReport(
            hour,
            continuity,
            Array.AsReadOnly(results.ToArray()));
    }
}
