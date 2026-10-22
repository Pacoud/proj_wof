
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Parametres experimentaux du modele de suppression.
/// Aucune valeur historique n'est imposee.
/// </summary>
internal sealed class TacticalSuppressionProfile
{
    internal double DangerMarginMeters { get; }
    internal double PressurePerThreateningRound { get; }
    internal double RecoveryPerHour { get; }
    internal double MaxFireReductionFraction { get; }

    internal TacticalSuppressionProfile(
        double dangerMarginMeters,
        double pressurePerThreateningRound,
        double recoveryPerHour,
        double maxFireReductionFraction)
    {
        if (!double.IsFinite(dangerMarginMeters)
            || dangerMarginMeters < 0)
            throw new ArgumentOutOfRangeException(
                nameof(dangerMarginMeters));

        if (!double.IsFinite(pressurePerThreateningRound)
            || pressurePerThreateningRound < 0)
            throw new ArgumentOutOfRangeException(
                nameof(pressurePerThreateningRound));

        if (!double.IsFinite(recoveryPerHour)
            || recoveryPerHour < 0)
            throw new ArgumentOutOfRangeException(
                nameof(recoveryPerHour));

        if (!double.IsFinite(maxFireReductionFraction)
            || maxFireReductionFraction < 0
            || maxFireReductionFraction > 1)
            throw new ArgumentOutOfRangeException(
                nameof(maxFireReductionFraction));

        DangerMarginMeters = dangerMarginMeters;
        PressurePerThreateningRound =
            pressurePerThreateningRound;
        RecoveryPerHour = recoveryPerHour;
        MaxFireReductionFraction =
            maxFireReductionFraction;
    }
}
