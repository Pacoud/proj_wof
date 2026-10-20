
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using WoF.Benchmarks;
using System.Text.Json;

if (args.Length > 0 && args[0] == "tactical")
{
    TacticalPerformanceBenchmark.Run(
        args.Skip(1).ToArray());

    return;
}

if (args.Length > 0 && args[0] == "history")
{
    HistoryBenchmark.Run(args.Skip(1).ToArray());
    return;
}


if (args.Length < 3 || args.Length > 5 ||
    (args[0] != "combat" && args[0] != "stationary") ||
    !int.TryParse(args[1], out int divisionCount) ||
    !int.TryParse(args[2], out int measuredHours) ||
    divisionCount <= 0 || measuredHours <= 0)
{
    PrintUsage();
    return;
}

bool combat = args[0] == "combat";

int divisionsPerBattle = 2;
int warmupHours = 24;

if (args.Length >= 4 &&
    !int.TryParse(args[3], out divisionsPerBattle))
{
    PrintUsage();
    return;
}

if (args.Length >= 5 &&
    !int.TryParse(args[4], out warmupHours))
{
    PrintUsage();
    return;
}

if (warmupHours < 0 ||
    (combat &&
        (divisionsPerBattle < 2 ||
         divisionsPerBattle % 2 != 0 ||
         divisionCount % divisionsPerBattle != 0)))
{
    PrintUsage();
    return;
}

int expectedBattles = combat
    ? divisionCount / divisionsPerBattle
    : 0;


    DateTime runStartedUtc = DateTime.UtcNow;

    #if DEBUG
    const string buildConfiguration = "Debug";
    #else
    const string buildConfiguration = "Release";
    #endif


Console.WriteLine("=== WoF C1.1 Benchmark ===");
Console.WriteLine($"Mode: {args[0]}");
Console.WriteLine($"Divisions: {divisionCount}");
Console.WriteLine($"Divisions per battle: {divisionsPerBattle}");
Console.WriteLine($"Expected battles: {expectedBattles}");
Console.WriteLine($"Warmup hours: {warmupHours}");
Console.WriteLine($"Measured hours: {measuredHours}");
Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"Logical processors: {Environment.ProcessorCount}");
Console.WriteLine($"Server GC: {GCSettings.IsServerGC}");

// 1. Construction et mémoire initiale.
long memoryBefore = GC.GetTotalMemory(true);
long buildAllocBefore = GC.GetAllocatedBytesForCurrentThread();

var buildWatch = Stopwatch.StartNew();

var engine = BenchmarkScenarioFactory.Create(
    divisionCount,
    combat,
    divisionsPerBattle
);

buildWatch.Stop();

long buildAllocated =
    GC.GetAllocatedBytesForCurrentThread()
    - buildAllocBefore;

long managedMemoryDelta =
    GC.GetTotalMemory(true) - memoryBefore;

long battalions = engine.Divisions.Sum(
    d => (long)d.Battalions.Count);

long companies = engine.Divisions.Sum(
    d => d.Battalions.Sum(
        b => (long)b.Companies.Count));

long authorizedPersonnel = engine.Divisions.Sum(
    d => (long)d.GetCurrentResourceTotals()
        .Personnel.Authorized);

if (battalions != divisionCount * 12L ||
    companies != divisionCount * 48L ||
    authorizedPersonnel != divisionCount * 10_000L)
{
    throw new InvalidOperationException(
        "Invalid benchmark hierarchy.");
}

Console.WriteLine();
Console.WriteLine("--- Construction ---");
Console.WriteLine($"Time: {buildWatch.Elapsed.TotalMilliseconds:F3} ms");
Console.WriteLine($"Allocated: {ToMiB(buildAllocated):F3} MiB");
Console.WriteLine($"Managed memory delta: {ToMiB(managedMemoryDelta):F3} MiB");
Console.WriteLine($"Battalions: {battalions:N0}");
Console.WriteLine($"Companies: {companies:N0}");
Console.WriteLine($"Authorized personnel: {authorizedPersonnel:N0}");

// 2. Créer les engagements hors mesure.
if (combat)
{
    engine.Tick();

    var initialEngagements =
        engine.Engagements.ActiveEngagements.ToArray();

    if (initialEngagements.Length != expectedBattles || 
        initialEngagements.Any(
            e => e.Participants.Count != divisionsPerBattle))
    {
        throw new InvalidOperationException(
            "Initial battle distribution is incorrect.");
    }
}

var stateBeforeWarmup =
    BenchmarkMilitaryState.Capture(engine);

// 3. Échauffement : non chronométré.
for (int i = 0; i < warmupHours; i++)
{
    engine.Tick();
}

var stateAfterWarmup =
    BenchmarkMilitaryState.Capture(engine);

int activeAfterWarmup =
    engine.Engagements.ActiveEngagements.Count();

if (activeAfterWarmup != expectedBattles)
{
    throw new InvalidOperationException(
        "Battle count changed during warmup.");
}

Console.WriteLine();
Console.WriteLine($"Active battles after warmup: {activeAfterWarmup}");

// 4. Mesurer séparément l'agrégation.
const int readPasses = 10;
long checksum = 0;

var aggregationWatch = Stopwatch.StartNew();

for (int pass = 0; pass < readPasses; pass++)
{
    foreach (var division in engine.Divisions)
    {
        checksum += division.GetCurrentResourceTotals()
            .Personnel.Available;
    }
}

aggregationWatch.Stop();

Console.WriteLine();
Console.WriteLine("--- Resource aggregation ---");
Console.WriteLine(
    $"Average pass: " +
    $"{aggregationWatch.Elapsed.TotalMilliseconds / readPasses:F3} ms");
Console.WriteLine($"Checksum: {checksum}");

// 5. Préparer les tableaux avant la mesure.
var tickTimes = new double[measuredHours];
var tickAllocated = new long[measuredHours];
var activeBattles = new int[measuredHours];
var combatCapableParticipants = new int[measuredHours];
var brokenDivisions = new int[measuredHours];

var gen0PerTick = new int[measuredHours];
var gen1PerTick = new int[measuredHours];
var gen2PerTick = new int[measuredHours];

int gc0Before = GC.CollectionCount(0);
int gc1Before = GC.CollectionCount(1);
int gc2Before = GC.CollectionCount(2);


// 6. Mesurer chaque tick individuellement.


BenchmarkEventSource.Log.MeasurementStart();

for (int i = 0; i < measuredHours; i++)
{
    BenchmarkEventSource.Log.MeasuredTickStart(i + 1);

    // Diagnostics préparatoires, hors chronométrage.
    int gc0Start = GC.CollectionCount(0);
    int gc1Start = GC.CollectionCount(1);
    int gc2Start = GC.CollectionCount(2);

    long bytesBefore =
        GC.GetAllocatedBytesForCurrentThread();

    long start = Stopwatch.GetTimestamp();

    engine.Tick();

    long end = Stopwatch.GetTimestamp();

    long bytesAfter =
        GC.GetAllocatedBytesForCurrentThread();

    // Seul engine.Tick() est chronométré.
    tickTimes[i] =
        (end - start) * 1000.0 / Stopwatch.Frequency;

    tickAllocated[i] =
        bytesAfter - bytesBefore;

    gen0PerTick[i] =
        GC.CollectionCount(0) - gc0Start;

    gen1PerTick[i] =
        GC.CollectionCount(1) - gc1Start;

    gen2PerTick[i] =
        GC.CollectionCount(2) - gc2Start;

    BenchmarkEventSource.Log.MeasuredTickStop(i + 1);

    // Contrôle militaire hors chronométrage.
    activeBattles[i] =
        engine.Engagements.ActiveEngagements.Count();

    int capable = 0;
    int broken = 0;

    foreach (var division in engine.Divisions)
    {
        if (division.IsBroken)
            broken++;

        if (division.IsEngaged && !division.IsBroken)
            capable++;
    }

    combatCapableParticipants[i] = capable;
    brokenDivisions[i] = broken;
}

BenchmarkEventSource.Log.MeasurementStop();

// Capturer immédiatement les compteurs de fin
// avant les calculs statistiques et les exports.
int gc0After = GC.CollectionCount(0);
int gc1After = GC.CollectionCount(1);
int gc2After = GC.CollectionCount(2);

// Photographie complète hors chronométrage.
var stateAfterMeasurement =
    BenchmarkMilitaryState.Capture(engine);


// 7. Calculer les statistiques.
double average = tickTimes.Average();

double[] sorted = (double[])tickTimes.Clone();
Array.Sort(sorted);

double median = sorted.Length % 2 == 0
    ? (sorted[sorted.Length / 2 - 1] +
       sorted[sorted.Length / 2]) / 2
    : sorted[sorted.Length / 2];

long totalAllocated = tickAllocated.Sum();

Console.WriteLine();
Console.WriteLine("--- Tick latency ---");
Console.WriteLine($"Average: {average:F3} ms");
Console.WriteLine($"Median: {median:F3} ms");
Console.WriteLine($"P95: {Percentile(sorted, 0.95):F3} ms");
Console.WriteLine($"P99: {Percentile(sorted, 0.99):F3} ms");
Console.WriteLine($"Minimum: {sorted[0]:F3} ms");
Console.WriteLine($"Maximum: {sorted[^1]:F3} ms");

Console.WriteLine();
Console.WriteLine("--- Allocations and GC ---");
Console.WriteLine(
    $"Allocations per tick: " +
    $"{ToMiB(totalAllocated) / measuredHours:F3} MiB");

Console.WriteLine(
    $"Gen0/1/2: " +
    $"{gc0After - gc0Before}/" +
    $"{gc1After - gc1Before}/" +
    $"{gc2After - gc2Before}");

Console.WriteLine($"Active battles at end: {activeBattles[^1]}");



int expectedCapable = combat ? divisionCount : 0;

bool battleCountsValid =
    activeBattles.All(x => x == expectedBattles);

bool combatantsValid =
    combatCapableParticipants.All(
        x => x == expectedCapable);

bool noBrokenDivisions =
    brokenDivisions.All(x => x == 0);

bool scenarioComparable =
    battleCountsValid &&
    combatantsValid &&
    noBrokenDivisions &&
    stateAfterWarmup.ActiveBattles == expectedBattles &&
    stateAfterWarmup.CombatCapableParticipants ==
        expectedCapable &&
    stateAfterWarmup.BrokenDivisions == 0;

Console.WriteLine();
Console.WriteLine("--- Military validity ---");
Console.WriteLine($"Comparable: {scenarioComparable}");

Console.WriteLine(
    $"Combat-capable at end: " +
    $"{combatCapableParticipants[^1]}");

Console.WriteLine(
    $"Broken divisions at end: " +
    $"{brokenDivisions[^1]}");

Console.WriteLine(
    $"Personnel at end: " +
    $"{stateAfterMeasurement.AvailablePersonnel:N0}");

Console.WriteLine(
    $"Total fuel at end: " +
    $"{stateAfterMeasurement.TotalFuel:F2}");

if (!scenarioComparable)
{
    Console.WriteLine(
        "WARNING: Military workload changed. " +
        "Review the diagnostics before comparing results.");
}

// 8. Export CSV APRÈS la mesure.
string resultsDirectory = "benchmark-results";
Directory.CreateDirectory(resultsDirectory);


string runId =
    $"{runStartedUtc:yyyyMMdd_HHmmss_fff}" +
    $"_p{Environment.ProcessId}";

string filename = Path.Combine(
    resultsDirectory,
    $"C1_2a_{args[0]}_d{divisionCount}" +
    $"_g{divisionsPerBattle}_{runId}.csv"
);

using (var writer = new StreamWriter(filename))
{
    writer.WriteLine(
        "Tick,Milliseconds,AllocatedBytes," +
        "ActiveBattles,CombatCapableParticipants," +
        "BrokenDivisions,Gen0,Gen1,Gen2"
    );

    for (int i = 0; i < measuredHours; i++)
    {
        writer.WriteLine(
            $"{i + 1}," +
            tickTimes[i].ToString(
                "F6", CultureInfo.InvariantCulture) + "," +
            $"{tickAllocated[i]}," +
            $"{activeBattles[i]}," +
            $"{combatCapableParticipants[i]}," +
            $"{brokenDivisions[i]}," +
            $"{gen0PerTick[i]}," +
            $"{gen1PerTick[i]}," +
            $"{gen2PerTick[i]}"
        );
    }
}

Console.WriteLine($"CSV written: {filename}");



string? gitRevision =
    GetGitOutput("rev-parse", "HEAD");

string? gitStatus =
    GetGitOutput(
        "status",
        "--porcelain",
        "--untracked-files=normal"
    );

var metadata = new
{
    BenchmarkVersion = "C1.2a",
    StartedUtc = runStartedUtc,
    FinishedUtc = DateTime.UtcNow,

    Source = new
    {
        GitRevision = gitRevision,
        WorkingTreeDirty = gitStatus is null
            ? (bool?)null
            : gitStatus.Length > 0
    },

    Environment = new
    {
        BuildConfiguration = buildConfiguration,
        Framework = RuntimeInformation.FrameworkDescription,
        OperatingSystem = RuntimeInformation.OSDescription,
        Architecture =
            RuntimeInformation.ProcessArchitecture.ToString(),
        LogicalProcessors = System.Environment.ProcessorCount,
        ServerGC = GCSettings.IsServerGC,
        TieredPGOOverride =
            System.Environment.GetEnvironmentVariable(
                "DOTNET_TieredPGO")
    },

    Scenario = new
    {
        Mode = args[0],
        Divisions = divisionCount,
        DivisionsPerBattle = divisionsPerBattle,
        ExpectedBattles = expectedBattles,
        WarmupHours = warmupHours,
        MeasuredHours = measuredHours
    },

    MilitaryStates = new
    {
        BeforeWarmup = stateBeforeWarmup,
        AfterWarmup = stateAfterWarmup,
        AfterMeasurement = stateAfterMeasurement
    },

    Validation = new
    {
        Comparable = scenarioComparable,
        BattleCountsValid = battleCountsValid,
        CombatantsValid = combatantsValid,
        NoBrokenDivisions = noBrokenDivisions
    },

    Performance = new
    {
        AverageMilliseconds = average,
        MedianMilliseconds = median,
        P95Milliseconds = Percentile(sorted, 0.95),
        P99Milliseconds = Percentile(sorted, 0.99),
        MaximumMilliseconds = sorted[^1],
        AverageAllocatedBytes =
            (double)totalAllocated / measuredHours
    },

    GarbageCollector = new
    {
        Gen0Collections = gc0After - gc0Before,
        Gen1Collections = gc1After - gc1Before,
        Gen2Collections = gc2After - gc2Before
    },

    CsvFile = Path.GetFileName(filename)
};

string jsonFilename =
    Path.ChangeExtension(filename, ".json");

File.WriteAllText(
    jsonFilename,
    JsonSerializer.Serialize(
        metadata,
        new JsonSerializerOptions
        {
            WriteIndented = true
        }
    )
);

Console.WriteLine($"Metadata written: {jsonFilename}");

GC.KeepAlive(engine);

static double ToMiB(long bytes)
    => bytes / (1024.0 * 1024.0);

static double Percentile(double[] sorted, double fraction)
{
    int index = Math.Clamp(
        (int)Math.Ceiling(fraction * sorted.Length) - 1,
        0,
        sorted.Length - 1
    );

    return sorted[index];
}

static void PrintUsage()
{
    Console.WriteLine(
        "Usage: <stationary|combat> <divisions> <hours> " +
        "[divisionsPerBattle] [warmupHours]");
}

static string? GetGitOutput(params string[] arguments)
{
    try
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);

        if (process is null)
            return null;

        string output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(3000))
        {
            process.Kill();
            return null;
        }

        return process.ExitCode == 0
            ? output.Trim()
            : null;
    }
    catch
    {
        // Le benchmark doit rester utilisable
        // même si Git n'est pas installé.
        return null;
    }
}
