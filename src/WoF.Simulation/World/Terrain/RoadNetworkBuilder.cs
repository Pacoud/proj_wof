namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Relie les localités entre elles et aux points de passage routiers des frontières.
///
/// Réseau : arbre couvrant minimal des localités (distance hexagonale,
/// départage par indice), puis chaque point de passage vers la localité la plus proche.
/// Chaque liaison est tracée par un A* sur les cases. Le coût croît avec la pente,
/// les bois, les marais et la montagne ; franchir une rivière coûte cher, ce qui limite
/// les ponts. Une route existante coûte peu : les routes se rejoignent.
/// Là où une route franchit un bord de rivière, un pont est posé.
/// </summary>
internal sealed class RoadNetworkBuilder : ITerrainGraph
{
    private const double SlopeMetersPerCost = 10;
    private const double WoodsCost = 0.5;
    private const double MarshCost = 4;
    private const double MountainCost = 2;
    private const double RiverCrossingCost = 15;
    private const double ExistingRoadFactor = 0.25;
    private const double BuildingsFactor = 0.5;

    private readonly ProvinceHexGrid _grid;

    public int NodeCount => _grid.CellCount;

    private RoadNetworkBuilder(ProvinceHexGrid grid)
    {
        _grid = grid;
    }

    internal static void Apply(ProvinceHexGrid grid)
    {
        var builder = new RoadNetworkBuilder(grid);

        var centers = grid.Localities.Select(l => l.CenterCell).ToArray();

        foreach (var (from, to) in MinimumSpanningTree(grid, centers))
            builder.Build(from, to);

        var roadCrossings = grid.BorderCrossings
            .Where(c => c.Kind == BorderCrossingKind.Road)
            .Select(c => c.CellIndex)
            .ToArray();

        foreach (int crossing in roadCrossings)
        {
            int target = centers.Length > 0
                ? Nearest(grid, crossing, centers)
                : roadCrossings[0];

            if (target != crossing)
                builder.Build(crossing, target);
            else
                grid.AddFeature(crossing, TerrainCellFeature.Road);
        }
    }

    private void Build(int from, int to)
    {
        var path = TerrainPathfinder.FindPath(this, from, to);

        if (path is null)
            return;

        _grid.AddFeature(path[0], TerrainCellFeature.Road);

        for (int i = 1; i < path.Count; i++)
        {
            int previous = path[i - 1];
            int cell = path[i];

            _grid.AddFeature(cell, TerrainCellFeature.Road);

            int direction = DirectionTo(previous, cell);

            if (_grid.IsRiverEdge(previous, direction)
                && !_grid.HasBridge(previous, direction))
            {
                _grid.SetBridge(previous, direction);
            }
        }
    }

    public void GetEdges(int node, List<(int Node, double Cost)> buffer)
    {
        float elevation = _grid.GetElevation(node);

        for (int d = 0; d < HexCoord.DirectionCount; d++)
        {
            int next = _grid.GetNeighborIndex(node, d);

            if (next < 0)
                continue;

            var features = _grid.GetFeatures(next);
            var terrain = _grid.GetTerrain(next);

            double cost = 1
                + Math.Abs(_grid.GetElevation(next) - elevation) / SlopeMetersPerCost;

            if ((features & TerrainCellFeature.Woods) != 0)
                cost += WoodsCost;

            if (terrain == TerrainType.Marsh)
                cost += MarshCost;

            if (terrain == TerrainType.Mountain)
                cost += MountainCost;

            if (_grid.IsRiverEdge(node, d) && !_grid.HasBridge(node, d))
                cost += RiverCrossingCost;

            if ((features & TerrainCellFeature.Road) != 0)
                cost *= ExistingRoadFactor;
            else if ((features & TerrainCellFeature.Buildings) != 0)
                cost *= BuildingsFactor;

            buffer.Add((next, cost));
        }
    }

    public double EstimateCost(int from, int to)
    {
        // Une étape coûte au moins 1 × ExistingRoadFactor.
        return _grid.GetCoord(from).DistanceTo(_grid.GetCoord(to))
            * ExistingRoadFactor;
    }

    private int DirectionTo(int from, int to)
    {
        for (int d = 0; d < HexCoord.DirectionCount; d++)
        {
            if (_grid.GetNeighborIndex(from, d) == to)
                return d;
        }

        throw new InvalidOperationException("Road path is not continuous.");
    }

    private static List<(int From, int To)> MinimumSpanningTree(
        ProvinceHexGrid grid,
        int[] centers)
    {
        var edges = new List<(int From, int To)>();

        if (centers.Length < 2)
            return edges;

        var inTree = new bool[centers.Length];
        inTree[0] = true;

        for (int added = 1; added < centers.Length; added++)
        {
            int bestFrom = -1;
            int bestTo = -1;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < centers.Length; i++)
            {
                if (!inTree[i])
                    continue;

                var a = grid.GetCoord(centers[i]);

                for (int j = 0; j < centers.Length; j++)
                {
                    if (inTree[j])
                        continue;

                    int distance = a.DistanceTo(grid.GetCoord(centers[j]));

                    // Parcours dans l'ordre (i, j) : à distance égale, la première paire reste.
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestFrom = i;
                        bestTo = j;
                    }
                }
            }

            inTree[bestTo] = true;
            edges.Add((centers[bestFrom], centers[bestTo]));
        }

        return edges;
    }

    private static int Nearest(ProvinceHexGrid grid, int cell, int[] centers)
    {
        var origin = grid.GetCoord(cell);

        int best = centers[0];
        int bestDistance = origin.DistanceTo(grid.GetCoord(best));

        for (int i = 1; i < centers.Length; i++)
        {
            int distance = origin.DistanceTo(grid.GetCoord(centers[i]));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = centers[i];
            }
        }

        return best;
    }
}
