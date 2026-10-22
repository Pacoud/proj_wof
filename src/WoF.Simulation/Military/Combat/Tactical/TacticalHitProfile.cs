
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Parametres du premier modele de dispersion.
/// Les valeurs seront calibrees ulterieurement.
/// </summary>
public sealed class TacticalHitProfile
{
    // Dispersion residuelle, meme a tres courte portee.
    public double MinimumDispersionMeters { get; }

    // Ecart-type de dispersion supplementaire a 100 m.
    public double DispersionAt100Meters { get; }

    // Rayon d'une cible individuelle theorique exposee.
    // Ne represente PAS la largeur d'une compagnie.
    public double EffectiveTargetRadiusMeters { get; }

    public TacticalHitProfile(
        double minimumDispersionMeters,
        double dispersionAt100Meters,
        double effectiveTargetRadiusMeters)
    {
        if (!double.IsFinite(minimumDispersionMeters)
            || minimumDispersionMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumDispersionMeters));
        }

        if (!double.IsFinite(dispersionAt100Meters)
            || dispersionAt100Meters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dispersionAt100Meters));
        }

        if (!double.IsFinite(effectiveTargetRadiusMeters)
            || effectiveTargetRadiusMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveTargetRadiusMeters));
        }

        MinimumDispersionMeters = minimumDispersionMeters;
        DispersionAt100Meters = dispersionAt100Meters;
        EffectiveTargetRadiusMeters = effectiveTargetRadiusMeters;
    }
}
