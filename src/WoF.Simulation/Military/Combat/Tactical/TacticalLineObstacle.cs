
namespace WoF.Simulation.Military.Combat.Tactical;

public enum TacticalLineObstacleKind
{
    Ridge,
    Wall
}

/// <summary>
/// Obstacle représenté par un segment horizontal.
/// TopElevationMeters est une altitude absolue,
/// dans le même repère que TacticalPoint.
/// </summary>
public sealed class TacticalLineObstacle
{
    public int Id { get; }
    public string Name { get; }
    public TacticalLineObstacleKind Kind { get; }

    public double StartXMeters { get; }
    public double StartYMeters { get; }
    public double EndXMeters { get; }
    public double EndYMeters { get; }

    public double TopElevationMeters { get; }

    public TacticalLineObstacle(
        int id,
        string name,
        TacticalLineObstacleKind kind,
        double startXMeters,
        double startYMeters,
        double endXMeters,
        double endYMeters,
        double topElevationMeters)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Obstacle name cannot be empty.",
                nameof(name));

        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        double[] values =
        {
            startXMeters,
            startYMeters,
            endXMeters,
            endYMeters,
            topElevationMeters
        };

        if (values.Any(v => !double.IsFinite(v)))
            throw new ArgumentOutOfRangeException(
                nameof(startXMeters),
                "Obstacle coordinates and elevation must be finite.");

        double length = double.Hypot(
            endXMeters - startXMeters,
            endYMeters - startYMeters);

        if (!double.IsFinite(length) || length <= 0)
            throw new ArgumentException(
                "Obstacle segment must have a finite, non-zero length.");

        Id = id;
        Name = name;
        Kind = kind;
        StartXMeters = startXMeters;
        StartYMeters = startYMeters;
        EndXMeters = endXMeters;
        EndYMeters = endYMeters;
        TopElevationMeters = topElevationMeters;
    }
}
