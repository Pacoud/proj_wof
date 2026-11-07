
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Parametres experimentaux de cohesion des compagnies.
/// Les valeurs sont des parametres de gameplay
/// a calibrer et non des statistiques historiques.
/// </summary>
internal sealed class TacticalCohesionProfile
{
    internal double LossPerCasualtyPercent { get; }
    internal double SustainedSuppressionThreshold { get; }
    internal double LossPerExcessPressurePoint { get; }
    internal double RecoveryPerQuietHour { get; }
    internal double RecoverySuppressionCeiling { get; }
    internal double MaxFireReductionFraction { get; }

    internal TacticalCohesionProfile(
        double lossPerCasualtyPercent,
        double sustainedSuppressionThreshold,
        double lossPerExcessPressurePoint,
        double recoveryPerQuietHour,
        double recoverySuppressionCeiling,
        double maxFireReductionFraction)
    {
        CheckNonnegative(
            lossPerCasualtyPercent,
            nameof(lossPerCasualtyPercent));

        CheckInRange(
            sustainedSuppressionThreshold, 0, 100,
            nameof(sustainedSuppressionThreshold));

        CheckNonnegative(
            lossPerExcessPressurePoint,
            nameof(lossPerExcessPressurePoint));

        CheckNonnegative(
            recoveryPerQuietHour,
            nameof(recoveryPerQuietHour));

        CheckInRange(
            recoverySuppressionCeiling, 0, 100,
            nameof(recoverySuppressionCeiling));

        CheckInRange(
            maxFireReductionFraction, 0, 1,
            nameof(maxFireReductionFraction));

        LossPerCasualtyPercent = lossPerCasualtyPercent;
        SustainedSuppressionThreshold =
            sustainedSuppressionThreshold;
        LossPerExcessPressurePoint =
            lossPerExcessPressurePoint;
        RecoveryPerQuietHour = recoveryPerQuietHour;
        RecoverySuppressionCeiling =
            recoverySuppressionCeiling;
        MaxFireReductionFraction =
            maxFireReductionFraction;
    }

    private static void CheckNonnegative(
        double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void CheckInRange(
        double value, double min, double max, string name)
    {
        if (!double.IsFinite(value)
            || value < min || value > max)
            throw new ArgumentOutOfRangeException(name);
    }
}
