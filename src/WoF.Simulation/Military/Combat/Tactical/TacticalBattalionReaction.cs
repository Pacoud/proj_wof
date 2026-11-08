
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

internal enum TacticalBattalionFrontState
{
    Unassigned,
    Holding,
    Threatened,
    RuptureConfirmed
}

internal enum TacticalBattalionReactionType
{
    Reorganize,
    Reinforce,
    RequestWithdrawal
}

/// <summary>
/// Demande envoyee au futur commandement tactique.
/// Ce n'est PAS un mouvement execute.
/// </summary>
internal sealed record TacticalBattalionReactionRequest(
    long Hour,
    Battalion Battalion,
    BattleSector Sector,
    TacticalBattalionReactionType Type,
    IReadOnlyList<CompanyDeployment> AffectedFrontline,
    IReadOnlyList<CompanyDeployment> ReserveCandidates);

internal sealed record TacticalBattalionReactionReport(
    Battalion Battalion,
    BattleSector? Sector,
    TacticalBattalionFrontState PreviousState,
    TacticalBattalionFrontState State,
    double? SectorReadiness,
    int ConsecutiveCriticalHours,
    int ConsecutiveRecoveryHours,
    TacticalBattalionReactionRequest? NewRequest);

internal sealed record TacticalBattalionReactionPhaseResult(
    long Hour,
    IReadOnlyList<TacticalBattalionReactionReport> Battalions,
    IReadOnlyList<TacticalBattalionReactionRequest> Requests);
