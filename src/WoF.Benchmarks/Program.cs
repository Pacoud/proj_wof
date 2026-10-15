
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using WoF.Benchmarks;

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

// 3. Échauffement : non chronométré.
for (int i = 0; i < warmupHours; i++)
{
    engine.Tick();
}

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

int gc0Before = GC.CollectionCount(0);
int gc1Before = GC.CollectionCount(1);
int gc2Before = GC.CollectionCount(2);

// 6. Mesurer chaque tick individuellement.
for (int i = 0; i < measuredHours; i++)
{
    long bytesBefore =
        GC.GetAllocatedBytesForCurrentThread();

    long start = Stopwatch.GetTimestamp();

    engine.Tick();

    long end = Stopwatch.GetTimestamp();

    long bytesAfter =
        GC.GetAllocatedBytesForCurrentThread();

    tickTimes[i] =
        (end - start) * 1000.0 / Stopwatch.Frequency;

    tickAllocated[i] = bytesAfter - bytesBefore;

    // Contrôle de la charge, hors chronométrage.
    activeBattles[i] =
        engine.Engagements.ActiveEngagements.Count();

    if (activeBattles[i] != expectedBattles)
    {
        throw new InvalidOperationException(
            $"Battle count changed at measured tick {i + 1}: " +
            $"{activeBattles[i]} instead of {expectedBattles}.");
    }
}

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
    $"{GC.CollectionCount(0) - gc0Before}/" +
    $"{GC.CollectionCount(1) - gc1Before}/" +
    $"{GC.CollectionCount(2) - gc2Before}");

Console.WriteLine($"Active battles at end: {activeBattles[^1]}");

// 8. Export CSV APRÈS la mesure.
string resultsDirectory = "benchmark-results";
Directory.CreateDirectory(resultsDirectory);

string filename = Path.Combine(
    resultsDirectory,
    $"C1_1_{args[0]}_d{divisionCount}" +
    $"_g{divisionsPerBattle}_" +
    $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.csv"
);

using (var writer = new StreamWriter(filename))
{
    writer.WriteLine(
        "Tick,Milliseconds,AllocatedBytes,ActiveBattles");

    for (int i = 0; i < measuredHours; i++)
    {
        writer.WriteLine(
            $"{i + 1}," +
            tickTimes[i].ToString(
                "F6", CultureInfo.InvariantCulture) + "," +
            $"{tickAllocated[i]}," +
            $"{activeBattles[i]}");
    }
}

Console.WriteLine();
Console.WriteLine($"CSV written: {filename}");

GC.KeepAlive(engine);

static double ToMiB(long bytes)
    => bytes / (1024.0 * 1024.0);

// Percentile au rang supérieur (nearest-rank).
// Le tableau doit déjà être trié.
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
