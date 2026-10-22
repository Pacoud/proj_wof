
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Configuration experimentale du cycle de tir horaire.
/// Ne represente pas encore une doctrine historique.
/// </summary>
internal sealed class TacticalHourlyCombatSettings
{
    internal TacticalDetectionProfile Detection { get; }
    internal TacticalDirectFireProfile Fire { get; }
    internal TacticalHitProfile Hit { get; }
    internal TacticalDefenseProfile Defense { get; }
    internal TacticalPersonnelEffectProfile Effects { get; }

    internal int MaxWeaponsPerCompany { get; }
    internal int RoundsPerWeapon { get; }
    internal ulong BaseSeed { get; }

    internal TacticalHourlyCombatSettings(
        TacticalDetectionProfile detection,
        TacticalDirectFireProfile fire,
        TacticalHitProfile hit,
        TacticalDefenseProfile defense,
        TacticalPersonnelEffectProfile effects,
        int maxWeaponsPerCompany,
        int roundsPerWeapon,
        ulong baseSeed)
    {
        Detection = detection
            ?? throw new ArgumentNullException(nameof(detection));
        Fire = fire
            ?? throw new ArgumentNullException(nameof(fire));
        Hit = hit
            ?? throw new ArgumentNullException(nameof(hit));
        Defense = defense
            ?? throw new ArgumentNullException(nameof(defense));
        Effects = effects
            ?? throw new ArgumentNullException(nameof(effects));

        if (maxWeaponsPerCompany <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxWeaponsPerCompany));

        if (roundsPerWeapon <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(roundsPerWeapon));

        MaxWeaponsPerCompany = maxWeaponsPerCompany;
        RoundsPerWeapon = roundsPerWeapon;
        BaseSeed = baseSeed;
    }
}
