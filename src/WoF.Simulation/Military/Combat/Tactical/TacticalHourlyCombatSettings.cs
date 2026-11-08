
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
    internal TacticalSuppressionProfile? Suppression { get; }
    internal TacticalCohesionProfile? Cohesion { get; }
    internal TacticalBattalionAssessmentProfile? BattalionAssessment
    {
        get;
    }

    internal TacticalBattalionReactionProfile? BattalionReactions
    {
        get;
    }


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
        ulong baseSeed,
        TacticalSuppressionProfile? suppression = null,
        TacticalCohesionProfile? cohesion = null,
        TacticalBattalionAssessmentProfile? battalionAssessment = null,
        TacticalBattalionReactionProfile? battalionReactions = null)
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
        Suppression = suppression;

        if (cohesion is not null && suppression is null)
        {
            throw new ArgumentException(
                "Cohesion requires the suppresion system",
                nameof(cohesion)
            );
        }

        Cohesion = cohesion;


        if (battalionAssessment is not null && cohesion is null )
        {
            throw new ArgumentException (
                "Battalion assessment requires company cohesion",
                nameof(battalionAssessment)
            );
        }

        BattalionAssessment = battalionAssessment;



        if (battalionReactions is not null
            && battalionAssessment is null)
        {
            throw new ArgumentException(
                "Battalion reactions require battalion assessment.",
                nameof(battalionReactions));
        }

        if (battalionReactions is not null)
        {
            battalionReactions.ValidateFor(battalionAssessment!);
        }

        BattalionReactions = battalionReactions;

    }
}
