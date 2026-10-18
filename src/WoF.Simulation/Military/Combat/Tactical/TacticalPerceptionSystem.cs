
using WoF.Simulation.Diplomacy;

namespace WoF.Simulation.Military.Combat.Tactical;

public sealed class TacticalObservationProfile
{
    public double MaxRangeMeters { get; }
    public double FieldOfViewDegrees { get; }

    public TacticalObservationProfile(
        double maxRangeMeters,
        double fieldOfViewDegrees)
    {
        if (!double.IsFinite(maxRangeMeters)
            || maxRangeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRangeMeters));
        }

        if (!double.IsFinite(fieldOfViewDegrees)
            || fieldOfViewDegrees <= 0
            || fieldOfViewDegrees > 360)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fieldOfViewDegrees));
        }

        MaxRangeMeters = maxRangeMeters;
        FieldOfViewDegrees = fieldOfViewDegrees;
    }
}

/// <summary>
/// Première règle de perception déterministe.
///
/// Un corridor dégagé, une portée suffisante et une
/// orientation compatible rendent une observation
/// possible dans ce modèle provisoire.
///
/// Aucun tir, perte ou renseignement partagé.
/// </summary>
public static class TacticalPerceptionSystem
{
    public static void Update(
        TacticalBattle battle,
        TacticalGeometryMap geometry,
        DiplomacySystem diplomacy,
        TacticalPerceptionState perception,
        long currentHour,
        TacticalObservationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(perception);
        ArgumentNullException.ThrowIfNull(profile);

        if (!ReferenceEquals(perception.Battle, battle)
            || !ReferenceEquals(geometry.Battle, battle))
        {
            throw new InvalidOperationException(
                "Battle, geometry and perception do not match.");
        }

        perception.EnsureCanUpdate(currentHour);

        var contacts = TacticalContactEvaluator.Evaluate(
            battle,
            geometry,
            diplomacy);

        var observations =
            new List<TacticalObservationEvent>();

        foreach (var contact in contacts)
        {
            var first = contact.Candidate.First;
            var second = contact.Candidate.Second;

            // A observe B.
            if (TryObserve(
                geometry,
                first,
                second,
                contact.FirstToSecondSight,
                profile,
                out var secondPosition))
            {
                observations.Add(new TacticalObservationEvent(
                    first,
                    second,
                    secondPosition,
                    second.Sector.Id));
            }

            // B observe A : décision indépendante.
            if (TryObserve(
                geometry,
                second,
                first,
                contact.SecondToFirstSight,
                profile,
                out var firstPosition))
            {
                observations.Add(new TacticalObservationEvent(
                    second,
                    first,
                    firstPosition,
                    first.Sector.Id));
            }
        }

        perception.Apply(currentHour, observations);
    }

    private static bool TryObserve(
        TacticalGeometryMap geometry,
        CompanyDeployment observer,
        CompanyDeployment target,
        TacticalSightResult sight,
        TacticalObservationProfile profile,
        out TacticalPoint observedPosition)
    {
        observedPosition = default;

        if (sight.Status !=
            TacticalSightStatus.ClearOfKnownObstacles)
        {
            return false;
        }

        var targetPosition = geometry.GetPosition(target);

        if (targetPosition is null
            || geometry.GetPosition(observer) is null)
        {
            return false;
        }

        var relation = geometry.Measure(observer, target);

        if (relation.HorizontalDistanceMeters
            > profile.MaxRangeMeters)
        {
            return false;
        }

        if (relation.RelativeBearingDegrees is not double angle)
            return false;

        // Convertit un azimut relatif [0, 360)
        // en écart minimal à l'axe frontal [0, 180].
        double deviation = Math.Min(angle, 360.0 - angle);

        if (deviation > profile.FieldOfViewDegrees / 2.0)
            return false;

        observedPosition = targetPosition.Point;
        return true;
    }
}
