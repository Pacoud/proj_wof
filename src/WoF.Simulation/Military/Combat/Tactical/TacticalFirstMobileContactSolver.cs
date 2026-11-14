
namespace WoF.Simulation.Military.Combat.Tactical;

internal readonly record struct TacticalFirstMobileContact(
    double TimeFraction,
    TacticalPoint FirstPosition,
    TacticalPoint SecondPosition,
    double DistanceMeters);

/// <summary>
/// Premier contact geometrique entre deux trajectoires
/// pendant leur intervalle temporel commun.
///
/// Aucun deplacement ni degat n'est applique ici.
/// </summary>
internal static class TacticalFirstMobileContactSolver
{
    internal static TacticalFirstMobileContact? Find(
        TacticalTimedTrajectorySegment first,
        TacticalTimedTrajectorySegment second,
        double radiusMeters)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (!double.IsFinite(radiusMeters)
            || radiusMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusMeters));
        }

        Validate(first);
        Validate(second);

        double start = Math.Max(
            first.StartTime, second.StartTime);

        double end = Math.Min(
            first.EndTime, second.EndTime);

        if (start > end)
            return null;

        TacticalPoint a = PositionAt(first, start);
        TacticalPoint b = PositionAt(second, start);

        double avx = VelocityX(first);
        double avy = VelocityY(first);

        double bvx = VelocityX(second);
        double bvy = VelocityY(second);

        double rx = a.XMeters - b.XMeters;
        double ry = a.YMeters - b.YMeters;

        double vx = avx - bvx;
        double vy = avy - bvy;

        double quadraticA = vx * vx + vy * vy;
        double quadraticB = 2 * (rx * vx + ry * vy);
        double quadraticC =
            rx * rx + ry * ry - radiusMeters * radiusMeters;

        double delta;

        if (quadraticC <= 0)
        {
            // Les compagnies sont deja dans le rayon.
            delta = 0;
        }
        else
        {
            if (quadraticA <= 0)
                return null;

            double discriminant =
                quadraticB * quadraticB
                - 4 * quadraticA * quadraticC;

            if (!double.IsFinite(discriminant))
                throw new InvalidOperationException(
                    "Contact calculation overflow.");

            if (discriminant < 0)
                return null;

            delta =
                (-quadraticB - Math.Sqrt(discriminant))
                / (2 * quadraticA);

            if (delta < -1e-10
                || delta > end - start + 1e-10)
            {
                return null;
            }

            delta = Math.Clamp(delta, 0, end - start);
        }

        double time = start + delta;

        TacticalPoint firstPosition =
            PositionAt(first, time);

        TacticalPoint secondPosition =
            PositionAt(second, time);

        double distance =
            firstPosition.HorizontalDistanceTo(secondPosition);

        if (!double.IsFinite(distance))
            throw new InvalidOperationException(
                "Invalid mobile contact distance.");

        return new TacticalFirstMobileContact(
            time,
            firstPosition,
            secondPosition,
            distance);
    }

    private static TacticalPoint PositionAt(
        TacticalTimedTrajectorySegment segment,
        double time)
    {
        double fraction = Math.Clamp(
            (time - segment.StartTime)
                / (segment.EndTime - segment.StartTime),
            0, 1);

        return new TacticalPoint(
            segment.From.XMeters
                + (segment.To.XMeters - segment.From.XMeters)
                * fraction,
            segment.From.YMeters
                + (segment.To.YMeters - segment.From.YMeters)
                * fraction,
            segment.From.GroundElevationMeters
                + (segment.To.GroundElevationMeters
                    - segment.From.GroundElevationMeters)
                * fraction);
    }

    private static double VelocityX(
        TacticalTimedTrajectorySegment segment)
    {
        return (segment.To.XMeters - segment.From.XMeters)
            / (segment.EndTime - segment.StartTime);
    }

    private static double VelocityY(
        TacticalTimedTrajectorySegment segment)
    {
        return (segment.To.YMeters - segment.From.YMeters)
            / (segment.EndTime - segment.StartTime);
    }

    private static void Validate(
        TacticalTimedTrajectorySegment segment)
    {
        if (!double.IsFinite(segment.StartTime)
            || !double.IsFinite(segment.EndTime)
            || segment.StartTime < 0
            || segment.EndTime > 1
            || segment.StartTime >= segment.EndTime)
        {
            throw new InvalidOperationException(
                "Invalid mobile trajectory interval.");
        }
    }
}
