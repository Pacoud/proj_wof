using System.Diagnostics;
using System.Text.Json;
using WoF.Simulation.Military.Combat.Tactical;

namespace WoF.Benchmarks;

internal static class TacticalDensityBenchmark
{
    private sealed record Activity(int Reports, int Orders, int Salvos, long Rounds, int Killed, int Wounded,
        int Occupation, int Encirclement);
    private sealed record Observation(int Province, double Detection, double Fire, double Control,
        Activity Activity, int FrontLine, int Support, int Reserve, int Lanes, Dictionary<string, double> PhaseMs);
    private sealed record Sample(double Total, double Detection, double Fire, double Control, double Other,
        Observation[] Battles);

    internal static int Run(string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out int count)
            || count is not (500 or 1000 or 2000 or 3000 or 5000 or 10000))
            throw new ArgumentException("tactical-density <500|1000|2000|3000|5000|10000>");
        int provinceCount = count == 10000 ? 8 : 1;
        var fields = TacticalDensityScenarioFactory.Fields(provinceCount);
        var measured = new List<Sample>();
        Activity[]? reference = null;
        for (int iteration = -3; iteration < 11; iteration++)
        {
            var sample = Measure(fields, count / provinceCount);
            var activities = sample.Battles.Select(b => b.Activity).ToArray();
            if (reference is not null && !reference.SequenceEqual(activities))
                throw new InvalidOperationException("Functional counters changed between repeats.");
            reference = activities;
            Console.WriteLine(JsonSerializer.Serialize(new { count, iteration, sample }));
            if (iteration >= 0)
                measured.Add(sample);
        }
        var ordered = measured.OrderBy(s => s.Total).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { count, median = ordered[5],
            min = ordered[0].Total, max = ordered[^1].Total, seed = TacticalDensityScenarioFactory.Seed,
            serverGC = System.Runtime.GCSettings.IsServerGC }));
        return 0;
    }

    private static Sample Measure(DensityField[] fields, int size)
    {
        var battles = fields.Select(f => TacticalDensityScenarioFactory.Create(f, size)).ToArray();
        var results = new TacticalHourlyCombatResult[battles.Length];
        var takes = new (int ByOccupation, int ByEncirclement)[battles.Length];
        var controlMs = new double[battles.Length];
        long begin = Stopwatch.GetTimestamp();
        for (int i = 0; i < battles.Length; i++)
        {
            var b = battles[i];
            results[i] = b.Orchestrator.Run(1);
            long start = Stopwatch.GetTimestamp();
            takes[i] = b.Control.ApplyHourlyControl(1, b.Field.Layout);
            controlMs[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        double total = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;

        // Extraire les résultats et compter les rapports après l'arrêt du chronomètre.
        var obs = new Observation[battles.Length];
        for (int i = 0; i < battles.Length; i++)
        {
            var b = battles[i];
            var phases = b.Timer.Snapshot();
            double detection = phases.Single(p => p.Phase == TacticalBenchmarkPhase.Detection).Milliseconds;
            double fire = phases.Where(p => p.Phase is TacticalBenchmarkPhase.FireOrderPlanning
                or TacticalBenchmarkPhase.FireExecution or TacticalBenchmarkPhase.PersonnelContacts
                or TacticalBenchmarkPhase.PersonnelEffects or TacticalBenchmarkPhase.CasualtyApplication
                or TacticalBenchmarkPhase.ExposureReconciliation).Sum(p => p.Milliseconds);
            var r = results[i];
            var deployments = b.Battle.Deployments;
            int reports = deployments.Sum(d => b.Perception.GetReports(d).Count);
            if (deployments.Count != size || r.Hour != 1 || r.SalvosFired == 0 || reports == 0)
                throw new InvalidOperationException("Incomplete battle or no combat activity.");
            obs[i] = new(b.Field.Province.Id, detection, fire, controlMs[i],
                new(reports, r.OrdersIssued, r.SalvosFired, r.RoundsExpended, r.Killed, r.Wounded,
                    takes[i].ByOccupation, takes[i].ByEncirclement),
                deployments.Count(d => d.Role == CompanyDeploymentRole.FrontLine),
                deployments.Count(d => d.Role == CompanyDeploymentRole.Support),
                deployments.Count(d => d.Role == CompanyDeploymentRole.Reserve), b.Lanes,
                phases.ToDictionary(p => p.Phase.ToString(), p => p.Milliseconds));
        }
        double det = obs.Sum(b => b.Detection), shot = obs.Sum(b => b.Fire), ctrl = controlMs.Sum();
        return new(total, det, shot, ctrl, total - det - shot - ctrl, obs);
    }
}