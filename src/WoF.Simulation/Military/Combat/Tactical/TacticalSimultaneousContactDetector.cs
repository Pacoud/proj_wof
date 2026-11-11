
using WoF.Simulation.Diplomacy;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Trajectoire geometrique pendant une fraction d'heure.
/// Les temps sont normalises sur [0, 1].
///
/// Une compagnie immobile utilise From == To et
/// IsMoving == false.
/// </summary>
internal sealed record TacticalTimedTrajectorySegment(
    CompanyDeployment Company,
    TacticalPoint From,
    TacticalPoint To,
    double StartTime,
    double EndTime,
    bool IsMoving,
    bool IsContestedReinforcementLeg = false);

/// <summary>
/// Rencontre potentielle issue de trajectoires prevues.
/// Ne signifie ni detection visuelle ni tir reel.
/// </summary>
internal sealed record TacticalPredictedEncounter(
    long Hour,
    CompanyDeployment First,
    CompanyDeployment Second,
    double TimeFraction,
    TacticalPoint EstimatedPosition,
    double DistanceMeters);

internal readonly record struct TacticalSweptContactStatistics(
    int SegmentCount,
    long BroadPhaseCandidates,
    long ExactPairChecks,
    int Encounters);

internal sealed record TacticalSimultaneousContactResult(
    IReadOnlyList<TacticalPredictedEncounter> Encounters,
    TacticalSweptContactStatistics Statistics);

internal static class TacticalSimultaneousContactDetector
{
    private sealed record Entry(
        TacticalTimedTrajectorySegment Segment,
        double MinX,
        double MaxX,
        double MinY,
        double MaxY,
        int CompanyIndex);

    internal static TacticalSimultaneousContactResult Detect(
        TacticalBattle battle,
        DiplomacySystem diplomacy,
        IReadOnlyList<TacticalTimedTrajectorySegment> segments,
        long hour,
        double contactRadiusMeters)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(segments);

        if (!double.IsFinite(contactRadiusMeters)
            || contactRadiusMeters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contactRadiusMeters));
        }

        var companyIndices =
            new Dictionary<CompanyDeployment, int>(
                ReferenceEqualityComparer.Instance);

        for (int i = 0; i < battle.Deployments.Count; i++)
            companyIndices.Add(battle.Deployments[i], i);

        var entries = new List<Entry>(segments.Count);

        foreach (var segment in segments)
        {
            if (segment is null
                || segment.Company is null
                || !companyIndices.TryGetValue(
                    segment.Company, out int index))
            {
                throw new InvalidOperationException(
                    "Trajectory belongs to another battle.");
            }

            if (!double.IsFinite(segment.StartTime)
                || !double.IsFinite(segment.EndTime)
                || segment.StartTime < 0
                || segment.EndTime > 1
                || segment.EndTime <= segment.StartTime)
            {
                throw new InvalidOperationException(
                    "Invalid trajectory time interval.");
            }

            if (!segment.IsMoving
                && segment.From != segment.To)
            {
                throw new InvalidOperationException(
                    "Stationary trajectory changes position.");
            }

            entries.Add(new Entry(
                segment,
                Math.Min(segment.From.XMeters,
                         segment.To.XMeters),
                Math.Max(segment.From.XMeters,
                         segment.To.XMeters),
                Math.Min(segment.From.YMeters,
                         segment.To.YMeters),
                Math.Max(segment.From.YMeters,
                         segment.To.YMeters),
                index));
        }

        entries.Sort((a, b) =>
        {
            int result = a.MinX.CompareTo(b.MinX);

            if (result != 0)
                return result;

            result = a.CompanyIndex.CompareTo(b.CompanyIndex);

            if (result != 0)
                return result;

            return a.Segment.StartTime.CompareTo(
                b.Segment.StartTime);
        });

        var active = new List<Entry>();

        var earliest =
            new Dictionary<(int First, int Second),
                TacticalPredictedEncounter>();

        long candidates = 0;
        long exactChecks = 0;

        foreach (var current in entries)
        {
            // Ecarter les enveloppes deja depassees.
            active.RemoveAll(other =>
                other.MaxX + contactRadiusMeters
                < current.MinX);

            foreach (var other in active)
            {
                if (other.CompanyIndex == current.CompanyIndex)
                    continue;

                if (other.MaxY + contactRadiusMeters
                        < current.MinY
                    || current.MaxY + contactRadiusMeters
                        < other.MinY)
                {
                    continue;
                }

                double overlapStart = Math.Max(
                    other.Segment.StartTime,
                    current.Segment.StartTime);

                double overlapEnd = Math.Min(
                    other.Segment.EndTime,
                    current.Segment.EndTime);

                if (overlapStart > overlapEnd)
                    continue;

                candidates++;

                if (!other.Segment.IsMoving
                    && !current.Segment.IsMoving)
                    continue;

                // Conserver l'exception historique du
                // renfort final face a un defenseur
                // immobile. Deux mobiles, eux,
                // peuvent toujours se rencontrer.
                if ((other.Segment.IsContestedReinforcementLeg
                        && !current.Segment.IsMoving)
                    || (current.Segment.IsContestedReinforcementLeg
                        && !other.Segment.IsMoving))
                {
                    continue;
                }

                var firstCompany = other.Segment.Company;
                var secondCompany = current.Segment.Company;

                if (!AreEnemies(
                    firstCompany, secondCompany, diplomacy))
                    continue;

                if (firstCompany.Company.Composition!
                        .Manpower.Current <= 0
                    || secondCompany.Company.Composition!
                        .Manpower.Current <= 0)
                {
                    continue;
                }

                exactChecks++;

                var result = ClosestApproach(
                    other.Segment,
                    current.Segment,
                    overlapStart,
                    overlapEnd);

                if (result.Distance > contactRadiusMeters)
                    continue;

                int firstId = Math.Min(
                    other.CompanyIndex, current.CompanyIndex);

                int secondId = Math.Max(
                    other.CompanyIndex, current.CompanyIndex);

                var orderedFirst = battle.Deployments[firstId];
                var orderedSecond = battle.Deployments[secondId];

                var encounter = new TacticalPredictedEncounter(
                    hour,
                    orderedFirst,
                    orderedSecond,
                    result.Time,
                    result.Point,
                    result.Distance);

                var key = (firstId, secondId);

                if (!earliest.TryGetValue(key, out var existing)
                    || encounter.TimeFraction
                        < existing.TimeFraction)
                {
                    earliest[key] = encounter;
                }
            }

            active.Add(current);
        }

        var ordered = earliest
            .OrderBy(pair => pair.Value.TimeFraction)
            .ThenBy(pair => pair.Key.First)
            .ThenBy(pair => pair.Key.Second)
            .Select(pair => pair.Value)
            .ToArray();

        return new TacticalSimultaneousContactResult(
            Array.AsReadOnly(ordered),
            new TacticalSweptContactStatistics(
                segments.Count,
                candidates,
                exactChecks,
                ordered.Length));
    }

    private static bool AreEnemies(
        CompanyDeployment a,
        CompanyDeployment b,
        DiplomacySystem diplomacy)
    {
        var aCountry =
            a.Company.ParentBattalion?.ParentDivision?.Country;

        var bCountry =
            b.Company.ParentBattalion?.ParentDivision?.Country;

        return aCountry is not null
            && bCountry is not null
            && diplomacy.AreAtWar(aCountry, bCountry);
    }

    private static (
        double Time,
        TacticalPoint Point,
        double Distance) ClosestApproach(
        TacticalTimedTrajectorySegment a,
        TacticalTimedTrajectorySegment b,
        double start,
        double end)
    {
        double aDuration = a.EndTime - a.StartTime;
        double bDuration = b.EndTime - b.StartTime;

        double avx =
            (a.To.XMeters - a.From.XMeters) / aDuration;
        double avy =
            (a.To.YMeters - a.From.YMeters) / aDuration;

        double bvx =
            (b.To.XMeters - b.From.XMeters) / bDuration;
        double bvy =
            (b.To.YMeters - b.From.YMeters) / bDuration;

        double ax =
            a.From.XMeters + avx * (start - a.StartTime);
        double ay =
            a.From.YMeters + avy * (start - a.StartTime);

        double bx =
            b.From.XMeters + bvx * (start - b.StartTime);
        double by =
            b.From.YMeters + bvy * (start - b.StartTime);

        double rx = ax - bx;
        double ry = ay - by;

        double vx = avx - bvx;
        double vy = avy - bvy;

        double velocitySquared = vx * vx + vy * vy;

        double delta = velocitySquared > 0
            ? Math.Clamp(
                -(rx * vx + ry * vy) / velocitySquared,
                0,
                end - start)
            : 0;

        double time = start + delta;

        double firstX = ax + avx * delta;
        double firstY = ay + avy * delta;

        double secondX = bx + bvx * delta;
        double secondY = by + bvy * delta;

        double distance = double.Hypot(
            firstX - secondX,
            firstY - secondY);

        var point = new TacticalPoint(
            (firstX + secondX) / 2,
            (firstY + secondY) / 2,
            (a.From.GroundElevationMeters
                + b.From.GroundElevationMeters) / 2);

        return (time, point, distance);
    }
}
