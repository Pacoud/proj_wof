
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Stabilise les decisions tactiques d'un bataillon.
/// Ces valeurs sont experimentales.
/// </summary>
internal sealed class TacticalBattalionReactionProfile
{
    internal int CriticalConfirmationHours { get; }
    internal int RecoveryConfirmationHours { get; }
    internal double RecoveryMargin { get; }

    internal TacticalBattalionReactionProfile(
        int criticalConfirmationHours,
        int recoveryConfirmationHours,
        double recoveryMargin)
    {
        if (criticalConfirmationHours < 1)
            throw new ArgumentOutOfRangeException(
                nameof(criticalConfirmationHours));

        if (recoveryConfirmationHours < 1)
            throw new ArgumentOutOfRangeException(
                nameof(recoveryConfirmationHours));

        if (!double.IsFinite(recoveryMargin)
            || recoveryMargin < 0
            || recoveryMargin > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recoveryMargin));
        }

        CriticalConfirmationHours =
            criticalConfirmationHours;

        RecoveryConfirmationHours =
            recoveryConfirmationHours;

        RecoveryMargin = recoveryMargin;
    }

    internal void ValidateFor(
        TacticalBattalionAssessmentProfile assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        if (assessment.HoldThreshold + RecoveryMargin > 100)
        {
            throw new InvalidOperationException(
                "Recovery threshold exceeds 100.");
        }
    }
}
