
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Probabilites de consequence pour un combattant
/// distinct ayant subi au moins un contact.
///
/// Parametres experimentaux a calibrer.
/// Aucune valeur historique imposee.
/// </summary>
internal sealed class TacticalPersonnelEffectProfile
{
    internal double KilledProbability { get; }

    internal double WoundedUnavailableProbability { get; }

    internal double NoLastingIncapacityProbability =>
        1.0 - KilledProbability - WoundedUnavailableProbability;

    internal TacticalPersonnelEffectProfile(
        double killedProbability,
        double woundedUnavailableProbability)
    {
        Validate(killedProbability);
        Validate(woundedUnavailableProbability);

        if (killedProbability
            + woundedUnavailableProbability > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(woundedUnavailableProbability),
                "Combined probabilities cannot exceed 1.");
        }

        KilledProbability = killedProbability;
        WoundedUnavailableProbability =
            woundedUnavailableProbability;
    }

    private static void Validate(double probability)
    {
        if (!double.IsFinite(probability)
            || probability < 0
            || probability > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probability));
        }
    }
}
