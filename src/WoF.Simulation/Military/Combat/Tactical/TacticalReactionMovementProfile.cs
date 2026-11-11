
namespace WoF.Simulation.Military.Combat.Tactical;

internal sealed class TacticalReactionMovementProfile
{
    internal double MetersPerHour { get; }

    internal double FrontLineExposureFraction { get; }

    /// <summary>
    /// Distance de contact potentielle sur une liaison.
    /// Zero desactive cette detection, pour conserver
    /// la compatibilite des scenarios precedents.
    /// </summary>
    internal double TransitContactDistanceMeters { get; }

    internal TacticalReactionMovementProfile(
        double metersPerHour,
        double frontLineExposureFraction = 0.25,
        double transitContactDistanceMeters = 0)
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

        if (!double.IsFinite(transitContactDistanceMeters)
            || transitContactDistanceMeters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transitContactDistanceMeters));
        }

        MetersPerHour = metersPerHour;

        FrontLineExposureFraction =
            frontLineExposureFraction;

        TransitContactDistanceMeters =
            transitContactDistanceMeters;
    }
}
