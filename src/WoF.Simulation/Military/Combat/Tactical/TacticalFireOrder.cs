
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Ordre interne : la cible reelle reste connue
/// uniquement du moteur de simulation.
/// </summary>
internal sealed record TacticalFireOrder(
    CompanyDeployment Shooter,
    CompanyDeployment Target,
    TacticalDirectFireProfile Profile,
    int WeaponsCommitted,
    int RoundsPerWeapon);

internal enum TacticalFireOrderStatus
{
    Fired,
    AuthorizationDenied,
    InsufficientWeapons,
    InsufficientAmmunition
}

/// <summary>
/// Resultat d'un ordre, dans l'ordre des demandes.
/// </summary>
internal readonly record struct TacticalFireOrderOutcome(
    TacticalFireOrderStatus Status,
    TacticalDirectFireStatus AuthorizationStatus);

/// <summary>
/// Salve executee. La cible est representee par
/// une piste de renseignement, sans reference
/// publique a la compagnie ennemie reelle.
/// </summary>
public sealed record TacticalFireEvent(
    long Hour,
    CompanyDeployment Shooter,
    int TargetTrackId,
    AmmunitionType AmmunitionType,
    int WeaponsCommitted,
    long RoundsExpended);

/// <summary>
/// Resultats et evenements d'une phase de tir.
/// </summary>
internal sealed record TacticalFirePhaseResult(
    long Hour,
    IReadOnlyList<TacticalFireOrderOutcome> Outcomes,
    IReadOnlyList<TacticalFireEvent> Events);
