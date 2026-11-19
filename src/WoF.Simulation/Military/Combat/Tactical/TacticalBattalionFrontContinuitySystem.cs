
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

internal enum TacticalFrontGapKind
{
    Interior,
    Boundary,
    Unanchored
}

internal sealed record TacticalFrontGapReport(
    IReadOnlyList<BattleSector> Sectors,
    IReadOnlyList<BattleSector> AdjacentOccupiedSectors,
    TacticalFrontGapKind Kind);

internal sealed record TacticalBattalionFrontContinuityReport(
    Battalion Battalion,
    IReadOnlyList<TacticalBattalionSectorAssessment> Sectors,
    IReadOnlyList<IReadOnlyList<BattleSector>> OccupiedComponents,
    IReadOnlyList<TacticalFrontGapReport> Gaps);

/// <summary>
/// Analyse la continuite topologique d'un front de bataillon.
///
/// Une position est physiquement occupee lorsqu'au
/// moins une compagnie de premiere ligne y est
/// stationnaire avec des effectifs disponibles.
///
/// Aucun mouvement, combat ou changement de controle
/// n'est applique par cette analyse.
/// </summary>
internal sealed class TacticalBattalionFrontContinuitySystem
{
    private readonly TacticalBattle _battle;

    internal TacticalBattalionFrontContinuitySystem(
        TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    internal TacticalBattalionFrontContinuityReport Evaluate(
        Battalion battalion,
        TacticalBattalionAssessmentProfile profile,
        IReadOnlyList<BattleSector> responsibilitySectors)
    {
        ArgumentNullException.ThrowIfNull(battalion);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(responsibilitySectors);

        if (responsibilitySectors.Count == 0)
            throw new ArgumentException(
                "Front responsibility cannot be empty.",
                nameof(responsibilitySectors));

        var unique = new HashSet<BattleSector>(
            ReferenceEqualityComparer.Instance);

        var front = new List<BattleSector>();

        foreach (var sector in responsibilitySectors)
        {
            if (sector is null
                || !_battle.Battlefield.ContainsSector(sector)
                || !unique.Add(sector))
            {
                throw new ArgumentException(
                    "Invalid or duplicated front sector.",
                    nameof(responsibilitySectors));
            }

            front.Add(sector);
        }

        // Reutiliser l'evaluation sectorielle existante.
        // Elle valide aussi les deploiements du bataillon.
        var assessments =
            _battle.BattalionAssessments.EvaluateSectors(
                battalion, profile);

        var bySector = new Dictionary<
            BattleSector,
            TacticalBattalionSectorAssessment>(
                ReferenceEqualityComparer.Instance);

        foreach (var assessment in assessments)
            bySector.Add(assessment.Sector, assessment);

        var sectorReports =
            new List<TacticalBattalionSectorAssessment>();

        var occupied = new HashSet<BattleSector>(
            ReferenceEqualityComparer.Instance);

        foreach (var sector in front)
        {
            if (!bySector.TryGetValue(
                sector, out var assessment))
            {
                // Secteur confie au bataillon, mais
                // sans aucun deploiement dans ce secteur.
                assessment = new TacticalBattalionSectorAssessment(
                    Battalion: battalion,
                    Sector: sector,
                    IsPrioritySector: false,
                    FrontLineAssignments: 0,
                    StationaryFrontLineCompanies: 0,
                    AuthorizedFrontLinePersonnel: 0,
                    AvailableFrontLinePersonnel: 0,
                    SupportAssignments: 0,
                    ReserveAssignments: 0,
                    FrontLineReadiness: 0,
                    Status: TacticalBattalionSectorStatus.Uncovered);
            }

            sectorReports.Add(assessment);

            // La tenue physique est distincte de la
            // capacite de combat et des seuils de mission.
            if (assessment.StationaryFrontLineCompanies > 0
                && assessment.AvailableFrontLinePersonnel > 0)
            {
                occupied.Add(sector);
            }
        }

        // Construire uniquement les connexions entre
        // secteurs de responsabilite de ce bataillon.
        var adjacency = new Dictionary<
            BattleSector,
            List<BattleSector>>(
                ReferenceEqualityComparer.Instance);

        foreach (var sector in front)
            adjacency.Add(sector, new List<BattleSector>());

        foreach (var connection in _battle.Battlefield.Connections)
        {
            if (!adjacency.TryGetValue(
                    connection.A, out var a)
                || !adjacency.TryGetValue(
                    connection.B, out var b))
            {
                continue;
            }

            a.Add(connection.B);
            b.Add(connection.A);
        }

        // Groupes de secteurs physiquement occupes.
        var occupiedComponents =
            FindComponents(front, occupied, adjacency);

        // Groupes de secteurs non occupes.
        var unoccupied = new HashSet<BattleSector>(
            front.Where(s => !occupied.Contains(s)),
            ReferenceEqualityComparer.Instance);

        var unoccupiedRegions =
            FindComponents(front, unoccupied, adjacency);

        var gaps = new List<TacticalFrontGapReport>();

        foreach (var region in unoccupiedRegions)
        {
            var anchors = new HashSet<BattleSector>(
                ReferenceEqualityComparer.Instance);

            foreach (var sector in region)
            {
                foreach (var neighbor in adjacency[sector])
                {
                    if (occupied.Contains(neighbor))
                        anchors.Add(neighbor);
                }
            }

            // Ordre de restitution stable.
            var adjacentOccupied = front
                .Where(anchors.Contains)
                .ToArray();

            TacticalFrontGapKind kind = anchors.Count switch
            {
                0 => TacticalFrontGapKind.Unanchored,
                1 => TacticalFrontGapKind.Boundary,
                _ => TacticalFrontGapKind.Interior
            };

            gaps.Add(new TacticalFrontGapReport(
                region,
                Array.AsReadOnly(adjacentOccupied),
                kind));
        }

        return new TacticalBattalionFrontContinuityReport(
            battalion,
            Array.AsReadOnly(sectorReports.ToArray()),
            occupiedComponents,
            Array.AsReadOnly(gaps.ToArray()));
    }

    /// <summary>
    /// Identifie les composantes connexes d'un sous-ensemble
    /// de secteurs avec un parcours iteratif en largeur.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<BattleSector>>
        FindComponents(
            IReadOnlyList<BattleSector> orderedFront,
            HashSet<BattleSector> members,
            Dictionary<BattleSector, List<BattleSector>> adjacency)
    {
        var visited = new HashSet<BattleSector>(
            ReferenceEqualityComparer.Instance);

        var components =
            new List<IReadOnlyList<BattleSector>>();

        foreach (var root in orderedFront)
        {
            if (!members.Contains(root)
                || !visited.Add(root))
            {
                continue;
            }

            var queue = new Queue<BattleSector>();
            var component = new List<BattleSector>();

            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                foreach (var neighbor in adjacency[current])
                {
                    if (members.Contains(neighbor)
                        && visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            components.Add(
                Array.AsReadOnly(component.ToArray()));
        }

        return Array.AsReadOnly(components.ToArray());
    }
}
