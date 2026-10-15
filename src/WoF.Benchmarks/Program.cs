
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using WoF.Benchmarks;

if (args.Length != 3 ||
    (args[0] != "stationary" && args[0] != "combat") ||
    !int.TryParse(args[1], out int divisionCount) ||
    !int.TryParse(args[2], out int hours) ||
    divisionCount <= 0 ||
    hours <= 0 ||
    (args[0] == "combat" && divisionCount % 2 != 0))
{
    Console.WriteLine(
        "Usage: <stationary|combat> <divisions> <hours>");
    return;
}

bool combat = args[0] == "combat";

Console.WriteLine("=== WoF Scale Benchmark ===");
Console.WriteLine($"Mode: {args[0]}");
Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"CPU logical processors: {Environment.ProcessorCount}");
Console.WriteLine($"Server GC: {GCSettings.IsServerGC}");
Console.WriteLine();

// Nettoyage avant construction.
// Exclu des mesures chronométrées.
long baselineMemory = GC.GetTotalMemory(true);

long allocatedBeforeBuild =
    GC.GetAllocatedBytesForCurrentThread();

var buildWatch = Stopwatch.StartNew();

var engine = BenchmarkScenarioFactory.Create(
    divisionCount,
    combat);

buildWatch.Stop();

long buildAllocations =
    GC.GetAllocatedBytesForCurrentThread()
    - allocatedBeforeBuild;

long managedMemory =
    GC.GetTotalMemory(true) - baselineMemory;

// Vérifier la structure en dehors du chronomètre.
long battalionCount =
    engine.Divisions.Sum(d => (long)d.Battalions.Count);

long companyCount =
    engine.Divisions.Sum(d =>
        d.Battalions.Sum(b => (long)b.Companies.Count));

long authorizedPersonnel =
    engine.Divisions.Sum(d =>
        (long)d.GetCurrentResourceTotals()
            .Personnel.Authorized);

if (engine.Divisions.Count != divisionCount ||
    battalionCount != divisionCount * 12L ||
    companyCount != divisionCount * 48L ||
    authorizedPersonnel != divisionCount * 10_000L)
{
    throw new InvalidOperationException(
        "Invalid benchmark scenario.");
}

Console.WriteLine($"Divisions: {divisionCount:N0}");
Console.WriteLine($"Battalions: {battalionCount:N0}");
Console.WriteLine($"Companies: {companyCount:N0}");
Console.WriteLine($"Personnel: {authorizedPersonnel:N0}");
Console.WriteLine();

Console.WriteLine(
    $"Construction: {buildWatch.Elapsed.TotalMilliseconds:F2} ms");

Console.WriteLine(
    $"Build allocations: {ToMiB(buildAllocations):F2} MiB");

Console.WriteLine(
    $"Managed memory delta: {ToMiB(managedMemory):F2} MiB");

// Dans le mode combat, le premier tick
// crée les engagements entre divisions.
if (combat)
{
    var detectionWatch = Stopwatch.StartNew();

    engine.Tick();

    detectionWatch.Stop();

    Console.WriteLine(
        $"Initial contact: {detectionWatch.Elapsed.TotalMilliseconds:F2} ms");

    Console.WriteLine(
        $"Initial battles: {engine.Engagements.ActiveEngagements.Count():N0}");
}

// Premier tick hors mesure : échauffement.
engine.Tick();

// Échauffement du parcours des ressources.
foreach (var division in engine.Divisions)
    _ = division.GetCurrentResourceTotals();

// BENCHMARK 1 : agrégation.
const int readPasses = 10;

long checksum = 0;
long allocatedBeforeRead =
    GC.GetAllocatedBytesForCurrentThread();

var readWatch = Stopwatch.StartNew();

for (int pass = 0; pass < readPasses; pass++)
{
    foreach (var division in engine.Divisions)
    {
        checksum += division.GetCurrentResourceTotals()
            .Personnel.Available;
    }
}

readWatch.Stop();

long readAllocations =
    GC.GetAllocatedBytesForCurrentThread()
    - allocatedBeforeRead;

Console.WriteLine();
Console.WriteLine("--- Resource aggregation ---");

Console.WriteLine(
    $"Average full pass: {readWatch.Elapsed.TotalMilliseconds / readPasses:F3} ms");

Console.WriteLine(
    $"Allocations per pass: {ToMiB(readAllocations) / readPasses:F3} MiB");

Console.WriteLine($"Checksum: {checksum}");

// BENCHMARK 2 : simulation complète.
long allocatedBeforeTicks =
    GC.GetAllocatedBytesForCurrentThread();

int gc0Before = GC.CollectionCount(0);
int gc1Before = GC.CollectionCount(1);
int gc2Before = GC.CollectionCount(2);

var tickWatch = Stopwatch.StartNew();

for (int hour = 0; hour < hours; hour++)
{
    engine.Tick();
}

tickWatch.Stop();

long tickAllocations =
    GC.GetAllocatedBytesForCurrentThread()
    - allocatedBeforeTicks;

Console.WriteLine();
Console.WriteLine("--- Simulation ticks ---");

Console.WriteLine(
    $"Total: {tickWatch.Elapsed.TotalMilliseconds:F2} ms");

Console.WriteLine(
    $"Average tick: {tickWatch.Elapsed.TotalMilliseconds / hours:F3} ms");

Console.WriteLine(
    $"Allocations per tick: {ToMiB(tickAllocations) / hours:F3} MiB");

Console.WriteLine(
    $"GC collections (Gen0/1/2): " +
    $"{GC.CollectionCount(0) - gc0Before}/" +
    $"{GC.CollectionCount(1) - gc1Before}/" +
    $"{GC.CollectionCount(2) - gc2Before}");

Console.WriteLine(
    $"Active battles at end: " +
    $"{engine.Engagements.ActiveEngagements.Count():N0}");

Console.WriteLine($"Final simulated hour: {engine.Clock.CurrentHour}");

GC.KeepAlive(engine);

static double ToMiB(long bytes) =>
    bytes / (1024.0 * 1024.0);
