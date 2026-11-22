namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Coordonnée axiale d'une case hexagonale, pointe en haut.
///
/// Directions, dans le sens trigonométrique :
/// 0 = est, 1 = nord-est, 2 = nord-ouest,
/// 3 = ouest, 4 = sud-ouest, 5 = sud-est.
/// La direction opposée à d est (d + 3) % 6.
///
/// Centre en mètres : X = s·(Q + R/2), Y = s·√3/2·R,
/// avec X vers l'est et Y vers le nord.
/// </summary>
public readonly record struct HexCoord(int Q, int R)
{
    public const int DirectionCount = 6;

    /// <summary>√3 / 2 : rapport entre l'écart de deux rangées et l'écart de deux centres.</summary>
    public const double RowSpacingFactor = 0.86602540378443864676;

    private static readonly (int Dq, int Dr)[] Offsets =
    {
        (1, 0),
        (0, 1),
        (-1, 1),
        (-1, 0),
        (0, -1),
        (1, -1)
    };

    public int S => -Q - R;

    public static int Opposite(int direction)
    {
        ValidateDirection(direction);

        return (direction + 3) % DirectionCount;
    }

    public HexCoord Neighbor(int direction)
    {
        ValidateDirection(direction);

        var (dq, dr) = Offsets[direction];

        return new HexCoord(Q + dq, R + dr);
    }

    public int DistanceTo(HexCoord other)
    {
        int dq = Math.Abs(other.Q - Q);
        int dr = Math.Abs(other.R - R);
        int ds = Math.Abs(other.S - S);

        return (dq + dr + ds) / 2;
    }

    public double CenterXMeters(double spacingMeters)
    {
        return spacingMeters * (Q + R / 2.0);
    }

    public double CenterYMeters(double spacingMeters)
    {
        return spacingMeters * RowSpacingFactor * R;
    }

    private static void ValidateDirection(int direction)
    {
        if (direction < 0 || direction >= DirectionCount)
            throw new ArgumentOutOfRangeException(
                nameof(direction));
    }
}
