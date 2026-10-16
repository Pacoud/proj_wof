using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Benchmarks;

public static class HistoryBenchmark
{
    private const int DivisionCount = 500;
    private const int DivisionsPerBattle = 10;
    private const int ExpectedActiveBattles = DivisionCount / DivisionsPerBattle;
    private const int LookupRepetitions = 5;
    private const int SeedBatchSize = 100;

    public static void Run(string[] arguments)
    {
        if (arguments.Length < 1 || arguments.Length > 3 ||
            !int.TryParse(arguments[0], out int archivedCount) ||
            archivedCount < 0 ||
            (arguments.Length >= 2 &&
             (!int.TryParse(arguments[1], out _) ||
              int.Parse(arguments[1]) <= 0)) ||
            (arguments.Length >= 3 &&
             (!int.TryParse(arguments[2], out _) ||
              int.Parse(arguments[2]) < 0)))
        {
            Console.WriteLine(
                "Usage: history <archivedBattles> [measuredTicks=72] [warmupTicks=24]");
            Environment.ExitCode = 2;
            return;
        }

        int measuredTicks = arguments.Length >= 2
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : 72;
        int warmupTicks = arguments.Length >= 3
            ? int.Parse(arguments[2], CultureInfo.InvariantCulture)
            : 24;

        DateTime startedUtc = DateTime.UtcNow;
        Console.WriteLine(
            $"C1.2d-History | archive={archivedCount} | " +
            $"active={ExpectedActiveBattles} | " +
            $"divisions={DivisionCount} | ticks={measuredTicks}");

        // Charge militaire identique pour toutes les exécutions.
        var engine = BenchmarkScenarioFactory.Create(
            DivisionCount, combat: true,
            divisionsPerBattle: DivisionsPerBattle);

        // L'archivage et la création des batailles sont HORS mesure.
        var seedWatch = Stopwatch.StartNew();
        SeedArchivedBattles(engine, archivedCount);
        seedWatch.Stop();

        // L'heure 1 détecte les 50 batailles principales.
        engine.Tick();
        Validate(engine, archivedCount);
        var beforeWarmup = BenchmarkMilitaryState.Capture(engine);

        for (int i = 0; i < warmupTicks; i++)
            engine.Tick();

        Validate(engine, archivedCount);
        var afterWarmup = BenchmarkMilitaryState.Capture(engine);

        var lookupMilliseconds = new double[measuredTicks];
        var lookupAllocatedBytes = new double[measuredTicks];
        var tickMilliseconds = new double[measuredTicks];
        var tickAllocatedBytes = new long[measuredTicks];
        var activeBattles = new int[measuredTicks];
        var archivedBattles = new int[measuredTicks];
        var capableDivisions = new int[measuredTicks];
        var brokenDivisions = new int[measuredTicks];

        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);

        for (int i = 0; i < measuredTicks; i++)
        {
            // Recherche seule : aucune division ne rejoint un nouveau
            // combat, car les 500 divisions sont déjà engagées.
            long lookupBytesStart = GC.GetAllocatedBytesForCurrentThread();
            long lookupStart = Stopwatch.GetTimestamp();

            for (int repetition = 0; repetition < LookupRepetitions; repetition++)
            {
                engine.Engagements.DetectProvinceEngagements(
                    engine.Divisions,
                    engine.Clock.CurrentHour);
            }

            long lookupStop = Stopwatch.GetTimestamp();
            long lookupBytesStop = GC.GetAllocatedBytesForCurrentThread();

            lookupMilliseconds[i] =
                (lookupStop - lookupStart) * 1000.0 /
                Stopwatch.Frequency / LookupRepetitions;
            lookupAllocatedBytes[i] =
                (double)(lookupBytesStop - lookupBytesStart) /
                LookupRepetitions;

            // Mesure indépendante du tick complet.
            long tickBytesStart = GC.GetAllocatedBytesForCurrentThread();
            long tickStart = Stopwatch.GetTimestamp();
            engine.Tick();
            long tickStop = Stopwatch.GetTimestamp();
            long tickBytesStop = GC.GetAllocatedBytesForCurrentThread();

            tickMilliseconds[i] =
                (tickStop - tickStart) * 1000.0 / Stopwatch.Frequency;
            tickAllocatedBytes[i] = tickBytesStop - tickBytesStart;

            // Contrôles hors chronométrage.
            activeBattles[i] = engine.Engagements.ActiveEngagements.Count();
            archivedBattles[i] = engine.Engagements.History.Reports.Count;
            capableDivisions[i] = engine.Divisions.Count(
                division => division.IsEngaged && !division.IsBroken);
            brokenDivisions[i] = engine.Divisions.Count(
                division => division.IsBroken);

            if (activeBattles[i] != ExpectedActiveBattles ||
                archivedBattles[i] != archivedCount ||
                capableDivisions[i] != DivisionCount ||
                brokenDivisions[i] != 0)
            {
                throw new InvalidOperationException(
                    $"Military workload changed at measured tick {i + 1}.");
            }
        }

        int gen0 = GC.CollectionCount(0) - gen0Before;
        int gen1 = GC.CollectionCount(1) - gen1Before;
        int gen2 = GC.CollectionCount(2) - gen2Before;
        var afterMeasurement = BenchmarkMilitaryState.Capture(engine);

        string directory = Path.Combine(
            "benchmark-results", "C1_2d_History");
        Directory.CreateDirectory(directory);

        string stem = $"history_h{archivedCount}_d{DivisionCount}" +
            $"_g{DivisionsPerBattle}_{startedUtc:yyyyMMdd_HHmmss_fff}" +
            $"_p{Environment.ProcessId}";
        string csvPath = Path.Combine(directory, stem + ".csv");

        using (var writer = new StreamWriter(csvPath))
        {
            writer.WriteLine(
                "Tick,LookupMillisecondsPerPass,LookupAllocatedBytesPerPass," +
                "FullTickMilliseconds,FullTickAllocatedBytes," +
                "ActiveBattles,ArchivedBattles,CombatCapableDivisions,BrokenDivisions");

            for (int i = 0; i < measuredTicks; i++)
            {
                writer.WriteLine(string.Join(",",
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    lookupMilliseconds[i].ToString("F6", CultureInfo.InvariantCulture),
                    lookupAllocatedBytes[i].ToString("F2", CultureInfo.InvariantCulture),
                    tickMilliseconds[i].ToString("F6", CultureInfo.InvariantCulture),
                    tickAllocatedBytes[i].ToString(CultureInfo.InvariantCulture),
                    activeBattles[i].ToString(CultureInfo.InvariantCulture),
                    archivedBattles[i].ToString(CultureInfo.InvariantCulture),
                    capableDivisions[i].ToString(CultureInfo.InvariantCulture),
                    brokenDivisions[i].ToString(CultureInfo.InvariantCulture)));
            }
        }

        string? revision = GitOutput("rev-parse", "HEAD");
        string? dirtyStatus = GitOutput("status", "--porcelain", "--untracked-files=normal");

        var metadata = new
        {
            BenchmarkVersion = "C1.2d-History-v1",
            StartedUtc = startedUtc,
            FinishedUtc = DateTime.UtcNow,
            Source = new
            {
                GitRevision = revision,
                WorkingTreeDirty = dirtyStatus is null
                    ? (bool?)null : dirtyStatus.Length > 0
            },
            Environment = new
            {
                BuildConfiguration = "Release (expected; use -c Release)",
                Framework = RuntimeInformation.FrameworkDescription,
                OperatingSystem = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Processors = Environment.ProcessorCount,
                ServerGC = GCSettings.IsServerGC,
                TieredPGOOverride = Environment.GetEnvironmentVariable("DOTNET_TieredPGO")
            },
            Scenario = new
            {
                ArchivedBattles = archivedCount,
                ActiveBattles = ExpectedActiveBattles,
                Divisions = DivisionCount,
                DivisionsPerBattle,
                WarmupTicks = warmupTicks,
                MeasuredTicks = measuredTicks,
                LookupRepetitions,
                SeedTimeMilliseconds = seedWatch.Elapsed.TotalMilliseconds
            },
            Performance = new
            {
                MeanLookupMilliseconds = lookupMilliseconds.Average(),
                MedianLookupMilliseconds = Median(lookupMilliseconds),
                P95LookupMilliseconds = Percentile(lookupMilliseconds, .95),
                MeanTickMilliseconds = tickMilliseconds.Average(),
                MedianTickMilliseconds = Median(tickMilliseconds),
                P95TickMilliseconds = Percentile(tickMilliseconds, .95),
                MeanLookupAllocatedBytes = lookupAllocatedBytes.Average(),
                MeanTickAllocatedBytes = tickAllocatedBytes.Average()
            },
            GarbageCollector = new { Gen0 = gen0, Gen1 = gen1, Gen2 = gen2 },
            MilitaryStates = new
            {
                BeforeWarmup = beforeWarmup,
                AfterWarmup = afterWarmup,
                AfterMeasurement = afterMeasurement
            },
            Validation = new
            {
                Comparable = true,
                ActiveBattles = activeBattles[^1],
                ArchivedBattles = archivedBattles[^1],
                CapableDivisions = capableDivisions[^1],
                BrokenDivisions = brokenDivisions[^1]
            },
            CsvFile = Path.GetFileName(csvPath)
        };

        string jsonPath = Path.ChangeExtension(csvPath, ".json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(
            metadata, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"History creation (outside timing): {seedWatch.Elapsed.TotalMilliseconds:F2} ms");
        Console.WriteLine($"Lookup: {lookupMilliseconds.Average():F3} ms/pass");
        Console.WriteLine($"Full tick: {tickMilliseconds.Average():F3} ms/tick");
        Console.WriteLine($"Lookup allocated: {lookupAllocatedBytes.Average() / 1048576.0:F3} MiB/pass");
        Console.WriteLine($"Full tick allocated: {tickAllocatedBytes.Average() / 1048576.0:F3} MiB/tick");
        Console.WriteLine($"GC Gen0/1/2: {gen0}/{gen1}/{gen2}");
        Console.WriteLine($"Military state: comparable; active={ExpectedActiveBattles}, archived={archivedCount}");
        Console.WriteLine($"CSV: {csvPath}");
        Console.WriteLine($"JSON: {jsonPath}");
        GC.KeepAlive(engine);
    }

    private static void SeedArchivedBattles(
        SimulationEngine engine, int archivedCount)
    {
        var france = new Country(101, "Archive France");
        var germany = new Country(102, "Archive Germany");
        engine.DeclareWar(france, germany);

        // Petits lots : évite de garder 10 000 engagements
        // simultanément actifs pendant la préparation.
        for (int start = 0; start < archivedCount; start += SeedBatchSize)
        {
            int batchCount = Math.Min(SeedBatchSize, archivedCount - start);
            var divisions = new List<Division>(batchCount * 2);

            for (int j = 0; j < batchCount; j++)
            {
                int id = start + j + 1;
                var province = new Province(
                    1_000_000 + id, $"Archived Province {id}", owner: france);

                divisions.Add(new Division(
                    $"Archive French {id}", province,
                    fuel: 100, ammunition: 100,
                    country: france, maxOrganization: 100_000));

                divisions.Add(new Division(
                    $"Archive German {id}", province,
                    fuel: 100, ammunition: 100,
                    country: germany, maxOrganization: 0.01));
            }

            engine.Engagements.DetectProvinceEngagements(
                divisions, currentHour: 0);
            engine.Engagements.ProcessEngagements(currentHour: 1);

            if (engine.Engagements.ActiveEngagements.Any())
                throw new InvalidOperationException(
                    "A seed battle was not archived after combat.");
        }

        if (engine.Engagements.History.Reports.Count != archivedCount ||
            engine.Engagements.Engagements.Count != archivedCount)
        {
            throw new InvalidOperationException(
                "History seed count does not match the request.");
        }
    }

    private static void Validate(SimulationEngine engine, int archivedCount)
    {
        var engagements = engine.Engagements.ActiveEngagements.ToArray();
        if (engagements.Length != ExpectedActiveBattles ||
            engagements.Any(e => e.Participants.Count != DivisionsPerBattle) ||
            engine.Engagements.History.Reports.Count != archivedCount ||
            engine.Divisions.Count(d => d.IsEngaged && !d.IsBroken) != DivisionCount)
        {
            throw new InvalidOperationException(
                "Initial active workload or archived history is incorrect.");
        }
    }

    private static double Median(double[] data)
    {
        var sorted = (double[])data.Clone();
        Array.Sort(sorted);
        return sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2
            : sorted[sorted.Length / 2];
    }

    private static double Percentile(double[] data, double fraction)
    {
        var sorted = (double[])data.Clone();
        Array.Sort(sorted);
        int index = Math.Clamp(
            (int)Math.Ceiling(fraction * sorted.Length) - 1,
            0, sorted.Length - 1);
        return sorted[index];
    }

    private static string? GitOutput(params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process is null)
                return null;
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return null;
            }
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
