using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Construit un TacticalBattlefield utilisable par le moteur actuel
/// à partir de la grille d'une province.
///
/// Seuls les grands secteurs demandés et leurs grands secteurs voisins sont inclus.
/// Chaque case devient un BattleSector (identifiant = indice de case + 1).
/// Deux cases voisines incluses sont reliées par une liaison de 200 m,
/// sauf si un bord de rivière sans pont les sépare.
/// </summary>
public static class TacticalBattlefieldBuilder
{
    public static ProvinceBattlefieldLayout Build(
        ProvinceHexGrid grid,
        GrandSectorPartition partition,
        IEnumerable<int> grandSectorIds)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(grandSectorIds);

        if (!ReferenceEquals(partition.Grid, grid))
            throw new InvalidOperationException(
                "Partition does not belong to this grid.");

        var requested = new SortedSet<int>();

        foreach (int id in grandSectorIds)
        {
            if (!partition.ContainsSectorId(id))
                throw new ArgumentOutOfRangeException(
                    nameof(grandSectorIds),
                    $"Unknown grand sector {id}.");

            requested.Add(id);
        }

        if (requested.Count == 0)
            throw new ArgumentException(
                "At least one grand sector is required.",
                nameof(grandSectorIds));

        var included = new SortedSet<int>(requested);

        foreach (int id in requested)
            included.UnionWith(partition.GetSector(id).NeighborIds);

        var cells = included
            .SelectMany(id => partition.GetSector(id).Cells)
            .OrderBy(c => c)
            .ToArray();

        var battlefield = new TacticalBattlefield();
        var sectorByCell = new Dictionary<int, BattleSector>(cells.Length);

        foreach (int cell in cells)
        {
            var sector = new BattleSector(
                cell + 1,
                $"Case {grid.GetColumn(cell)}-{grid.GetRow(cell)}",
                grid.GetTerrain(cell),
                (int)Math.Round(
                    grid.GetElevation(cell),
                    MidpointRounding.AwayFromZero),
                ToSectorFeature(grid.GetFeatures(cell)));

            battlefield.AddSector(sector);
            sectorByCell.Add(cell, sector);
        }

        foreach (int cell in cells)
        {
            // Directions 0 à 2 seulement : chaque paire n'est vue qu'une fois.
            for (int d = 0; d < 3; d++)
            {
                int neighbor = grid.GetNeighborIndex(cell, d);

                if (neighbor < 0
                    || !grid.CanCross(cell, d)
                    || !sectorByCell.TryGetValue(neighbor, out var other))
                {
                    continue;
                }

                battlefield.Connect(
                    sectorByCell[cell],
                    other,
                    ProvinceHexGrid.SpacingMeters);
            }
        }

        return new ProvinceBattlefieldLayout(
            grid,
            partition,
            battlefield,
            requested.ToArray(),
            included.ToArray(),
            cells,
            sectorByCell);
    }

    internal static SectorFeature ToSectorFeature(TerrainCellFeature features)
    {
        var result = SectorFeature.None;

        if ((features & TerrainCellFeature.Woods) != 0)
            result |= SectorFeature.Woods;

        if ((features & TerrainCellFeature.Buildings) != 0)
            result |= SectorFeature.Buildings;

        if ((features & TerrainCellFeature.Road) != 0)
            result |= SectorFeature.Road;

        return result;
    }
}
