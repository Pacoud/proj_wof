namespace WoF.Simulation.World.Terrain;

internal readonly record struct CoveredCell(int CellIndex, double LengthMeters);

internal static class HexFrontCoverage
{
    internal const double MinimumFrontCoverageMeters = 20;

    private const double Spacing = ProvinceHexGrid.SpacingMeters;
    private const double HalfWidth = Spacing / 2;
    private const double Radius = ProvinceHexGrid.CircumradiusMeters;
    private const double RowStep = Spacing * HexCoord.RowSpacingFactor;

    // Normales sortantes, dans l'ordre de HexCoord.Neighbor.
    private static readonly (double X, double Y)[] Normals =
    [
        (1, 0), (0.5, HexCoord.RowSpacingFactor), (-0.5, HexCoord.RowSpacingFactor),
        (-1, 0), (-0.5, -HexCoord.RowSpacingFactor), (0.5, -HexCoord.RowSpacingFactor)
    ];

    internal static IReadOnlyList<CoveredCell> Measure(
        ProvinceHexGrid grid, (double X, double Y) a, (double X, double Y) b)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!double.IsFinite(a.X) || !double.IsFinite(a.Y))
            throw new ArgumentOutOfRangeException(nameof(a));
        if (!double.IsFinite(b.X) || !double.IsFinite(b.Y))
            throw new ArgumentOutOfRangeException(nameof(b));

        // Même parcours et mêmes arrondis quand on échange les extrémités.
        if (a.Y > b.Y || (a.Y == b.Y && a.X > b.X))
            (a, b) = (b, a);

        double dx = b.X - a.X, dy = b.Y - a.Y;
        double length = double.Hypot(dx, dy);
        if (!double.IsFinite(length))
            throw new ArgumentOutOfRangeException(nameof(b));
        if (length == 0)
            return Array.Empty<CoveredCell>();

        var result = new List<CoveredCell>();
        int firstRow = LowerBound((a.Y - Radius) / RowStep, grid.Rows);
        int lastRow = UpperBound((b.Y + Radius) / RowStep, grid.Rows);

        for (int row = firstRow; row <= lastRow; row++)
        {
            double centerY = row * RowStep;
            double x1 = a.X, x2 = b.X;
            if (dy > 0)
            {
                // Portion du segment à la hauteur de cette rangée, pointes incluses.
                x1 = a.X + dx * Math.Clamp((centerY - Radius - a.Y) / dy, 0, 1);
                x2 = a.X + dx * Math.Clamp((centerY + Radius - a.Y) / dy, 0, 1);
            }

            double shift = (row & 1) * HalfWidth;
            int firstCol = LowerBound((Math.Min(x1, x2) - HalfWidth - shift) / Spacing, grid.Columns);
            int lastCol = UpperBound((Math.Max(x1, x2) + HalfWidth - shift) / Spacing, grid.Columns);

            for (int col = firstCol; col <= lastCol; col++)
            {
                int cell = row * grid.Columns + col;
                double meters = LengthInCell(grid, cell, a, b, length);
                if (meters >= MinimumFrontCoverageMeters)
                    result.Add(new CoveredCell(cell, meters));
            }
        }
        return result;
    }

    // Arrondi vers l'extérieur. Une borne hors grille peut donner une plage vide.
    private static int LowerBound(double value, int count) =>
        (int)Math.Clamp(Math.Floor(value), 0, count);

    private static int UpperBound(double value, int count) =>
        (int)Math.Clamp(Math.Ceiling(value), -1, count - 1);

    private static double LengthInCell(ProvinceHexGrid grid, int cell,
        (double X, double Y) a, (double X, double Y) b, double length)
    {
        double cx = grid.GetCenterXMeters(cell), cy = grid.GetCenterYMeters(cell);
        double enter = 0, exit = length;

        // Quelques unités du dernier chiffre représentable à l'échelle de la case.
        double scale = Math.Max(Spacing, Math.Max(Math.Abs(cx), Math.Abs(cy)));
        double roundoff = 8 * (Math.BitIncrement(scale) - scale);

        for (int dir = 0; dir < Normals.Length; dir++)
        {
            var (nx, ny) = Normals[dir];
            double da = (a.X - cx) * nx + (a.Y - cy) * ny - HalfWidth;
            double db = (b.X - cx) * nx + (b.Y - cy) * ny - HalfWidth;

            // Une même portion d'arête ne peut appartenir qu'à une case.
            double tolerance = ny == 0 ? 0 : roundoff;
            if (Math.Abs(da) <= tolerance && Math.Abs(db) <= tolerance)
            {
                int neighbor = grid.GetNeighborIndex(cell, dir);
                if (neighbor >= 0 && neighbor < cell)
                    return 0;
                continue;
            }

            if (da > 0 && db > 0)
                return 0;
            if (da <= 0 && db <= 0)
                continue;

            // Distance en mètres depuis a jusqu'à l'intersection avec ce côté.
            double crossing = length * (da / (da - db));
            if (da > 0)
                enter = Math.Max(enter, crossing);
            else
                exit = Math.Min(exit, crossing);
            if (exit <= enter)
                return 0;
        }
        double meters = exit - enter;
        // Stabilise le seuil inclusif lorsque la coupe oblique tombe à 20 m.
        return Math.Abs(meters - MinimumFrontCoverageMeters) <= roundoff
            ? MinimumFrontCoverageMeters
            : meters;
    }
}