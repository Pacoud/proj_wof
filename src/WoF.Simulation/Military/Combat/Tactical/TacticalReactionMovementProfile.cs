
namespace WoF.Simulation.Military.Combat.Tactical;

internal sealed class TacticalReactionMovementProfile
{
    internal double MetersPerHour { get; }

    // Part des survivants initialement exposes
    // a l'arrivee d'un renfort en premiere ligne.
    internal double FrontLineExposureFraction { get; }

    internal TacticalReactionMovementProfile(
        double metersPerHour,
        double frontLineExposureFraction = 0.25)
    {
        if (!double.IsFinite(metersPerHour)
            || metersPerHour <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metersPerHour));
        }

        if (!double.IsFinite(frontLineExposureFraction)
            || frontLineExposureFraction <= 0
            || frontLineExposureFraction > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frontLineExposureFraction));
        }

        MetersPerHour = metersPerHour;
        FrontLineExposureFraction =
            frontLineExposureFraction;
    }
}
