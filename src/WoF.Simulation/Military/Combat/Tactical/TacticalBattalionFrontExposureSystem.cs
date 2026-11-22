
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Proximite PHYSIQUE de forces ennemies.
/// Ne represente ni une observation connue
/// du commandant, ni une autorisation de tir.
/// </summary>
internal enum TacticalEnemyProximity
{
    None,
    Adjacent,
    SameSector
}

internal sealed record TacticalBattalionSectorHostileReport(
    TacticalBattalionSectorAssessment Friendly,
    int HostileCompaniesInSector,
    int HostilePersonnelInSector,
    int HostileCompaniesAdjacent,
    int HostilePersonnelAdjacent,
    TacticalEnemyProximity Proximity);

internal sealed record TacticalBattalionGapHostileReport(
    TacticalFrontGapReport Gap,
    int HostileCompaniesInside,
    int HostilePersonnelInside,
    int HostileCompaniesAdjacent,
    int HostilePersonnelAdjacent,
    TacticalEnemyProximity Proximity);

internal sealed record TacticalBattalionFrontExposureReport(
    TacticalBattalionFrontContinuityReport Continuity,
    IReadOnlyList<TacticalBattalionSectorHostileReport> Sectors,
    IReadOnlyList<TacticalBattalionGapHostileReport> Gaps);

/// <summary>
/// Analyse la presence hostile dans et autour du
/// dispositif de responsabilite d'un bataillon.
///
/// Les donnees proviennent des positions reelles.
/// Ce diagnostic interne ne constitue pas encore
/// un renseignement disponible au commandement.
/// </summary>
internal sealed class TacticalBattalionFrontExposureSystem
{
    private readonly TacticalBattle _battle;

    private readonly record struct ForceCount(
        int Companies,
        int Personnel);

    internal TacticalBattalionFrontExposureSystem(
        TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    internal TacticalBattalionFrontExposureReport Evaluate(
        Battalion battalion,
        TacticalBattalionAssessmentProfile profile,
        IReadOnlyList<BattleSector> responsibilitySectors,
        DiplomacySystem diplomacy)
    {
        ArgumentNullException.ThrowIfNull(battalion);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(responsibilitySectors);
        ArgumentNullException.ThrowIfNull(diplomacy);

        // Reutiliser la continuite de 9d
        // et ses validations de deploiement.
        var continuity =
            new TacticalBattalionFrontContinuitySystem(
                _battle).Evaluate(
                    battalion,
                    profile,
                    responsibilitySectors);

        var country = battalion.ParentDivision?.Country
            ?? throw new InvalidOperationException(
                "Battalion has no country.");

        // Les voisins sont lus dans l'index du champ de bataille :
        // aucun parcours de toutes ses liaisons.
        var battlefield = _battle.Battlefield;

        // Presence des compagnies adverses capables
        // d'occuper physiquement une premiere ligne.
        var hostiles = new Dictionary<
            BattleSector,
            ForceCount>(
                ReferenceEqualityComparer.Instance);

        foreach (var deployment in _battle.Deployments)
        {
            if (deployment.Role !=
                    CompanyDeploymentRole.FrontLine
                || deployment.IsInTransit)
            {
                continue;
            }

            var otherCountry = deployment.Company
                .ParentBattalion?.ParentDivision?.Country;

            if (otherCountry is null
                || !diplomacy.AreAtWar(
                    country, otherCountry))
            {
                continue;
            }

            var composition = deployment.Company.Composition;
            int available = composition?.Manpower.Current ?? 0;

            if (available <= 0)
                continue;

            var before = hostiles.GetValueOrDefault(
                deployment.Sector);

            hostiles[deployment.Sector] = new ForceCount(
                checked(before.Companies + 1),
                checked(before.Personnel + available));
        }

        var sectorReports =
            new List<TacticalBattalionSectorHostileReport>();

        foreach (var friendly in continuity.Sectors)
        {
            var here = Sum(
                new[] { friendly.Sector },
                hostiles);

            var nearby = Sum(
                battlefield.GetNeighbors(friendly.Sector),
                hostiles);

            sectorReports.Add(
                new TacticalBattalionSectorHostileReport(
                    Friendly: friendly,
                    HostileCompaniesInSector: here.Companies,
                    HostilePersonnelInSector: here.Personnel,
                    HostileCompaniesAdjacent: nearby.Companies,
                    HostilePersonnelAdjacent: nearby.Personnel,
                    Proximity: Classify(here, nearby)));
        }

        var gapReports =
            new List<TacticalBattalionGapHostileReport>();

        foreach (var gap in continuity.Gaps)
        {
            var inside = new HashSet<BattleSector>(
                gap.Sectors,
                ReferenceEqualityComparer.Instance);

            var border = new HashSet<BattleSector>(
                ReferenceEqualityComparer.Instance);

            foreach (var sector in inside)
            {
                foreach (var neighbor in battlefield.GetNeighbors(sector))
                {
                    if (!inside.Contains(neighbor))
                        border.Add(neighbor);
                }
            }

            var internalForce = Sum(inside, hostiles);
            var externalForce = Sum(border, hostiles);

            gapReports.Add(
                new TacticalBattalionGapHostileReport(
                    Gap: gap,
                    HostileCompaniesInside:
                        internalForce.Companies,
                    HostilePersonnelInside:
                        internalForce.Personnel,
                    HostileCompaniesAdjacent:
                        externalForce.Companies,
                    HostilePersonnelAdjacent:
                        externalForce.Personnel,
                    Proximity: Classify(
                        internalForce,
                        externalForce)));
        }

        return new TacticalBattalionFrontExposureReport(
            continuity,
            Array.AsReadOnly(sectorReports.ToArray()),
            Array.AsReadOnly(gapReports.ToArray()));
    }

    private static ForceCount Sum(
        IEnumerable<BattleSector> sectors,
        IReadOnlyDictionary<BattleSector, ForceCount> forces)
    {
        int companies = 0;
        int personnel = 0;

        // Une meme position adjacente a plusieurs
        // secteurs n'est comptee qu'une fois.
        var visited = new HashSet<BattleSector>(
            ReferenceEqualityComparer.Instance);

        foreach (var sector in sectors)
        {
            if (!visited.Add(sector)
                || !forces.TryGetValue(sector, out var force))
            {
                continue;
            }

            companies = checked(
                companies + force.Companies);

            personnel = checked(
                personnel + force.Personnel);
        }

        return new ForceCount(companies, personnel);
    }

    private static TacticalEnemyProximity Classify(
        ForceCount inside,
        ForceCount adjacent)
    {
        if (inside.Companies > 0)
            return TacticalEnemyProximity.SameSector;

        if (adjacent.Companies > 0)
            return TacticalEnemyProximity.Adjacent;

        return TacticalEnemyProximity.None;
    }
}
