using WoF.Simulation.Core;

namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Trace les rivières sur les bords de cases, par un A* sur le graphe des coins.
///
/// Chaque case possède deux coins : le coin nord (emplacement 0) et le coin sud (1).
/// Les cases propriétaires de coins peuvent déborder d'une rangée et d'une colonne
/// autour du cadre, pour que les coins extérieurs du bord existent.
/// Une arête du graphe est un bord de case : la parcourir le marque comme rivière.
///
/// Coût d'une arête : 1, plus un terme d'altitude (la rivière cherche les creux),
/// plus un terme de bruit haché qui la fait serpenter. Les bords qui longent le
/// cadre sont fortement pénalisés.
/// </summary>
internal sealed class RiverTracer : ITerrainGraph
{
    private const long RiverSeedDomain = 0x52495652; // « RIVR »

    private const double ElevationWeight = 6;
    private const double MeanderWeight = 1.5;
    private const double FrameEdgePenalty = 20;
    private const int SourceCandidates = 48;
    private const int MinimumSourceDistance = 25;

    private readonly ProvinceHexGrid _grid;
    private readonly double[] _normalizedElevation;
    private readonly ulong _seed;
    private readonly int _extendedColumns;
    private readonly int _extendedRows;

    public int NodeCount => _extendedColumns * _extendedRows * 2;

    private RiverTracer(ProvinceHexGrid grid, double[] normalizedElevation)
    {
        _grid = grid;
        _normalizedElevation = normalizedElevation;
        _seed = DeterministicRandom.Hash(
            grid.Descriptor.ProvinceSeed, RiverSeedDomain);
        _extendedColumns = grid.Columns + 2;
        _extendedRows = grid.Rows + 2;
    }

    internal static void Apply(ProvinceHexGrid grid, double[] normalizedElevation)
    {
        var tracer = new RiverTracer(grid, normalizedElevation);

        var crossings = grid.BorderCrossings
            .Where(c => c.Kind == BorderCrossingKind.River)
            .OrderBy(c => ProvinceBorderLayout.BearingFromCenter(grid, c.CellIndex))
            .ThenBy(c => c.CellIndex)
            .ToArray();

        int paired = crossings.Length - crossings.Length % 2;
        int half = paired / 2;

        // Appariement avec le point de passage « opposé » autour du cadre :
        // la rivière traverse la province.
        for (int k = 0; k < half; k++)
        {
            tracer.Trace(
                tracer.OuterCorner(crossings[k].CellIndex),
                tracer.OuterCorner(crossings[k + half].CellIndex));
        }

        // Un point de passage resté seul : la rivière naît à une source, sur une hauteur.
        if (paired < crossings.Length)
        {
            int crossingCell = crossings[^1].CellIndex;
            int sourceCell = tracer.ChooseSource(crossingCell);

            tracer.Trace(
                tracer.OuterCorner(crossingCell),
                tracer.CornerNode(grid.GetCoord(sourceCell), 0));
        }
    }

    private void Trace(int startCorner, int goalCorner)
    {
        var path = TerrainPathfinder.FindPath(this, startCorner, goalCorner);

        if (path is null)
            return;

        for (int i = 1; i < path.Count; i++)
        {
            if (!TryGetEdge(path[i - 1], path[i], out var cell, out int direction))
                throw new InvalidOperationException("River path is not continuous.");

            _grid.SetRiverEdge(cell, direction);
        }
    }

    public void GetEdges(int node, List<(int Node, double Cost)> buffer)
    {
        DecodeCorner(node, out var owner, out int slot);

        for (int k = 0; k < 3; k++)
        {
            Incident(owner, slot, k, out var other, out int otherSlot,
                out var edgeCell, out int edgeDirection);

            if (!TryCornerNode(other, otherSlot, out int otherNode))
                continue;

            if (!TryEdgeCost(edgeCell, edgeDirection, out double cost))
                continue;

            buffer.Add((otherNode, cost));
        }
    }

    public double EstimateCost(int from, int to)
    {
        var (ax, ay) = CornerPosition(from);
        var (bx, by) = CornerPosition(to);

        // Chaque arête mesure un rayon circonscrit et coûte au moins 1.
        return double.Hypot(bx - ax, by - ay)
            / ProvinceHexGrid.CircumradiusMeters;
    }

    /// <summary>
    /// Les trois arêtes d'un coin.
    /// Coin nord de (q,r) : vers les coins sud de (q,r+1), (q−1,r+1) et (q−1,r+2).
    /// Coin sud de (q,r) : vers les coins nord de (q,r−1), (q+1,r−1) et (q+1,r−2).
    /// </summary>
    private static void Incident(
        HexCoord owner,
        int slot,
        int index,
        out HexCoord other,
        out int otherSlot,
        out HexCoord edgeCell,
        out int edgeDirection)
    {
        int q = owner.Q;
        int r = owner.R;

        if (slot == 0)
        {
            otherSlot = 1;

            (other, edgeCell, edgeDirection) = index switch
            {
                0 => (new HexCoord(q, r + 1), owner, 1),
                1 => (new HexCoord(q - 1, r + 1), owner, 2),
                _ => (new HexCoord(q - 1, r + 2), new HexCoord(q - 1, r + 1), 0)
            };
        }
        else
        {
            otherSlot = 0;

            (other, edgeCell, edgeDirection) = index switch
            {
                0 => (new HexCoord(q, r - 1), owner, 4),
                1 => (new HexCoord(q + 1, r - 1), owner, 5),
                _ => (new HexCoord(q + 1, r - 2), new HexCoord(q, r - 1), 0)
            };
        }
    }

    private bool TryGetEdge(int from, int to, out HexCoord cell, out int direction)
    {
        DecodeCorner(from, out var owner, out int slot);

        for (int k = 0; k < 3; k++)
        {
            Incident(owner, slot, k, out var other, out int otherSlot,
                out cell, out direction);

            if (TryCornerNode(other, otherSlot, out int node) && node == to)
                return true;
        }

        cell = default;
        direction = -1;
        return false;
    }

    private bool TryEdgeCost(HexCoord cell, int direction, out double cost)
    {
        bool hasA = _grid.TryGetIndex(cell, out int a);
        bool hasB = _grid.TryGetIndex(cell.Neighbor(direction), out int b);

        if (!hasA && !hasB)
        {
            cost = 0;
            return false;
        }

        double elevation = hasA && hasB
            ? (_normalizedElevation[a] + _normalizedElevation[b]) / 2
            : _normalizedElevation[hasA ? a : b];

        // Représentation canonique du bord, pour un bruit identique dans les deux sens.
        var canonicalCell = cell;
        int canonicalDirection = direction;

        if (direction >= 3)
        {
            canonicalCell = cell.Neighbor(direction);
            canonicalDirection = HexCoord.Opposite(direction);
        }

        ulong hash = DeterministicRandom.Hash(
            DeterministicRandom.Hash(_seed, canonicalCell.Q, canonicalCell.R),
            canonicalDirection);

        cost = 1
            + ElevationWeight * elevation
            + MeanderWeight * DeterministicRandom.ToUniform(hash)
            + (hasA && hasB ? 0 : FrameEdgePenalty);

        return true;
    }

    /// <summary>Coin d'une case du bord qui touche l'extérieur du cadre, le plus éloigné du centre.</summary>
    private int OuterCorner(int cell)
    {
        var c = _grid.GetCoord(cell);

        // Coins de la case : nord, sud, puis ceux portés par les voisines.
        (HexCoord Owner, int Slot)[] corners =
        {
            (c, 0),
            (c, 1),
            (new HexCoord(c.Q, c.R + 1), 1),
            (new HexCoord(c.Q - 1, c.R + 1), 1),
            (new HexCoord(c.Q, c.R - 1), 0),
            (new HexCoord(c.Q + 1, c.R - 1), 0)
        };

        int best = -1;
        double bestDistance = double.NegativeInfinity;

        foreach (var (owner, slot) in corners)
        {
            if (!TouchesOutside(owner, slot)
                || !TryCornerNode(owner, slot, out int node))
            {
                continue;
            }

            var (x, y) = CornerPosition(node);

            double distance = double.Hypot(
                x - _grid.FrameCenterXMeters,
                y - _grid.FrameCenterYMeters);

            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = node;
            }
        }

        return best >= 0 ? best : CornerNode(c, 0);
    }

    private bool TouchesOutside(HexCoord owner, int slot)
    {
        var first = slot == 0
            ? new HexCoord(owner.Q, owner.R + 1)
            : new HexCoord(owner.Q, owner.R - 1);

        var second = slot == 0
            ? new HexCoord(owner.Q - 1, owner.R + 1)
            : new HexCoord(owner.Q + 1, owner.R - 1);

        return !_grid.TryGetIndex(owner, out _)
            || !_grid.TryGetIndex(first, out _)
            || !_grid.TryGetIndex(second, out _);
    }

    private int ChooseSource(int crossingCell)
    {
        ulong state = DeterministicRandom.Hash(_seed, crossingCell);
        var crossing = _grid.GetCoord(crossingCell);

        int best = -1;
        int fallback = -1;

        for (int i = 0; i < SourceCandidates; i++)
        {
            int cell = DeterministicRandom.NextInt(ref state, 0, _grid.CellCount - 1);

            if (fallback < 0
                || _normalizedElevation[cell] > _normalizedElevation[fallback])
            {
                fallback = cell;
            }

            if (_grid.IsFrameBoundary(cell)
                || _grid.GetCoord(cell).DistanceTo(crossing) < MinimumSourceDistance)
            {
                continue;
            }

            if (best < 0 || _normalizedElevation[cell] > _normalizedElevation[best])
                best = cell;
        }

        return best >= 0 ? best : fallback;
    }

    internal int CornerNode(HexCoord owner, int slot)
    {
        if (!TryCornerNode(owner, slot, out int node))
            throw new ArgumentOutOfRangeException(nameof(owner));

        return node;
    }

    private bool TryCornerNode(HexCoord owner, int slot, out int node)
    {
        int row = owner.R;
        int column = owner.Q + (row - (row & 1)) / 2;

        int extendedRow = row + 1;
        int extendedColumn = column + 1;

        if (extendedRow < 0 || extendedRow >= _extendedRows
            || extendedColumn < 0 || extendedColumn >= _extendedColumns)
        {
            node = -1;
            return false;
        }

        node = (extendedRow * _extendedColumns + extendedColumn) * 2 + slot;
        return true;
    }

    private void DecodeCorner(int node, out HexCoord owner, out int slot)
    {
        slot = node & 1;

        int extended = node >> 1;
        int row = extended / _extendedColumns - 1;
        int column = extended % _extendedColumns - 1;

        owner = new HexCoord(column - (row - (row & 1)) / 2, row);
    }

    private (double X, double Y) CornerPosition(int node)
    {
        DecodeCorner(node, out var owner, out int slot);

        double x = owner.CenterXMeters(ProvinceHexGrid.SpacingMeters);
        double y = owner.CenterYMeters(ProvinceHexGrid.SpacingMeters);

        return slot == 0
            ? (x, y + ProvinceHexGrid.CircumradiusMeters)
            : (x, y - ProvinceHexGrid.CircumradiusMeters);
    }
}
