
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Consequences simulees sur UNE compagnie cible.
/// Ce bilan reste interne et n'applique pas encore
/// les pertes au PersonnelPool.
/// </summary>
internal readonly record struct TacticalPersonnelEffectResult(
    CompanyDeployment Target,
    int ExposedPersonnel,
    long ContactEvents,
    int DistinctStruck,
    int Killed,
    int WoundedUnavailable,
    int NoLastingIncapacity)
{
    internal int TotalOperationalCasualties =>
        checked(Killed + WoundedUnavailable);
}

/// <summary>
/// Consequences d'une phase de contacts.
/// Les ressources militaires ne sont pas modifiees.
/// </summary>
internal sealed record TacticalPersonnelEffectsPhaseResult(
    long Hour,
    ulong Seed,
    IReadOnlyList<TacticalPersonnelEffectResult> Targets);
