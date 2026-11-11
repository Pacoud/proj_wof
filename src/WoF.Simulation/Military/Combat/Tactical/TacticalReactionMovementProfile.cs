
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

    internal double SpatialCellSizeMeters { get; }

    internal bool UseSimultaneousContacts { get; }

    internal TacticalReactionMovementProfile(
        double metersPerHour,
        double frontLineExposureFraction = 0.25,
        double transitContactDistanceMeters = 0,
        double spatialCellSizeMeters = 250,
        bool useSimultaneousContacts = false)
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


        if (!double.IsFinite(spatialCellSizeMeters)
            || spatialCellSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spatialCellSizeMeters));
        }

        // Eviter qu'une recherche de contact ne
        // consulte un voisinage disproportionne.
        if (transitContactDistanceMeters > 0
            && Math.Ceiling(
                transitContactDistanceMeters / spatialCellSizeMeters) + 1 > 16)
        {
            throw new ArgumentException(
                "Spatial cell size is too small for contact radius.",
                nameof(spatialCellSizeMeters));
        }

        if (useSimultaneousContacts
            && transitContactDistanceMeters <= 0)
        {
            throw new ArgumentException(
                "Simultaneous contacts require a positive contact radius.",
                nameof(useSimultaneousContacts));
        }

        UseSimultaneousContacts = useSimultaneousContacts;
        SpatialCellSizeMeters = spatialCellSizeMeters;  
    }
}
