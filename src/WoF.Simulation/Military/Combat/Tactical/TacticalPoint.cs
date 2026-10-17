
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Point dans le repère local d'une bataille.
/// X et Y sont en mètres.
/// L'altitude est exprimée en mètres.
/// X croît vers l'est, Y vers le nord.
/// </summary>
public readonly record struct TacticalPoint
{
    public double XMeters { get; }
    public double YMeters { get; }
    public double GroundElevationMeters { get; }

    public TacticalPoint(
        double xMeters,
        double yMeters,
        double groundElevationMeters)
    {
        if (!double.IsFinite(xMeters))
            throw new ArgumentOutOfRangeException(
                nameof(xMeters));

        if (!double.IsFinite(yMeters))
            throw new ArgumentOutOfRangeException(
                nameof(yMeters));

        if (!double.IsFinite(groundElevationMeters))
            throw new ArgumentOutOfRangeException(
                nameof(groundElevationMeters));

        XMeters = xMeters;
        YMeters = yMeters;
        GroundElevationMeters = groundElevationMeters;
    }

    public double HorizontalDistanceTo(TacticalPoint other)
    {
        double dx = other.XMeters - XMeters;
        double dy = other.YMeters - YMeters;

        return double.Hypot(dx, dy);
    }

    public double SpatialDistanceTo(TacticalPoint other)
    {
        double horizontal = HorizontalDistanceTo(other);

        double elevationDifference =
            other.GroundElevationMeters -
            GroundElevationMeters;

        return double.Hypot(
            horizontal,
            elevationDifference);
    }
}
