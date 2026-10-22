
namespace WoF.Simulation.Military.Combat;

/// <summary>
/// Moteur autorise a produire les effets militaires
/// pour un engagement donne.
///
/// LegacyOperational est temporaire : le but final
/// est de migrer tous les engagements vers Tactical.
/// </summary>
public enum EngagementCombatMode
{
    LegacyOperational,
    Tactical
}
