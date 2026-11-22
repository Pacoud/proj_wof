
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


    private readonly record struct DetectionEntry(
        CompanyTacticalPosition? Position,
        TacticalUnitSignature? Signature,
        double VisualRangeMeters);


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

        // Instantane local a cette passe.
        // Aucun cache persistant entre les heures.
        var entries = new Dictionary<
            CompanyDeployment, DetectionEntry>(
            battle.Deployments.Count,
            ReferenceEqualityComparer.Instance);

        foreach (var company in battle.Deployments)
        {
            signatures.TryGet(company, out var signature);

            var position = geometry.GetPosition(company);

            double visualRange = signature is null
                ? 0
                : CalculateVisualRange(signature, profile);

            entries.Add(
                company,
                new DetectionEntry(
                    position,
                    signature,
                    visualRange));
        }

        var observations = new List<TacticalObservationEvent>();

        TacticalContactDetector.VisitCandidates(
            battle,
            diplomacy,
            currentHour,
            (first, second, topology, connectionDistance) =>
            {
                var firstEntry = entries[first];
                var secondEntry = entries[second];

                var firstPosition = firstEntry.Position;
                var secondPosition = secondEntry.Position;

                // Les deux directions sont impossibles
                // sans positions valides.
                if (firstPosition is null
                    || secondPosition is null)
                {
                    return;
                }

                // Aucune observation si les deux
                // signatures sont absentes.
                if (firstEntry.Signature is null
                    && secondEntry.Signature is null)
                {
                    return;
                }

                // Distance symetrique : un seul calcul
                // pour les deux directions.
                double distance =
                    firstPosition.Point.HorizontalDistanceTo(
                        secondPosition.Point);

                var firstObservation = TryDetect(
                    geometry,
                    first,
                    second,
                    firstEntry,
                    secondEntry,
                    distance,
                    profile);

                if (firstObservation is not null)
                    observations.Add(firstObservation);

                var secondObservation = TryDetect(
                    geometry,
                    second,
                    first,
                    secondEntry,
                    firstEntry,
                    distance,
                    profile);

                if (secondObservation is not null)
                    observations.Add(secondObservation);
            });

        // La validation et l'application restent
        // atomiques, apres toute la collecte.
        perception.Apply(currentHour, observations);
    }





    private static TacticalObservationEvent? TryDetect(
        TacticalGeometryMap geometry,
        CompanyDeployment observer,
        CompanyDeployment target,
        DetectionEntry observerEntry,
        DetectionEntry targetEntry,
        double distance,
        TacticalDetectionProfile profile)
    {
        var signature = targetEntry.Signature;

        if (signature is null)
            return null;

        var observerPosition = observerEntry.Position;
        var targetPosition = targetEntry.Position;

        if (observerPosition is null
            || targetPosition is null)
        {
            return null;
        }

        if (!double.IsFinite(distance) || distance <= 0)
            return null;

        if (distance <= targetEntry.VisualRangeMeters)
        {
            var a = observerPosition.Point;
            var b = targetPosition.Point;

            bool inFieldOfView =
                profile.FieldOfViewDegrees == 360.0;

            // Lorsque le champ de vision couvre 360°,
            // aucune orientation ne peut exclure la cible.
            //
            // La distance a deja ete verifiee :
            // elle est finie et strictement positive.
            if (!inFieldOfView)
            {
                double dx = b.XMeters - a.XMeters;
                double dy = b.YMeters - a.YMeters;

                // Calcul historique conserve pour
                // les champs de vision inferieurs a 360°.
                double bearing =
                    (Math.Atan2(dx, dy) * 180.0 / Math.PI
                    + 360.0) % 360.0;

                double relativeBearing =
                    (bearing - observerPosition.FacingDegrees
                    + 360.0) % 360.0;

                double deviation = Math.Min(
                    relativeBearing,
                    360.0 - relativeBearing);

                inFieldOfView =
                    deviation <= profile.FieldOfViewDegrees / 2.0;
            }

            if (inFieldOfView)
            {

            
            
                bool hasClearSight;

                if (geometry.LineObstacles.Count == 0
                    && geometry.Terrain is null)
                {
                    // Ni obstacle ni relief : pas d'intersection
                    // a calculer, pas d'objet TacticalSightResult
                    // a allouer.
                    hasClearSight =
                        TacticalSightEvaluator.IsClearWithoutObstacles(
                            a,
                            b,
                            distance);
                }
                else
                {
                    // Obstacles tracés ou relief : l'evaluateur
                    // complet, comme pour le tir.
                    hasClearSight =
                        TacticalSightEvaluator.Evaluate(
                            geometry,
                            observer,
                            target).Status
                        == TacticalSightStatus.ClearOfKnownObstacles;
                }

                if (hasClearSight)
                {
                    TacticalPoint estimatedPosition = new(
                        RoundToCell(
                            b.XMeters,
                            profile.LocalizationCellMeters),
                        RoundToCell(
                            b.YMeters,
                            profile.LocalizationCellMeters),
                        RoundToCell(
                            b.GroundElevationMeters,
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
            
        }
        }
        // Toujours independant de la visibilite.
        bool canHear =
            signature.Activity == TacticalActivity.Firing
            && profile.MaxAcousticRangeMeters > 0
            && distance <= profile.MaxAcousticRangeMeters;

        if (canHear)
        {
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



    private static double CalculateVisualRange(
        TacticalUnitSignature signature,
        TacticalDetectionProfile profile)
    {
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

        return profile.MaxVisualRangeMeters
            * activityFactor
            * concealmentFactor;
    }

}
