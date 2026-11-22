
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

    /// <summary>
    /// Plus grande distance à laquelle une compagnie peut être vue ou
    /// entendue, mesurée vers le point le plus proche de son front :
    /// la portée visuelle d'une compagnie qui tire sans dissimulation,
    /// ou la portée acoustique si elle est plus grande.
    /// Sert à choisir les paires candidates au contact.
    /// </summary>
    internal double MaxDetectionRangeMeters =>
        Math.Max(
            MaxVisualRangeMeters
                * TacticalDetectionSystem.FiringVisualRangeFactor,
            MaxAcousticRangeMeters);
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
    /// <summary>
    /// Facteur de portée visuelle d'une compagnie qui tire : le plus fort
    /// des facteurs d'activité, la dissimulation ne pouvant que réduire
    /// la portée.
    /// </summary>
    internal const double FiringVisualRangeFactor = 2.5;

    // FrontPoints n'est calculé que pour une compagnie placée et dotée
    // d'une signature, c'est-à-dire une cible possible. Une classe : les
    // entrées, avec leurs trois points, passent par référence d'une paire
    // à l'autre au lieu d'être copiées.
    private sealed record DetectionEntry(
        CompanyTacticalPosition? Position,
        TacticalUnitSignature? Signature,
        double VisualRangeMeters,
        TacticalFrontPoints FrontPoints);


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

            var frontPoints = signature is not null && position is not null
                ? geometry.GetFrontPoints(position)
                : default;

            entries.Add(
                company,
                new DetectionEntry(
                    position,
                    signature,
                    visualRange,
                    frontPoints));
        }

        var observations = new List<TacticalObservationEvent>();

        // Paires topologiques, puis paires à portée de détection.
        TacticalContactDetector.VisitCandidates(
            battle,
            diplomacy,
            currentHour,
            geometry,
            profile.MaxDetectionRangeMeters,
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

                // Distance entre centres, symetrique : un seul
                // calcul pour les deux directions. Les extremites
                // du front sont mesurees dans TryDetect.
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





    /// <summary>
    /// Observation de la cible par l'observateur, mesurée de l'œil de
    /// l'observateur (son centre) vers le centre de la cible, puis vers
    /// les extrémités de son front (option b).
    ///
    /// Le premier point à portée visuelle, dans le champ de vision et
    /// sans obstacle donne une localisation : la position signalée est
    /// ce point, arrondi, et le secteur signalé est celui de ce point
    /// (le secteur principal pour le centre). Sinon, une compagnie qui
    /// tire est entendue si son point le plus proche est à portée
    /// acoustique.
    ///
    /// Un point confondu avec l'observateur n'a ni relèvement ni ligne de
    /// vue : il n'est pas vu, et deux compagnies au même point se voient
    /// par leurs extrémités. L'audition, elle, le compte, à 0 m.
    /// </summary>
    private static TacticalObservationEvent? TryDetect(
        TacticalGeometryMap geometry,
        CompanyDeployment observer,
        CompanyDeployment target,
        DetectionEntry observerEntry,
        DetectionEntry targetEntry,
        double centerDistance,
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

        // Une extrémité est au plus à un demi-front du centre : au-delà,
        // aucun point ne peut être vu ni entendu. La marge garde ce
        // raccourci plus large que les calculs exacts qui suivent.
        double reach = signature.Activity == TacticalActivity.Firing
            ? Math.Max(
                targetEntry.VisualRangeMeters,
                profile.MaxAcousticRangeMeters)
            : targetEntry.VisualRangeMeters;

        if (centerDistance - targetPosition.FrontageMeters / 2.0
            > reach + 1e-6)
        {
            return null;
        }

        TacticalPoint eye = observerPosition.Point;
        var points = targetEntry.FrontPoints;

        // Distance au point le plus proche, pour l'audition.
        double nearest = double.PositiveInfinity;

        for (int i = 0; i < points.Count; i++)
        {
            TacticalPoint point = points[i];

            // La distance entre centres est déjà calculée.
            double distance = i == 0
                ? centerDistance
                : eye.HorizontalDistanceTo(point);

            if (!double.IsFinite(distance))
                continue;

            nearest = Math.Min(nearest, distance);

            if (distance <= 0
                || distance > targetEntry.VisualRangeMeters
                || !IsInFieldOfView(observerPosition, point, profile)
                || !HasClearSight(geometry, eye, point, distance))
            {
                continue;
            }

            var seenSector = i == 0
                ? target.Sector
                : geometry.FindSectorAt(target.Sector, point);

            TacticalPoint estimatedPosition = new(
                RoundToCell(
                    point.XMeters,
                    profile.LocalizationCellMeters),
                RoundToCell(
                    point.YMeters,
                    profile.LocalizationCellMeters),
                RoundToCell(
                    point.GroundElevationMeters,
                    10));

            return new TacticalObservationEvent(
                observer,
                target,
                estimatedPosition,
                seenSector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters:
                    profile.LocalizationCellMeters,
                EstimatedCompanyType: null);
        }

        // Toujours independant de la visibilite.
        bool canHear =
            signature.Activity == TacticalActivity.Firing
            && profile.MaxAcousticRangeMeters > 0
            && nearest <= profile.MaxAcousticRangeMeters;

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

    private static bool IsInFieldOfView(
        CompanyTacticalPosition observer,
        TacticalPoint point,
        TacticalDetectionProfile profile)
    {
        // Lorsque le champ de vision couvre 360°,
        // aucune orientation ne peut exclure la cible.
        if (profile.FieldOfViewDegrees == 360.0)
            return true;

        double relativeBearing =
            TacticalGeometryMap.RelativeBearingDegrees(
                observer.Point,
                observer.FacingDegrees,
                point);

        double deviation = Math.Min(
            relativeBearing,
            360.0 - relativeBearing);

        return deviation <= profile.FieldOfViewDegrees / 2.0;
    }

    private static bool HasClearSight(
        TacticalGeometryMap geometry,
        TacticalPoint eye,
        TacticalPoint point,
        double distance)
    {
        // Ni obstacle ni relief : pas d'intersection a calculer,
        // pas d'objet TacticalSightResult a allouer.
        if (geometry.LineObstacles.Count == 0
            && geometry.Terrain is null)
        {
            return TacticalSightEvaluator.IsClearWithoutObstacles(
                eye,
                point,
                distance);
        }

        // Obstacles tracés ou relief : l'evaluateur complet,
        // comme pour le tir.
        return TacticalSightEvaluator.EvaluatePoints(
                geometry,
                eye,
                point).Status
            == TacticalSightStatus.ClearOfKnownObstacles;
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
            TacticalActivity.Firing => FiringVisualRangeFactor,
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
