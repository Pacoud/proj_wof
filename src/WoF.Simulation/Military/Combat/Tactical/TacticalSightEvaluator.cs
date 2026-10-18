
namespace WoF.Simulation.Military.Combat.Tactical;

public enum TacticalSightStatus
{
    Unknown,
    ClearOfKnownObstacles,
    BlockedByObstacle
}

public sealed record TacticalSightResult(
    TacticalSightStatus Status,
    double? HorizontalDistanceMeters,
    TacticalLineObstacle? BlockingObstacle,
    double? ObstructionDistanceMeters);

/// <summary>
/// Évaluation géométrique d'un corridor d'observation.
///
/// Ne simule ni détection effective, ni météo,
/// ni camouflage, ni portée d'une arme.
/// </summary>
public static class TacticalSightEvaluator
{
    private const double ParameterEpsilon = 1e-10;
    private const double HeightEpsilonMeters = 1e-7;

    public static TacticalSightResult Evaluate(
        TacticalGeometryMap geometry,
        CompanyDeployment observer,
        CompanyDeployment target,
        double observerEyeHeightMeters = 1.7,
        double targetHeightMeters = 1.7)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(target);

        if (ReferenceEquals(observer, target))
            throw new ArgumentException(
                "Observer and target must be different.");

        if (!double.IsFinite(observerEyeHeightMeters)
            || observerEyeHeightMeters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observerEyeHeightMeters));
        }

        if (!double.IsFinite(targetHeightMeters)
            || targetHeightMeters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetHeightMeters));
        }

        var first = geometry.GetPosition(observer);
        var second = geometry.GetPosition(target);

        if (first is null || second is null)
        {
            return new TacticalSightResult(
                TacticalSightStatus.Unknown,
                null, null, null);
        }

        TacticalPoint a = first.Point;
        TacticalPoint b = second.Point;

        double horizontal = a.HorizontalDistanceTo(b);

        if (!double.IsFinite(horizontal)
            || horizontal <= ParameterEpsilon)
        {
            return new TacticalSightResult(
                TacticalSightStatus.Unknown,
                double.IsFinite(horizontal) ? horizontal : null,
                null, null);
        }

        double startHeight =
            a.GroundElevationMeters + observerEyeHeightMeters;

        double endHeight =
            b.GroundElevationMeters + targetHeightMeters;

        if (!double.IsFinite(startHeight)
            || !double.IsFinite(endHeight))
        {
            return new TacticalSightResult(
                TacticalSightStatus.Unknown,
                horizontal, null, null);
        }

        TacticalLineObstacle? closestObstacle = null;
        double closestParameter = double.PositiveInfinity;

        foreach (var obstacle in geometry.LineObstacles)
        {
            if (!TryGetIntersectionInterval(
                a, b, obstacle,
                out double enter,
                out double exit))
            {
                continue;
            }

            double slope = endHeight - startHeight;

            double heightAtEntry =
                startHeight + slope * enter;

            double heightAtExit =
                startHeight + slope * exit;

            double top =
                obstacle.TopElevationMeters
                + HeightEpsilonMeters;

            double? blockingParameter = null;

            if (top >= heightAtEntry)
            {
                blockingParameter = enter;
            }
            else if (slope < 0 && top >= heightAtExit)
            {
                // Cas d'un obstacle colinéaire au corridor :
                // la ligne peut descendre sous le sommet
                // entre l'entrée et la sortie.
                blockingParameter = Math.Clamp(
                    (top - startHeight) / slope,
                    enter,
                    exit);
            }

            if (blockingParameter is double parameter
                && parameter < closestParameter)
            {
                closestParameter = parameter;
                closestObstacle = obstacle;
            }
        }

        if (closestObstacle is not null)
        {
            return new TacticalSightResult(
                TacticalSightStatus.BlockedByObstacle,
                horizontal,
                closestObstacle,
                horizontal * closestParameter);
        }

        return new TacticalSightResult(
            TacticalSightStatus.ClearOfKnownObstacles,
            horizontal,
            null, null);
    }

    /// <summary>
    /// Calcule l'intervalle [enter, exit] le long
    /// du corridor A -> B qui croise le segment
    /// représentant l'obstacle.
    ///
    /// Dans un croisement ordinaire, enter == exit.
    /// Dans un chevauchement colinéaire, l'intervalle
    /// peut avoir une longueur positive.
    ///
    /// Les extrémités du corridor sont exclues pour
    /// éviter de masquer une unité par un obstacle
    /// situé exactement sur son point d'observation.
    /// </summary>
    private static bool TryGetIntersectionInterval(
        TacticalPoint a,
        TacticalPoint b,
        TacticalLineObstacle obstacle,
        out double enter,
        out double exit)
    {
        enter = 0;
        exit = 0;

        double rx = b.XMeters - a.XMeters;
        double ry = b.YMeters - a.YMeters;

        double sx =
            obstacle.EndXMeters - obstacle.StartXMeters;

        double sy =
            obstacle.EndYMeters - obstacle.StartYMeters;

        double qx = obstacle.StartXMeters - a.XMeters;
        double qy = obstacle.StartYMeters - a.YMeters;

        double denominator = Cross(rx, ry, sx, sy);

        double parallelTolerance =
            ParameterEpsilon * Math.Max(
                1.0,
                double.Hypot(rx, ry) *
                double.Hypot(sx, sy));

        if (Math.Abs(denominator) > parallelTolerance)
        {
            double t = Cross(qx, qy, sx, sy)
                / denominator;

            double u = Cross(qx, qy, rx, ry)
                / denominator;

            if (t <= ParameterEpsilon
                || t >= 1 - ParameterEpsilon
                || u < -ParameterEpsilon
                || u > 1 + ParameterEpsilon)
            {
                return false;
            }

            enter = t;
            exit = t;
            return true;
        }

        // Segments parallèles : vérifier l'alignement.
        double collinearTolerance =
            ParameterEpsilon * Math.Max(
                1.0,
                double.Hypot(qx, qy) *
                double.Hypot(rx, ry));

        if (Math.Abs(Cross(qx, qy, rx, ry))
            > collinearTolerance)
        {
            return false;
        }

        double lengthSquared = rx * rx + ry * ry;

        double t0 =
            (qx * rx + qy * ry) / lengthSquared;

        double endQx = obstacle.EndXMeters - a.XMeters;
        double endQy = obstacle.EndYMeters - a.YMeters;

        double t1 =
            (endQx * rx + endQy * ry)
            / lengthSquared;

        enter = Math.Max(
            ParameterEpsilon,
            Math.Min(t0, t1));

        exit = Math.Min(
            1 - ParameterEpsilon,
            Math.Max(t0, t1));

        return enter <= exit;
    }

    private static double Cross(
        double ax,
        double ay,
        double bx,
        double by)
    {
        return ax * by - ay * bx;
    }
}
