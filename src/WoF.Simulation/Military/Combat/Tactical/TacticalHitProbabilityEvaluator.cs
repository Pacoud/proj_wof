
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Calcule la probabilite theorique d'impact
/// d'une salve deja executee.
/// 
/// Utilise uniquement la position du tireur
/// et la position RAPPORTÉE de la cible.
/// Ne modifie aucune ressource militaire.
/// </summary>
internal static class TacticalHitProbabilityEvaluator
{
    public static TacticalHitEstimate Evaluate(
        TacticalFireEvent fireEvent,
        TacticalGeometryMap geometry,
        TacticalPerceptionState perception,
        TacticalHitProfile profile)
    {
        ArgumentNullException.ThrowIfNull(fireEvent);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(perception);
        ArgumentNullException.ThrowIfNull(profile);

        if (!ReferenceEquals(geometry.Battle, perception.Battle))
        {
            throw new InvalidOperationException(
                "Geometry and perception belong to different battles.");
        }

        var battle = geometry.Battle;

        if (fireEvent.Shooter is null
            || !battle.ContainsDeployment(fireEvent.Shooter))
        {
            throw new InvalidOperationException(
                "Shooter is outside this battle.");
        }

        if (fireEvent.Hour < battle.Engagement.StartHour
            || fireEvent.TargetTrackId <= 0
            || fireEvent.WeaponsCommitted <= 0
            || fireEvent.RoundsExpended <= 0)
        {
            throw new ArgumentException(
                "Invalid fire event.", nameof(fireEvent));
        }

        // C2.4c.1 ne traite que les armes legeres.
        if (fireEvent.AmmunitionType != AmmunitionType.SmallArms)
        {
            throw new NotSupportedException(
                "Only small arms are modeled in C2.4c.1.");
        }

        var shooterPosition =
            geometry.GetPosition(fireEvent.Shooter);

        if (shooterPosition is null)
        {
            throw new InvalidOperationException(
                "Shooter has no tactical position.");
        }

        // Le rapport ne contient pas la reference
        // a la compagnie ennemie reelle.
        var contact = perception.GetCurrentTrackReport(
            fireEvent.Shooter,
            fireEvent.TargetTrackId,
            fireEvent.Hour);

        if (contact is null
            || contact.LastKnownPosition is null
            || contact.PositionUncertaintyMeters is null)
        {
            throw new InvalidOperationException(
                "No current localized contact for this salvo.");
        }

        double uncertainty =
            contact.PositionUncertaintyMeters.Value;

        if (!double.IsFinite(uncertainty)
            || uncertainty < 0)
        {
            throw new InvalidOperationException(
                "Invalid localization uncertainty.");
        }

        // IMPORTANT :
        // Distance au point OBSERVE, pas au point
        // reel de la compagnie ennemie.
        double range =
            shooterPosition.Point.SpatialDistanceTo(
                contact.LastKnownPosition.Value);

        // Approximation d'une dispersion supplementaire
        // croissant lineairement avec la distance.
        double rangeDispersion =
            profile.DispersionAt100Meters * (range / 100.0);

        // Somme quadratique des dispersions supposees
        // independantes.
        double effectiveDispersion = double.Hypot(
            double.Hypot(
                profile.MinimumDispersionMeters,
                rangeDispersion),
            uncertainty);

        double ratio =
            profile.EffectiveTargetRadiusMeters
            / effectiveDispersion;

        // Probabilite pour une gaussienne isotrope 2D
        // centree sur une cible circulaire.
        double probability = 1.0 -
            Math.Exp(-0.5 * ratio * ratio);

        probability = Math.Clamp(
            probability, 0.0, 1.0);

        double expectedImpacts =
            fireEvent.RoundsExpended * probability;

        return new TacticalHitEstimate(
            Hour: fireEvent.Hour,
            TargetTrackId: fireEvent.TargetTrackId,
            RoundsExpended: fireEvent.RoundsExpended,
            EstimatedRangeMeters: range,
            LocalizationUncertaintyMeters: uncertainty,
            EffectiveDispersionMeters: effectiveDispersion,
            HitProbabilityPerRound: probability,
            ExpectedImpacts: expectedImpacts);
    }
}
