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

    /// <summary>
    /// Coordonnées axiales fractionnaires d'un point en mètres :
    /// inverse exact de CenterXMeters et CenterYMeters.
    /// </summary>
    internal static (double Q, double R) FractionalFromMeters(
        double xMeters,
        double yMeters,
        double spacingMeters)
    {
        double r = yMeters / (spacingMeters * RowSpacingFactor);
        double q = xMeters / spacingMeters - r / 2.0;

        return (q, r);
    }

    /// <summary>
    /// Case dont l'hexagone contient le point (arrondi cubique).
    /// Un point situé exactement sur un bord est attribué de façon
    /// déterministe à l'une des deux cases.
    /// </summary>
    public static HexCoord FromMeters(
        double xMeters,
        double yMeters,
        double spacingMeters)
    {
        if (!double.IsFinite(xMeters))
            throw new ArgumentOutOfRangeException(nameof(xMeters));

        if (!double.IsFinite(yMeters))
            throw new ArgumentOutOfRangeException(nameof(yMeters));

        if (!double.IsFinite(spacingMeters) || spacingMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(spacingMeters));

        var (q, r) = FractionalFromMeters(xMeters, yMeters, spacingMeters);
        double s = -q - r;

        if (Math.Abs(q) > MaxCoordinate || Math.Abs(r) > MaxCoordinate)
            throw new ArgumentOutOfRangeException(
                nameof(xMeters), "Point is too far from the origin.");

        double roundedQ = Math.Round(q);
        double roundedR = Math.Round(r);
        double roundedS = Math.Round(s);

        double dq = Math.Abs(roundedQ - q);
        double dr = Math.Abs(roundedR - r);
        double ds = Math.Abs(roundedS - s);

        // La coordonnée la plus éloignée de son arrondi
        // est recalculée à partir des deux autres.
        if (dq > dr && dq > ds)
            roundedQ = -roundedR - roundedS;
        else if (dr > ds)
            roundedR = -roundedQ - roundedS;

        return new HexCoord((int)roundedQ, (int)roundedR);
    }

    private const double MaxCoordinate = 1e9;

    private static void ValidateDirection(int direction)
    {
        if (direction < 0 || direction >= DirectionCount)
            throw new ArgumentOutOfRangeException(
                nameof(direction));
    }
}
