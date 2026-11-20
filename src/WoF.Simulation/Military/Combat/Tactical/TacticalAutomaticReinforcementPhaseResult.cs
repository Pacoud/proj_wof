
namespace WoF.Simulation.Military.Combat.Tactical;

internal enum TacticalAutomaticReinforcementStatus
{
    Approved,
    NoEligibleReserve,
    ApprovalFailed,
    SkippedBattleEnding
}

internal sealed record TacticalAutomaticReinforcementDecision(
    TacticalBattalionReactionRequest Request,
    TacticalAutomaticReinforcementStatus Status,
    TacticalSectorMovementOrder? Movement,
    string? FailureReason);

internal sealed record TacticalAutomaticReinforcementPhaseResult(
    long Hour,
    IReadOnlyList<TacticalAutomaticReinforcementDecision> Decisions);
