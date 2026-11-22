
namespace WoF.Simulation.Military.Combat.Tactical;

public enum TacticalSightStatus
{
    Unknown,
    ClearOfKnownObstacles,
    BlockedByObstacle,
    BlockedByTerrain
}

public sealed record TacticalSightResult(
    TacticalSightStatus Status,
    double? HorizontalDistanceMeters,
    TacticalLineObstacle? BlockingObstacle,
    double? ObstructionDistanceMeters);

/// <summary>
/// Évaluation géométrique d'un corridor d'observation.
///
/// Bloquent la vue : les obstacles tracés à la main et, quand la
/// géométrie est reliée à une grille, le sol et le couvert des cases
/// traversées. La première obstruction le long du corridor l'emporte.
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

        return EvaluatePoints(
            geometry,
            first.Point,
            second.Point,
            observerEyeHeightMeters,
            targetHeightMeters);
    }

    /// <summary>
    /// Corridor entre deux points quelconques de la carte : l'œil de
    /// l'observateur au-dessus du point a, le point visé au-dessus de b.
    /// Sert à viser une extrémité du front d'une compagnie (option b).
    ///
    /// Les hauteurs ne sont pas revérifiées : l'appelant les fournit
    /// finies et positives, comme Evaluate le contrôle.
    /// </summary>
    internal static TacticalSightResult EvaluatePoints(
        TacticalGeometryMap geometry,
        TacticalPoint a,
        TacticalPoint b,
        double observerEyeHeightMeters = 1.7,
        double targetHeightMeters = 1.7)
    {
        ArgumentNullException.ThrowIfNull(geometry);

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

        // Le relief n'est cherché qu'avant le premier obstacle tracé :
        // à égalité, l'obstacle tracé l'emporte.
        if (geometry.Terrain is TacticalTerrainSurface terrain
            && TryFindTerrainObstruction(
                terrain,
                a,
                b,
                horizontal,
                startHeight,
                endHeight,
                closestParameter,
                out double terrainParameter))
        {
            return new TacticalSightResult(
                TacticalSightStatus.BlockedByTerrain,
                horizontal,
                null,
                horizontal * terrainParameter);
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

    /// <summary>
    /// Parcourt le corridor par pas d'au plus 25 m, extrémités exclues.
    /// En chaque point, la ligne de vue est comparée au sol interpolé,
    /// augmenté du couvert de la case traversée. Le couvert des cases de
    /// l'observateur et de la cible n'est pas compté : la dissimulation
    /// de la cible dans sa propre case relève de sa signature.
    ///
    /// Renvoie le premier paramètre bloquant, strictement inférieur à
    /// <paramref name="beforeParameter"/>.
    /// </summary>
    private static bool TryFindTerrainObstruction(
        TacticalTerrainSurface terrain,
        TacticalPoint a,
        TacticalPoint b,
        double horizontal,
        double startHeight,
        double endHeight,
        double beforeParameter,
        out double parameter)
    {
        parameter = double.PositiveInfinity;

        int steps = (int)Math.Ceiling(
            horizontal / TacticalTerrainSurface.SightSampleSpacingMeters);

        if (steps < 2)
            return false;

        int observerCell = terrain.FindCell(a.XMeters, a.YMeters);
        int targetCell = terrain.FindCell(b.XMeters, b.YMeters);

        double dx = b.XMeters - a.XMeters;
        double dy = b.YMeters - a.YMeters;
        double slope = endHeight - startHeight;

        for (int i = 1; i < steps; i++)
        {
            double t = (double)i / steps;

            if (t >= beforeParameter)
                return false;

            double x = a.XMeters + dx * t;
            double y = a.YMeters + dy * t;

            if (!terrain.TryGetGroundElevation(x, y, out double top))
                continue;

            int cell = terrain.FindCell(x, y);

            if (cell >= 0 && cell != observerCell && cell != targetCell)
                top += terrain.GetCoverHeight(cell);

            if (top + HeightEpsilonMeters >= startHeight + slope * t)
            {
                parameter = t;
                return true;
            }
        }

        return false;
    }

    private static double Cross(
        double ax,
        double ay,
        double bx,
        double by)
    {
        return ax * by - ay * bx;
    }



    internal static bool IsClearWithoutObstacles(
        TacticalPoint observer,
        TacticalPoint target,
        double horizontalDistanceMeters)
    {
        // Reproduit les conditions de validite
        // de Evaluate() pour un corridor sans obstacle.
        //
        // Le controle de l'absence d'obstacles
        // appartient a l'appelant.

        return double.IsFinite(horizontalDistanceMeters)
            && horizontalDistanceMeters > ParameterEpsilon
            && double.IsFinite(
                observer.GroundElevationMeters + 1.7)
            && double.IsFinite(
                target.GroundElevationMeters + 1.7);
    }

}
