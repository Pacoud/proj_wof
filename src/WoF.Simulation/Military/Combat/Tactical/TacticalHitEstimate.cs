
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Estimation statistique, sans tir supplementaire,
/// sans application d'impacts ni de pertes.
/// </summary>
public readonly record struct TacticalHitEstimate(
    long Hour,
    int TargetTrackId,
    long RoundsExpended,
    double EstimatedRangeMeters,
    double LocalizationUncertaintyMeters,
    double EffectiveDispersionMeters,
    double HitProbabilityPerRound,
    double ExpectedImpacts);
