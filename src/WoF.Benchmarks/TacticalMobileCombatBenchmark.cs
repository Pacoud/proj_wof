using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;

namespace WoF.Benchmarks;

internal static class TacticalMobileCombatBenchmark
{
    internal sealed record Parameters(
        string Mode, TacticalMobileBenchmarkOptions Scenario, int Measurements, int Warmup);

    internal readonly record struct Measurement(
        double Milliseconds, long AllocatedBytes, int Gen0, int Gen1, int Gen2);

    internal sealed record Load(
        int TotalCompanies, int MobileCompanies, int Sectors, int ActiveMovements,
        long AvailablePersonnel, long AvailableSmallArmsRounds, int ActiveEngagements)
    {
        public double ActualMobilityPercent => 100.0 * MobileCompanies / TotalCompanies;
    }

    internal sealed record Work(
        long? BroadPhaseCandidates, long? ExactPairChecks, int? ResolutionPasses,
        int? TrajectorySegments, int? PredictedEncounters, int? FirstContacts,
        int? ConfirmedContacts, int? SalvosFired, int? MobileSalvosFired,
        long? RoundsExpended, int? Killed, int? Wounded,
        int? ActiveMovementsAfter, BenchmarkMilitaryState? MilitaryStateAfter);

    internal sealed record Sample(int Number, Measurement Measurement, Load Load, Work Work, string? Rejection)
    {
        public bool Valid => Rejection is null;
    }

    internal sealed record Statistics(
        int ValidSamples, double? MeanMs, double? MedianMs, double? P95Ms,
        double? MinMs, double? MaxMs, double? MeanAllocatedBytes, long Gen0, long Gen1, long Gen2);

    internal sealed record RunInfo(
        string Benchmark, DateTimeOffset StartedUtc, Parameters Parameters,
        string DotNet, string OperatingSystem, int LogicalProcessors,
        string BuildConfiguration, string? GitCommit, bool? GitDirty);

    internal sealed record Report(
        RunInfo Info, Statistics Statistics, IReadOnlyList<string> Warnings, IReadOnlyList<Sample> Samples);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static int Run(string[] args)
    {
        if (!TryParse(args, out var parameters))
        {
            Console.Error.WriteLine(
                "Usage: tactical-mobile <detector|full> <companies>=2+ <mobility:0..100> " +
                "<sparse|dense> <measurements>=1+ <warmup>=0+ <seed:int>");
            return 2;
        }

        var report = Execute(parameters!);
        var (json, csv) = Export(report, Path.Combine("benchmark-results", "tactical-mobile"));
        var stats = report.Statistics;
        var load = report.Samples[0].Load;
        Console.WriteLine(FormattableString.Invariant(
            $"{parameters!.Mode}: {load.TotalCompanies} companies, mobility requested {parameters.Scenario.MobilityPercent:G}% / actual {load.ActualMobilityPercent:F2}% ({load.MobileCompanies}), {load.ActiveMovements} active orders."));
        Console.WriteLine(FormattableString.Invariant(
            $"Valid samples: {stats.ValidSamples}/{report.Samples.Count}; median: {stats.MedianMs:F3} ms; p95: {stats.P95Ms:F3} ms."));
        foreach (string warning in report.Warnings)
            Console.WriteLine(warning);
        Console.WriteLine($"JSON: {json}");
        Console.WriteLine($"CSV: {csv}");

        return stats.ValidSamples == report.Samples.Count ? 0 : 1;
    }

    internal static bool TryParse(string[] args, out Parameters? parameters)
    {
        parameters = null;
        var culture = CultureInfo.InvariantCulture;
        if (args.Length != 7
            || args[0] is not ("detector" or "full")
            || !int.TryParse(args[1], NumberStyles.Integer, culture, out int companies) || companies < 2
            || !double.TryParse(args[2], NumberStyles.Float, culture, out double mobility)
            || !double.IsFinite(mobility) || mobility is < 0 or > 100
            || args[3] is not ("sparse" or "dense")
            || !int.TryParse(args[4], NumberStyles.Integer, culture, out int count) || count < 1
            || !int.TryParse(args[5], NumberStyles.Integer, culture, out int warmup) || warmup < 0
            || !int.TryParse(args[6], NumberStyles.Integer, culture, out int seed))
        {
            return false;
        }

        var density = args[3] == "sparse"
            ? TacticalMobileBenchmarkDensity.Sparse : TacticalMobileBenchmarkDensity.Dense;
        parameters = new(args[0], new(companies, mobility, density, seed), count, warmup);
        return true;
    }

    internal static Report Execute(Parameters parameters)
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string? status = GitOutput("status", "--porcelain", "--untracked-files=normal");
        var info = new RunInfo(
            "tactical-mobile", DateTimeOffset.UtcNow, parameters,
            RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
            Environment.ProcessorCount, configuration, GitOutput("rev-parse", "HEAD"),
            status is null ? null : status.Length > 0);

        var samples = parameters.Mode == "detector"
            ? MeasureDetector(parameters) : MeasureTicks(parameters);
        var warnings = new List<string>();
        double actual = samples[0].Load.ActualMobilityPercent;
        if (actual != parameters.Scenario.MobilityPercent)
        {
            warnings.Add(FormattableString.Invariant(
                $"Requested mobility {parameters.Scenario.MobilityPercent:G}%; observed at start {actual:F2}%. Compare runs using actual workload."));
        }

        if (info.GitDirty == true)
            warnings.Add("Working tree has local changes; the Git commit alone does not identify the measured sources.");

        warnings.AddRange(samples.Select(sample => sample.Rejection).OfType<string>().Distinct());
        return new Report(info, Summarize(samples), warnings.AsReadOnly(), samples);
    }

    private static IReadOnlyList<Sample> MeasureDetector(Parameters parameters)
    {
        if (parameters.Warmup > 0)
        {
            var warmup = TacticalMobileBenchmarkScenarioFactory.CreateDetector(parameters.Scenario);
            for (int i = 0; i < parameters.Warmup; i++)
                Detect(warmup);
        }

        var scenario = TacticalMobileBenchmarkScenarioFactory.CreateDetector(parameters.Scenario);
        var samples = new Sample[parameters.Measurements];
        TacticalSimultaneousContactResult? reference = null;
        for (int i = 0; i < samples.Length; i++)
        {
            var load = CaptureLoad(scenario.Battle, scenario.MobileCompanies, scenario.ActiveMovements);
            string? rejection = ValidateLoad(parameters, load, referenceLoad: i == 0 ? null : samples[0].Load);
            var measurement = Measure(() => Detect(scenario), out var result);
            reference ??= result;
            if (reference.Statistics != result.Statistics
                || !reference.Encounters.SequenceEqual(result.Encounters)
                || !reference.FirstContacts.SequenceEqual(result.FirstContacts))
            {
                rejection ??= "Detector output changed with identical trajectories.";
            }

            var stats = result.Statistics;
            var work = new Work(stats.BroadPhaseCandidates, stats.ExactPairChecks, null,
                stats.SegmentCount, result.Encounters.Count, result.FirstContacts.Count,
                null, null, null, null, null, null, null, null);
            samples[i] = new Sample(i + 1, measurement, load, work, rejection);
        }

        return Array.AsReadOnly(samples);
    }

    private static TacticalSimultaneousContactResult Detect(TacticalMobileDetectorScenario scenario) =>
        TacticalSimultaneousContactDetector.Detect(
            scenario.Battle, scenario.Diplomacy, scenario.Trajectories,
            scenario.Battle.Engagement.StartHour, scenario.ContactRadiusMeters, includeFirstContacts: true);

    private static IReadOnlyList<Sample> MeasureTicks(Parameters parameters)
    {
        for (int i = 0; i < parameters.Warmup; i++)
            TacticalMobileBenchmarkScenarioFactory.CreateTick(parameters.Scenario).Engine.Tick();

        var samples = new Sample[parameters.Measurements];
        for (int i = 0; i < samples.Length; i++)
        {
            var scenario = TacticalMobileBenchmarkScenarioFactory.CreateTick(parameters.Scenario);
            var load = CaptureLoad(scenario.Battle, scenario.MobileCompanies, scenario.ActiveMovements);
            string? rejection = ValidateLoad(parameters, load, referenceLoad: i == 0 ? null : samples[0].Load);
            if (scenario.Orchestrator.ReactionExecution!.PendingMovements.Any(order =>
                    order.Company.Company.Composition!.Manpower.Current <= 0
                    || order.Company.Company.Composition!.InfantryEquipment.Current <= 0
                    || order.Company.Company.Composition!.Ammunition.GetAvailable(AmmunitionType.SmallArms) <= 0))
            {
                rejection ??= "An active movement has no personnel, equipment or ammunition.";
            }

            long hour = scenario.Engine.Clock.CurrentHour + 1;
            var measurement = Measure(scenario.Engine.Tick);
            var combat = scenario.Orchestrator.LastResult;
            var movement = scenario.Orchestrator.LastMovementPhase;
            var mobile = scenario.Orchestrator.LastMobileCombatSummary;
            var resolution = movement?.SimultaneousResolution;

            if (combat?.Hour != hour || movement?.Hour != hour || scenario.Engine.Clock.CurrentHour != hour)
                rejection ??= "The expected tactical hour was not executed.";
            else if (load.ActiveMovements > 0 && parameters.Scenario.EnableMobileCombat
                && (mobile?.Hour != hour || mobile.Value.ConfirmedContacts == 0
                    || mobile.Value.MobileSalvosFired == 0))
                rejection ??= "The tick with active orders produced no confirmed mobile contact or no mobile salvo.";

            var after = BenchmarkMilitaryState.Capture(scenario.Engine);
            var afterLoad = CaptureLoad(scenario.Battle, scenario.MobileCompanies, scenario.ActiveMovements);
            if (combat is { } result
                && (load.AvailableSmallArmsRounds - afterLoad.AvailableSmallArmsRounds != result.RoundsExpended
                    || load.AvailablePersonnel - afterLoad.AvailablePersonnel != (long)result.Killed + result.Wounded))
            {
                rejection ??= "The change in personnel or ammunition disagrees with the combat report.";
            }

            var work = new Work(
                resolution?.BroadPhaseCandidates, resolution?.ExactPairChecks, resolution?.ResolutionPasses,
                null, movement?.PredictedEncounters?.Count, null, mobile?.ConfirmedContacts,
                combat?.SalvosFired, mobile?.MobileSalvosFired, combat?.RoundsExpended, combat?.Killed,
                combat?.Wounded, afterLoad.ActiveMovements, after);
            if (i > 0 && work != samples[0].Work)
                rejection ??= "Tactical results changed between independently rebuilt samples.";

            samples[i] = new Sample(i + 1, measurement, load, work, rejection);
        }

        return Array.AsReadOnly(samples);
    }

    private static Load CaptureLoad(TacticalBattle battle, int mobile, int movements)
    {
        long personnel = 0;
        long rounds = 0;
        foreach (var deployment in battle.Deployments)
        {
            var composition = deployment.Company.Composition!;
            personnel += composition.Manpower.Current;
            rounds += composition.Ammunition.GetAvailable(AmmunitionType.SmallArms);
        }

        return new Load(battle.Deployments.Count, mobile, battle.Battlefield.Sectors.Count,
            movements, personnel, rounds, battle.Engagement.IsActive ? 1 : 0);
    }

    private static string? ValidateLoad(Parameters parameters, Load load, Load? referenceLoad)
    {
        int requested = (int)Math.Floor(parameters.Scenario.TotalCompanies
            * parameters.Scenario.MobilityPercent / 100);
        if (load.TotalCompanies != parameters.Scenario.TotalCompanies
            || load.ActiveEngagements != 1 || load.Sectors == 0 || load.AvailablePersonnel <= 0
            || load.MobileCompanies < 0 || load.MobileCompanies > requested)
        {
            return "The constructed battle does not match the requested workload.";
        }

        if (parameters.Mode == "detector" && load.MobileCompanies != requested)
            return "The detector did not receive the requested number of moving companies.";

        if (parameters.Mode == "full" &&
            (load.ActiveMovements != load.MobileCompanies || load.AvailableSmallArmsRounds <= 0
                || (requested > 0 && load.ActiveMovements == 0)))
        {
            return "The full scenario has no usable ammunition or does not have the expected active orders.";
        }

        return referenceLoad is null || load == referenceLoad
            ? null : "Initial workload changed between samples.";
    }

    internal static Measurement Measure(Action operation) => Measure(() =>
    {
        operation();
        return true;
    }, out _);

    private static Measurement Measure<TResult>(Func<TResult> operation, out TResult result)
    {
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        result = operation();
        long end = Stopwatch.GetTimestamp();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return new Measurement(Stopwatch.GetElapsedTime(start, end).TotalMilliseconds, bytes,
            GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);
    }

    internal static Statistics Summarize(IReadOnlyList<Sample> samples)
    {
        var valid = samples.Where(sample => sample.Valid).ToArray();
        if (valid.Length == 0)
            return new(0, null, null, null, null, null, null, 0, 0, 0);

        var times = valid.Select(sample => sample.Measurement.Milliseconds).Order().ToArray();
        double median = (times[(times.Length - 1) / 2] + times[times.Length / 2]) / 2;
        int p95Index = (int)Math.Ceiling(times.Length * 0.95) - 1;
        return new(valid.Length, times.Average(), median, times[p95Index], times[0], times[^1],
            valid.Average(sample => (double)sample.Measurement.AllocatedBytes),
            valid.Sum(sample => (long)sample.Measurement.Gen0),
            valid.Sum(sample => (long)sample.Measurement.Gen1),
            valid.Sum(sample => (long)sample.Measurement.Gen2));
    }

    internal static (string Json, string Csv) Export(Report report, string folder)
    {
        Directory.CreateDirectory(folder);
        string stem = FormattableString.Invariant(
            $"tactical-mobile-{report.Info.Parameters.Mode}-{report.Info.StartedUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        string json = Path.Combine(folder, stem + ".json");
        string csv = Path.Combine(folder, stem + ".csv");
        using (var output = new FileStream(json, FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(output, report, JsonOptions);

        using var writer = new StreamWriter(new FileStream(csv, FileMode.CreateNew, FileAccess.Write));
        bool headerWritten = false;
        foreach (var sample in report.Samples)
        {
            var row = JsonSerializer.SerializeToElement(new
            {
                report.Info, report.Statistics, report.Warnings, Sample = sample
            }, JsonOptions);
            var columns = Flatten(row).ToArray();
            if (!headerWritten)
            {
                writer.WriteLine(string.Join(",", columns.Select(column => CsvCell(column.Name))));
                headerWritten = true;
            }
            writer.WriteLine(string.Join(",", columns.Select(column => CsvCell(column.Value))));
        }

        return (json, csv);

        static IEnumerable<(string Name, string? Value)> Flatten(JsonElement element, string prefix = "")
        {
            foreach (var property in element.EnumerateObject())
            {
                string name = prefix + property.Name;
                if (property.Name == nameof(Work.MilitaryStateAfter))
                    continue;
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var nested in Flatten(property.Value, name + "."))
                        yield return nested;
                }
                else
                {
                    yield return (name, property.Value.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.String => property.Value.GetString(),
                        _ => JsonSerializer.Serialize(property.Value)
                    });
                }
            }
        }

        static string CsvCell(string? value) =>
            value is null ? string.Empty : "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string? GitOutput(params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? output.GetAwaiter().GetResult().Trim() : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
