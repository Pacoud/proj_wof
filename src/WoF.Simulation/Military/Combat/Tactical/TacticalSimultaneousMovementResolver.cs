
namespace WoF.Simulation.Military.Combat.Tactical;

internal sealed record TacticalSimultaneousMovementResolution(
    IReadOnlySet<CompanyDeployment> PausedCompanies,
    IReadOnlyList<TacticalPredictedEncounter> PredictedEncounters,
    int ResolutionPasses,
    long BroadPhaseCandidates,
    long ExactPairChecks);

internal static class TacticalSimultaneousMovementResolver
{
    /// <summary>
    /// Resout les conflits entre intentions simultanees.
    ///
    /// buildTimelines doit etre pur : il reconstruit
    /// les trajectoires prevues depuis le meme
    /// instant initial, sans aucune mutation.
    ///
    /// Pour les compagnies deja pausees, il doit
    /// produire une position immobile sur [0, 1].
    /// </summary>
    internal static TacticalSimultaneousMovementResolution Resolve(
        TacticalBattle battle,
        WoF.Simulation.Diplomacy.DiplomacySystem diplomacy,
        IReadOnlyCollection<CompanyDeployment> movers,
        Func<IReadOnlySet<CompanyDeployment>,
            IReadOnlyList<TacticalTimedTrajectorySegment>> buildTimelines,
        long hour,
        double contactRadiusMeters)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(buildTimelines);

        var moving = new HashSet<CompanyDeployment>(
            movers, ReferenceEqualityComparer.Instance);

        var paused = new HashSet<CompanyDeployment>(
            ReferenceEqualityComparer.Instance);

        var indices = new Dictionary<CompanyDeployment, int>(
            ReferenceEqualityComparer.Instance);

        for (int i = 0; i < battle.Deployments.Count; i++)
            indices.Add(battle.Deployments[i], i);

        foreach (var company in moving)
        {
            if (!indices.ContainsKey(company))
                throw new InvalidOperationException(
                    "Moving company belongs to another battle.");
        }

        var encounters =
            new Dictionary<(int, int), TacticalPredictedEncounter>();

        int passes = 0;
        long broadPhase = 0;
        long exactChecks = 0;

        while (true)
        {
            passes++;

            // Les positions reelles de la bataille
            // restent inchangees pendant ce calcul.
            var trajectories = buildTimelines(paused);

            var result =
                TacticalSimultaneousContactDetector.Detect(
                    battle,
                    diplomacy,
                    trajectories,
                    hour,
                    contactRadiusMeters);

            broadPhase += result.Statistics.BroadPhaseCandidates;
            exactChecks += result.Statistics.ExactPairChecks;

            bool changed = false;

            foreach (var encounter in result.Encounters)
            {
                int a = indices[encounter.First];
                int b = indices[encounter.Second];

                var key = (Math.Min(a, b), Math.Max(a, b));

                if (!encounters.TryGetValue(key, out var old)
                    || encounter.TimeFraction < old.TimeFraction)
                {
                    encounters[key] = encounter;
                }

                if (moving.Contains(encounter.First)
                    && paused.Add(encounter.First))
                {
                    changed = true;
                }

                if (moving.Contains(encounter.Second)
                    && paused.Add(encounter.Second))
                {
                    changed = true;
                }
            }

            if (!changed)
                break;
        }

        var ordered = encounters
            .OrderBy(pair => pair.Value.TimeFraction)
            .ThenBy(pair => pair.Key.Item1)
            .ThenBy(pair => pair.Key.Item2)
            .Select(pair => pair.Value)
            .ToArray();

        return new TacticalSimultaneousMovementResolution(
            paused,
            Array.AsReadOnly(ordered),
            passes,
            broadPhase,
            exactChecks);
    }
}
