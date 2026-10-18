
using WoF.Simulation.Diplomacy;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Capacites simplifiees des observateurs.
/// Tous les observateurs partagent ce profil pour
/// cette premiere iteration.
/// </summary>
public sealed class TacticalDetectionProfile
{
    public double MaxVisualRangeMeters { get; }
    public double MaxAcousticRangeMeters { get; }
    public double FieldOfViewDegrees { get; }
    public double LocalizationCellMeters { get; }

    public TacticalDetectionProfile(
        double maxVisualRangeMeters,
        double maxAcousticRangeMeters,
        double fieldOfViewDegrees,
        double localizationCellMeters = 50)
    {
        if (!double.IsFinite(maxVisualRangeMeters)
            || maxVisualRangeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxVisualRangeMeters));
        }

        if (!double.IsFinite(maxAcousticRangeMeters)
            || maxAcousticRangeMeters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAcousticRangeMeters));
        }

        if (!double.IsFinite(fieldOfViewDegrees)
            || fieldOfViewDegrees <= 0
            || fieldOfViewDegrees > 360)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fieldOfViewDegrees));
        }

        if (!double.IsFinite(localizationCellMeters)
            || localizationCellMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(localizationCellMeters));
        }

        MaxVisualRangeMeters = maxVisualRangeMeters;
        MaxAcousticRangeMeters = maxAcousticRangeMeters;
        FieldOfViewDegrees = fieldOfViewDegrees;
        LocalizationCellMeters = localizationCellMeters;
    }
}

/// <summary>
/// Evaluation deterministe des signatures visuelles
/// et acoustiques, puis application a la memoire.
///
/// Cette version n'identifie jamais automatiquement
/// le type reel de la compagnie cible.
/// </summary>
public static class TacticalDetectionSystem
{
    public static void Update(
        TacticalBattle battle,
        TacticalGeometryMap geometry,
        DiplomacySystem diplomacy,
        TacticalPerceptionState perception,
        TacticalSignatureFrame signatures,
        long currentHour,
        TacticalDetectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(perception);
        ArgumentNullException.ThrowIfNull(signatures);
        ArgumentNullException.ThrowIfNull(profile);

        if (!ReferenceEquals(geometry.Battle, battle)
            || !ReferenceEquals(perception.Battle, battle)
            || !ReferenceEquals(signatures.Battle, battle))
        {
            throw new InvalidOperationException(
                "Detection inputs belong to different battles.");
        }

        perception.EnsureCanUpdate(currentHour);

        var contacts = TacticalContactEvaluator.Evaluate(
            battle,
            geometry,
            diplomacy);

        var observations = new List<TacticalObservationEvent>();

        foreach (var contact in contacts)
        {
            var first = contact.Candidate.First;
            var second = contact.Candidate.Second;

            // La premiere compagnie observe la seconde.
            var firstObservation = TryDetect(
                geometry,
                signatures,
                first,
                second,
                contact.FirstToSecondSight,
                profile);

            if (firstObservation is not null)
                observations.Add(firstObservation);

            // La seconde observe la premiere.
            var secondObservation = TryDetect(
                geometry,
                signatures,
                second,
                first,
                contact.SecondToFirstSight,
                profile);

            if (secondObservation is not null)
                observations.Add(secondObservation);
        }

        // Une seule application par passe :
        // les anciennes pistes non actualisees
        // deviennent perimees.
        perception.Apply(currentHour, observations);
    }

    private static TacticalObservationEvent? TryDetect(
        TacticalGeometryMap geometry,
        TacticalSignatureFrame signatures,
        CompanyDeployment observer,
        CompanyDeployment target,
        TacticalSightResult sight,
        TacticalDetectionProfile profile)
    {
        if (!signatures.TryGet(
            target,
            out var signature)
            || signature is null)
        {
            // Ne jamais supposer qu'une compagnie
            // sans signature est exposee.
            return null;
        }

        var observerPosition = geometry.GetPosition(observer);
        var targetPosition = geometry.GetPosition(target);

        if (observerPosition is null || targetPosition is null)
            return null;

        var spatial = geometry.Measure(observer, target);

        double distance = spatial.HorizontalDistanceMeters;

        if (!double.IsFinite(distance) || distance <= 0)
            return null;

        double activityFactor = signature.Activity switch
        {
            TacticalActivity.Stationary => 1.0,
            TacticalActivity.Moving => 1.4,
            TacticalActivity.Firing => 2.5,
            _ => throw new InvalidOperationException(
                "Unknown tactical activity.")
        };

        double concealmentFactor = signature.Concealment switch
        {
            TacticalConcealment.None => 1.0,
            TacticalConcealment.Partial => 0.65,
            TacticalConcealment.Strong => 0.25,
            _ => throw new InvalidOperationException(
                "Unknown tactical concealment.")
        };

        double visualRange =
            profile.MaxVisualRangeMeters
            * activityFactor
            * concealmentFactor;

        bool withinVisualRange = distance <= visualRange;

        bool inFieldOfView = false;

        if (spatial.RelativeBearingDegrees is double angle)
        {
            double deviation = Math.Min(
                angle,
                360.0 - angle);

            inFieldOfView =
                deviation <= profile.FieldOfViewDegrees / 2.0;
        }

        bool canObserveVisually =
            sight.Status
                == TacticalSightStatus.ClearOfKnownObstacles
            && withinVisualRange
            && inFieldOfView;

        if (canObserveVisually)
        {
            // Position estimee sur une grille grossiere.
            // Nous n'exposons pas les coordonnees
            // reelles avec une precision absolue.
            TacticalPoint estimatedPosition = new(
                RoundToCell(
                    targetPosition.Point.XMeters,
                    profile.LocalizationCellMeters),
                RoundToCell(
                    targetPosition.Point.YMeters,
                    profile.LocalizationCellMeters),
                RoundToCell(
                    targetPosition.Point.GroundElevationMeters,
                    10));

            return new TacticalObservationEvent(
                observer,
                target,
                estimatedPosition,
                target.Sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters:
                    profile.LocalizationCellMeters,
                EstimatedCompanyType: null);
        }

        // Les tirs constituent une signature sonore
        // meme si un obstacle masque le corridor.
        //
        // L'acoustique n'utilise pas encore un modele
        // directionnel de propagation du son.
        bool canHear =
            signature.Activity == TacticalActivity.Firing
            && profile.MaxAcousticRangeMeters > 0
            && distance <= profile.MaxAcousticRangeMeters;

        if (canHear)
        {
            // Approximation du secteur source :
            // pas de coordonnees, ni d'identification.
            return new TacticalObservationEvent(
                observer,
                target,
                ObservedPosition: null,
                ObservedSectorId: target.Sector.Id,
                KnowledgeLevel: TacticalKnowledgeLevel.Suspected,
                PositionUncertaintyMeters: null,
                EstimatedCompanyType: null);
        }

        return null;
    }

    private static double RoundToCell(
        double value,
        double cellSize)
    {
        return Math.Round(
            value / cellSize,
            MidpointRounding.AwayFromZero) * cellSize;
    }
}
