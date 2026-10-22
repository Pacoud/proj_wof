
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Estimation interne tenant compte des protections.
/// Aucune information ennemie supplementaire n'est
/// destinee a la vue renseignement du joueur.
/// </summary>
internal readonly record struct TacticalProtectedHitEstimate(
    TacticalHitEstimate GeometricEstimate,
    TacticalStance TargetStance,
    TacticalCover TargetCover,
    double ExposureFactor,
    double CoverInterceptionFraction,
    double UnblockedHitProbabilityPerRound,
    double ExpectedUnblockedImpacts);

/// <summary>
/// Combine une estimation geometrique existante avec
/// la disposition defensive reelle d'une compagnie.
/// Aucun tir, degat ou retrait de munitions.
/// </summary>
internal static class TacticalProtectedHitEvaluator
{
    internal static TacticalProtectedHitEstimate Evaluate(
        TacticalHitEstimate geometric,
        TacticalDefensiveDisposition disposition,
        TacticalDefenseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        double probability = geometric.HitProbabilityPerRound;

        if (!double.IsFinite(probability)
            || probability < 0
            || probability > 1
            || geometric.RoundsExpended <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(geometric),
                "Invalid geometric hit estimate.");
        }

        double exposure =
            profile.GetExposureFactor(disposition.Stance);

        double interception =
            profile.GetInterceptionFraction(disposition.Cover);

        // Probabilite qu'un coup atteigne une cible
        // theorique exposee sans etre intercepte.
        double effectiveProbability =
            probability * exposure * (1.0 - interception);

        effectiveProbability = Math.Clamp(
            effectiveProbability, 0.0, 1.0);

        double expected =
            geometric.RoundsExpended * effectiveProbability;

        return new TacticalProtectedHitEstimate(
            GeometricEstimate: geometric,
            TargetStance: disposition.Stance,
            TargetCover: disposition.Cover,
            ExposureFactor: exposure,
            CoverInterceptionFraction: interception,
            UnblockedHitProbabilityPerRound:
                effectiveProbability,
            ExpectedUnblockedImpacts: expected);
    }
}
