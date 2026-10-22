
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Estimation de projectiles passant dans
/// l'emprise dangereuse d'une compagnie.
/// La cible reelle reste interne au moteur.
/// </summary>
internal readonly record struct TacticalSuppressionThreat(
    CompanyDeployment Target,
    long RoundsExpended,
    double DangerZoneProbability,
    double ExpectedThreateningRounds);

internal static class TacticalSuppressionResolver
{
    internal static TacticalSuppressionThreat Evaluate(
        TacticalFireSnapshot shot,
        TacticalHitProfile hitProfile,
        TacticalSuppressionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(hitProfile);
        ArgumentNullException.ThrowIfNull(profile);

        var fire = shot.FireEvent
            ?? throw new ArgumentException(
                "Missing fire event.", nameof(shot));

        if (shot.RealTarget is null
            || fire.RoundsExpended <= 0
            || !double.IsFinite(shot.TargetFrontageMeters)
            || shot.TargetFrontageMeters <= 0
            || !double.IsFinite(shot.TargetFacingDegrees)
            || shot.TargetFacingDegrees < 0
            || shot.TargetFacingDegrees >= 360)
        {
            throw new ArgumentException(
                "Invalid fire snapshot.", nameof(shot));
        }

        if (shot.TargetPersonnel.Exposed == 0)
        {
            return new TacticalSuppressionThreat(
                shot.RealTarget,
                fire.RoundsExpended,
                0,
                0);
        }

        double range = shot.ShooterPoint
            .SpatialDistanceTo(shot.ReportedTargetPoint);

        double sigma = double.Hypot(
            hitProfile.MinimumDispersionMeters,
            hitProfile.DispersionAt100Meters
                * range / 100.0);

        if (!double.IsFinite(sigma) || sigma <= 0)
            throw new InvalidOperationException(
                "Invalid shot dispersion.");

        double angle =
            shot.TargetFacingDegrees * Math.PI / 180.0;

        double lx = Math.Cos(angle);
        double ly = -Math.Sin(angle);
        double fx = Math.Sin(angle);
        double fy = Math.Cos(angle);

        double dx = shot.ReportedTargetPoint.XMeters
            - shot.RealTargetPoint.XMeters;

        double dy = shot.ReportedTargetPoint.YMeters
            - shot.RealTargetPoint.YMeters;

        double lateralOffset = dx * lx + dy * ly;
        double forwardOffset = dx * fx + dy * fy;

        double halfWidth =
            shot.TargetFrontageMeters / 2.0
            + profile.DangerMarginMeters;

        double halfDepth =
            shot.TargetPersonnel.ExposedDepthMeters / 2.0
            + profile.DangerMarginMeters;

        double lateralProbability = NormalInterval(
            -halfWidth,
            halfWidth,
            lateralOffset,
            sigma);

        double depthProbability = NormalInterval(
            -halfDepth,
            halfDepth,
            forwardOffset,
            sigma);

        double probability = Math.Clamp(
            lateralProbability * depthProbability,
            0.0,
            1.0);

        double expected =
            fire.RoundsExpended * probability;

        return new TacticalSuppressionThreat(
            shot.RealTarget,
            fire.RoundsExpended,
            probability,
            expected);
    }

    private static double NormalInterval(
        double lower,
        double upper,
        double mean,
        double sigma)
    {
        return Math.Clamp(
            NormalCdf((upper - mean) / sigma)
            - NormalCdf((lower - mean) / sigma),
            0.0,
            1.0);
    }

    // Meme approximation numerique de la loi normale
    // que le calcul des contacts de C2.4c.4.
    private static double NormalCdf(double x)
    {
        double absolute = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.2316419 * absolute);

        double polynomial = t * (
            0.319381530 + t * (
           -0.356563782 + t * (
            1.781477937 + t * (
           -1.821255978 + t * 1.330274429))));

        double tail =
            Math.Exp(-0.5 * absolute * absolute)
            / Math.Sqrt(2.0 * Math.PI)
            * polynomial;

        return x >= 0
            ? 1.0 - tail
            : tail;
    }
}
