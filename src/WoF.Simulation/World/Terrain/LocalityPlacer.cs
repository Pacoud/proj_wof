using WoF.Simulation.Core;

namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Place les localités puis les fait croître autour de leur centre.
///
/// Ordre : villes, bourgs, villages. Pour chaque localité, plusieurs cases
/// tirées au hasard sont évaluées ; la meilleure est retenue (basse, plate,
/// proche d'une rivière), à distance minimale des localités déjà placées.
/// Une localité ne s'étend ni en montagne ni dans un marais, et ne franchit
/// pas de rivière (les ponts ne viennent qu'avec les routes).
/// </summary>
internal static class LocalityPlacer
{
    private const long LocalitySeedDomain = 0x4C4F4341; // « LOCA »
    private const int SiteCandidates = 48;

    internal static void Apply(
        ProvinceHexGrid grid,
        TerrainGenerationProfile profile,
        double[] normalizedElevation)
    {
        ulong state = DeterministicRandom.Hash(
            grid.Descriptor.ProvinceSeed, LocalitySeedDomain);

        var centers = new List<(int Cell, int Spacing)>();

        PlaceKind(grid, normalizedElevation, ref state, centers,
            TerrainLocalityKind.City, profile.Cities, profile.CityCells);

        PlaceKind(grid, normalizedElevation, ref state, centers,
            TerrainLocalityKind.Town, profile.Towns, profile.TownCells);

        PlaceKind(grid, normalizedElevation, ref state, centers,
            TerrainLocalityKind.Village, profile.Villages, profile.VillageCells);
    }

    private static void PlaceKind(
        ProvinceHexGrid grid,
        double[] normalizedElevation,
        ref ulong state,
        List<(int Cell, int Spacing)> centers,
        TerrainLocalityKind kind,
        IntRange count,
        IntRange size)
    {
        if (count.Max == 0)
            return;

        int wanted = DeterministicRandom.NextInt(ref state, count.Min, count.Max);
        int spacing = SpacingCells(kind);

        for (int i = 0; i < wanted; i++)
        {
            int site = ChooseSite(grid, normalizedElevation, ref state, centers, spacing);

            if (site < 0)
                continue;

            int targetSize = DeterministicRandom.NextInt(ref state, size.Min, size.Max);

            var cells = Grow(grid, ref state, site, targetSize);

            foreach (int cell in cells)
            {
                grid.AddFeature(cell, TerrainCellFeature.Buildings);
                grid.RemoveFeature(cell, TerrainCellFeature.Woods);
            }

            cells.Sort();

            grid.AddLocality(new TerrainLocality(kind, site, cells.AsReadOnly()));
            centers.Add((site, spacing));
        }
    }

    private static int ChooseSite(
        ProvinceHexGrid grid,
        double[] normalizedElevation,
        ref ulong state,
        List<(int Cell, int Spacing)> centers,
        int spacing)
    {
        int best = -1;
        double bestScore = double.NegativeInfinity;

        for (int attempt = 0; attempt < SiteCandidates; attempt++)
        {
            int cell = DeterministicRandom.NextInt(ref state, 0, grid.CellCount - 1);

            if (!IsBuildable(grid, cell) || grid.IsFrameBoundary(cell))
                continue;

            var coord = grid.GetCoord(cell);
            bool tooClose = false;

            foreach (var (center, otherSpacing) in centers)
            {
                if (coord.DistanceTo(grid.GetCoord(center))
                    < Math.Max(spacing, otherSpacing))
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            double score = Score(grid, normalizedElevation, cell);

            if (score > bestScore)
            {
                bestScore = score;
                best = cell;
            }
        }

        return best;
    }

    private static double Score(
        ProvinceHexGrid grid,
        double[] normalizedElevation,
        int cell)
    {
        double elevation = normalizedElevation[cell];
        double slope = 0;
        bool nearRiver = grid.HasAnyRiverEdge(cell);

        for (int d = 0; d < HexCoord.DirectionCount; d++)
        {
            int neighbor = grid.GetNeighborIndex(cell, d);

            if (neighbor < 0)
                continue;

            slope = Math.Max(slope,
                Math.Abs(normalizedElevation[neighbor] - elevation));

            nearRiver |= grid.HasAnyRiverEdge(neighbor);
        }

        return -elevation - 10 * slope + (nearRiver ? 0.3 : 0);
    }

    /// <summary>
    /// Croissance aléatoire et compacte : parmi deux cases tirées sur la frontière,
    /// on garde la plus proche du centre.
    /// </summary>
    private static List<int> Grow(
        ProvinceHexGrid grid,
        ref ulong state,
        int site,
        int targetSize)
    {
        var cells = new List<int> { site };
        var taken = new HashSet<int> { site };
        var frontier = new List<int>();
        var inFrontier = new HashSet<int>();
        var center = grid.GetCoord(site);

        AddNeighbors(grid, site, taken, frontier, inFrontier);

        while (cells.Count < targetSize && frontier.Count > 0)
        {
            int first = DeterministicRandom.NextInt(ref state, 0, frontier.Count - 1);
            int second = DeterministicRandom.NextInt(ref state, 0, frontier.Count - 1);

            int pick = Closer(grid, center, frontier[first], frontier[second])
                ? first
                : second;

            int cell = frontier[pick];

            frontier[pick] = frontier[^1];
            frontier.RemoveAt(frontier.Count - 1);
            inFrontier.Remove(cell);

            cells.Add(cell);
            taken.Add(cell);

            AddNeighbors(grid, cell, taken, frontier, inFrontier);
        }

        return cells;
    }

    private static bool Closer(ProvinceHexGrid grid, HexCoord center, int a, int b)
    {
        int da = grid.GetCoord(a).DistanceTo(center);
        int db = grid.GetCoord(b).DistanceTo(center);

        return da < db || (da == db && a <= b);
    }

    private static void AddNeighbors(
        ProvinceHexGrid grid,
        int cell,
        HashSet<int> taken,
        List<int> frontier,
        HashSet<int> inFrontier)
    {
        for (int d = 0; d < HexCoord.DirectionCount; d++)
        {
            if (!grid.CanCross(cell, d))
                continue;

            int neighbor = grid.GetNeighborIndex(cell, d);

            if (taken.Contains(neighbor)
                || inFrontier.Contains(neighbor)
                || !IsBuildable(grid, neighbor))
            {
                continue;
            }

            frontier.Add(neighbor);
            inFrontier.Add(neighbor);
        }
    }

    private static bool IsBuildable(ProvinceHexGrid grid, int cell)
    {
        var terrain = grid.GetTerrain(cell);

        return terrain != TerrainType.Mountain
            && terrain != TerrainType.Marsh
            && (grid.GetFeatures(cell) & TerrainCellFeature.Buildings) == 0;
    }

    /// <summary>Distance minimale, en cases, entre le centre d'une localité et les autres centres.</summary>
    private static int SpacingCells(TerrainLocalityKind kind)
    {
        return kind switch
        {
            TerrainLocalityKind.City => 20,
            TerrainLocalityKind.Town => 12,
            _ => 5
        };
    }
}
