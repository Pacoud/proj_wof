
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Benchmarks;

public static class TacticalPerformanceBenchmark
{
    private readonly record struct Sample(
        double DetectionMs,
        long DetectionBytes,
        double PublicationMs,
        long PublicationBytes,
        double DeliveryMs,
        long DeliveryBytes,
        int PublishedReports);

    private sealed class Scenario : IDisposable
    {
        public TacticalBattle Battle { get; }
        public TacticalGeometryMap Geometry { get; }
        public DiplomacySystem Diplomacy { get; }
        public TacticalPerceptionState Perception { get; }

        public TacticalSignatureFrame Signatures { get; }
        public TacticalDetectionProfile DetectionProfile { get; }

        public TacticalCommunicationNetwork Network { get; }
        public TacticalMessageRouter Router { get; }
        public TacticalNetworkIntelligenceRelay Relay { get; }

        public Division FrenchDivision { get; }
        public TacticalCommunicationNode DivisionNode { get; }

        public List<CompanyDeployment> FrenchUnits { get; } = new();
        public List<TacticalCommunicationNode> FrenchNodes { get; } = new();

        public int CandidateCount { get; }

        public Scenario(int companiesPerSide, int sectorCount)
        {
            var france = new Country(1, "France");
            var germany = new Country(2, "Germany");
            var province = new Province(
                1, "Benchmark", TerrainType.Plains);

            var french = MakeFormation(
                france, province, companiesPerSide);

            var german = MakeFormation(
                germany, province, companiesPerSide);

            FrenchDivision = french.Division;

            Diplomacy = new DiplomacySystem();
            Diplomacy.DeclareWar(france, germany);

            var engagements = new EngagementSystem(Diplomacy);
            engagements.DetectProvinceEngagements(
                new[] { french.Division, german.Division },
                currentHour: 1);

            var engagement = engagements.ActiveEngagements.Single();

            var field = new TacticalBattlefield();
            var sectors = new BattleSector[sectorCount];

            for (int i = 0; i < sectorCount; i++)
            {
                sectors[i] = new BattleSector(
                    i + 1, $"Secteur {i + 1}", TerrainType.Plains);

                field.AddSector(sectors[i]);

                if (i > 0)
                    field.Connect(
                        sectors[i - 1], sectors[i], 1800);
            }

            Battle = new TacticalBattle(engagement, field);
            Geometry = new TacticalGeometryMap(Battle);

            for (int i = 0; i < sectorCount; i++)
            {
                Geometry.DefineSector(
                    sectors[i],
                    i * 1800 + 90,
                    0,
                    radiusMeters: 500);
            }

            Signatures = new TacticalSignatureFrame(Battle);

            for (int i = 0; i < companiesPerSide; i++)
            {
                int sectorIndex = i % sectorCount;
                var sector = sectors[sectorIndex];

                double baseX = sectorIndex * 1800;
                double y =
                    ((i / sectorCount) % 20 - 10) * 12;

                var frenchUnit = Battle.DeployCompany(
                    french.LineCompanies[i],
                    sector,
                    CompanyDeploymentRole.FrontLine);

                var germanUnit = Battle.DeployCompany(
                    german.LineCompanies[i],
                    sector,
                    CompanyDeploymentRole.FrontLine);

                Geometry.PlaceCompany(
                    frenchUnit,
                    new TacticalPoint(baseX, y, 100),
                    facingDegrees: 90,
                    frontageMeters: 40);

                Geometry.PlaceCompany(
                    germanUnit,
                    new TacticalPoint(baseX + 180, y, 100),
                    facingDegrees: 270,
                    frontageMeters: 40);

                FrenchUnits.Add(frenchUnit);

                var signature = new TacticalUnitSignature(
                    TacticalActivity.Stationary,
                    TacticalConcealment.None);

                Signatures.Set(frenchUnit, signature);
                Signatures.Set(germanUnit, signature);
            }

            Perception = new TacticalPerceptionState(Battle);

            DetectionProfile = new TacticalDetectionProfile(
                maxVisualRangeMeters: 450,
                maxAcousticRangeMeters: 0,
                fieldOfViewDegrees: 160,
                localizationCellMeters: 50);

            CandidateCount =
                TacticalContactDetector.Detect(
                    Battle, Diplomacy).Count;

            Network = new TacticalCommunicationNetwork(Battle);

            var battalionNode =
                Network.ForBattalion(french.Battalion);

            DivisionNode =
                Network.ForDivision(french.Division);

            Network.AddDirectedLink(
                battalionNode,
                DivisionNode,
                TacticalCommunicationMedium.Radio,
                delayMinutes: 10);

            Network.AddDirectedLink(
                battalionNode,
                DivisionNode,
                TacticalCommunicationMedium.Courier,
                delayMinutes: 60);

            Network.BindRadioOperator(
                battalionNode, french.Signals);

            Network.BindRadioOperator(
                DivisionNode, french.Signals);

            foreach (var unit in FrenchUnits)
            {
                var node = Network.ForCompany(unit);
                FrenchNodes.Add(node);

                Network.AddDirectedLink(
                    node,
                    battalionNode,
                    TacticalCommunicationMedium.Radio,
                    delayMinutes: 5);

                Network.AddDirectedLink(
                    node,
                    battalionNode,
                    TacticalCommunicationMedium.Courier,
                    delayMinutes: 40);

                Network.BindRadioOperator(node, unit.Company);
            }

            Network.EnablePhysicalRadioChecks();

            Router = new TacticalMessageRouter(Network);

            Relay = new TacticalNetworkIntelligenceRelay(
                Battle, Perception, Network, Router);
        }

        public Sample RunPass(long hour)
        {
            long allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();

            long detectionStart = Stopwatch.GetTimestamp();

            TacticalDetectionSystem.Update(
                Battle,
                Geometry,
                Diplomacy,
                Perception,
                Signatures,
                hour,
                DetectionProfile);

            long detectionEnd = Stopwatch.GetTimestamp();

            long detectionBytes =
                GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;

            allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();

            long publicationStart = Stopwatch.GetTimestamp();

            int published = 0;
            long minute = checked(hour * 60);

            foreach (var unit in FrenchUnits)
            {
                // Politique de charge constante :
                // un contact valide par compagnie et
                // par heure de simulation.
                var report = Perception.GetReports(unit)
                    .FirstOrDefault(r =>
                        r.IsCurrentlyObserved
                        && r.LastObservedHour == hour);

                if (report is not null
                    && Relay.Publish(unit, report, minute))
                {
                    published++;
                }
            }

            long publicationEnd = Stopwatch.GetTimestamp();

            long publicationBytes =
                GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;

            allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();

            long deliveryStart = Stopwatch.GetTimestamp();

            // Les messages radio mettent 5 + 10 minutes.
            Relay.Step(minute + 15);

            long deliveryEnd = Stopwatch.GetTimestamp();

            long deliveryBytes =
                GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;

            if (published != FrenchUnits.Count)
                throw new InvalidOperationException(
                    "Benchmark publication workload changed.");

            return new Sample(
                ToMs(detectionEnd - detectionStart),
                detectionBytes,
                ToMs(publicationEnd - publicationStart),
                publicationBytes,
                ToMs(deliveryEnd - deliveryStart),
                deliveryBytes,
                published);
        }

        public void Dispose() => Network.Dispose();
    }

    private static (
        Division Division,
        Battalion Battalion,
        Company[] LineCompanies,
        Company Signals)
        MakeFormation(
            Country country,
            Province province,
            int count)
    {
        var division = new Division(
            country.Name + " Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: count * 100 + 10,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0,
                radios: count + 1));

        var battalion = new Battalion(
            1, "Main Battalion", BattalionType.Infantry);

        var companies = new Company[count];

        for (int i = 0; i < count; i++)
        {
            companies[i] = new Company(
                i + 1,
                $"Company {i + 1}",
                CompanyType.Infantry,
                new CompanyComposition(
                    manpower: 100,
                    radios: 1));

            battalion.AddCompany(companies[i]);
        }

        var signals = new Company(
            count + 1,
            "Signals Company",
            CompanyType.Signals,
            new CompanyComposition(
                manpower: 10,
                radios: 1));

        battalion.AddCompany(signals);
        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return (division, battalion, companies, signals);
    }

    private static double ToMs(long ticks) =>
        ticks * 1000.0 / Stopwatch.Frequency;

    private static void PrintStatistics(
        string name,
        IReadOnlyList<Sample> samples,
        Func<Sample, double> getMs,
        Func<Sample, long> getBytes)
    {
        double[] sorted = samples
            .Select(getMs)
            .OrderBy(x => x)
            .ToArray();

        double median = sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1]
               + sorted[sorted.Length / 2]) / 2
            : sorted[sorted.Length / 2];

        int p95Index = Math.Clamp(
            (int)Math.Ceiling(sorted.Length * 0.95) - 1,
            0,
            sorted.Length - 1);

        double avgBytes = samples.Average(
            x => (double)getBytes(x));

        Console.WriteLine(
            $"{name,-16} " +
            $"median={median,10:F3} ms  " +
            $"p95={sorted[p95Index],10:F3} ms  " +
            $"alloc={avgBytes / 1024.0,12:F1} KiB/passe");
    }

    public static void Run(string[] args)
    {

        bool waitForProfiler =
            args.Length == 5 &&
            args[4] == "--wait-for-profiler";
        if (args.Length != 4 && !waitForProfiler
            || !int.TryParse(args[0], out int perSide)
            || !int.TryParse(args[1], out int sectors)
            || !int.TryParse(args[2], out int repetitions)
            || !int.TryParse(args[3], out int warmup)
            || perSide < 1 || perSide > 512
            || sectors < 1 || sectors > 32
            || repetitions < 5 || repetitions > 2000
            || warmup < 1 || warmup > 50)
        {
            Console.WriteLine(
                "Usage: tactical <compagniesParCamp> " +
                "<secteurs> <mesures> <echauffement>");

            Environment.ExitCode = 2;
            return;
        }

        long beforeMemory = GC.GetTotalMemory(true);
        long buildStart = Stopwatch.GetTimestamp();

        using var scenario = new Scenario(perSide, sectors);

        double buildMs =
            ToMs(Stopwatch.GetTimestamp() - buildStart);

        long memoryAfterBuild = GC.GetTotalMemory(true);

        // Sonde de routage froid puis chaud.
        long start = Stopwatch.GetTimestamp();

        foreach (var node in scenario.FrenchNodes)
        {
            var route = scenario.Router.FindRoute(
                node, scenario.DivisionNode);

            if (route?.TotalMinutes != 15)
                throw new InvalidOperationException(
                    "Unexpected initial communication route.");
        }

        double coldRoutingMs =
            ToMs(Stopwatch.GetTimestamp() - start);

        start = Stopwatch.GetTimestamp();

        foreach (var node in scenario.FrenchNodes)
        {
            scenario.Router.FindRoute(
                node, scenario.DivisionNode);
        }

        double hotRoutingMs =
            ToMs(Stopwatch.GetTimestamp() - start);

        for (int i = 1; i <= warmup; i++)
            scenario.RunPass(i);

        if (waitForProfiler)
        {
            Console.WriteLine(
                $"PROFILE_READY PID={Environment.ProcessId}");

            Console.WriteLine(
                "Démarre dotnet-trace dans un autre terminal, " +
                "puis appuie sur Entrée ici.");

            Console.ReadLine();
        }

        int gc0Before = GC.CollectionCount(0);
        int gc1Before = GC.CollectionCount(1);
        int gc2Before = GC.CollectionCount(2);

        var samples = new Sample[repetitions];

        for (int i = 0; i < repetitions; i++)
            samples[i] = scenario.RunPass(warmup + i + 1);

        int gc0 = GC.CollectionCount(0) - gc0Before;
        int gc1 = GC.CollectionCount(1) - gc1Before;
        int gc2 = GC.CollectionCount(2) - gc2Before;

        int received =
            scenario.Relay.GetDivisionReports(
                scenario.FrenchDivision).Count;

        if (received != perSide)
            throw new InvalidOperationException(
                "Division did not receive the expected reports.");

        long liveMemory = GC.GetTotalMemory(true);

        Console.WriteLine(
            "\n=== WoF C2.3b — Tactical profiling ===");

        Console.WriteLine(
            $"Companies: {perSide} per side, " +
            $"{perSide * 2} front-line total");

        Console.WriteLine(
            $"Sectors: {sectors}, " +
            $"contact candidates: {scenario.CandidateCount:N0}");

        Console.WriteLine($"Construction: {buildMs:F3} ms");

        Console.WriteLine(
            $"Memory after construction: " +
            $"{(memoryAfterBuild - beforeMemory) / 1048576.0:F2} MiB");

        Console.WriteLine(
            $"Memory after measurements: " +
            $"{(liveMemory - beforeMemory) / 1048576.0:F2} MiB");

        Console.WriteLine(
            $"Routing cold/hot: " +
            $"{coldRoutingMs:F3} / {hotRoutingMs:F3} ms");

        Console.WriteLine();

        PrintStatistics(
            "Detection", samples,
            x => x.DetectionMs,
            x => x.DetectionBytes);

        PrintStatistics(
            "Publication", samples,
            x => x.PublicationMs,
            x => x.PublicationBytes);

        PrintStatistics(
            "Delivery", samples,
            x => x.DeliveryMs,
            x => x.DeliveryBytes);

        Console.WriteLine(
            $"\nRouting searches/cache hits: " +
            $"{scenario.Router.SearchCount}/" +
            $"{scenario.Router.CacheHitCount}");

        Console.WriteLine(
            $"Reports per pass: {samples[0].PublishedReports}");

        Console.WriteLine(
            $"Delivered division reports: {received}");

        Console.WriteLine(
            $"Pending messages: {scenario.Relay.PendingMessageCount}");

        Console.WriteLine($"GC Gen0/1/2: {gc0}/{gc1}/{gc2}");

        // Vérifie aussi la réaction du cache à une
        // perte physique, hors chronométrage.
        var first = scenario.FrenchUnits[0].Company;
        first.Composition!.Radios.ApplyLoss(1);

        var fallback = scenario.Router.FindRoute(
            scenario.FrenchNodes[0],
            scenario.DivisionNode);

        if (fallback?.TotalMinutes != 50)
            throw new InvalidOperationException(
                "Radio loss did not select courier fallback.");

        first.Composition.Radios.Reinforce(1);

        if (scenario.Router.FindRoute(
                scenario.FrenchNodes[0],
                scenario.DivisionNode)?.TotalMinutes != 15)
        {
            throw new InvalidOperationException(
                "Radio restoration did not restore the route.");
        }

        string folder = Path.Combine(
            "benchmark-results", "tactical");

        Directory.CreateDirectory(folder);

        string name =
            $"C2_3b_c{perSide}_s{sectors}_" +
            DateTime.UtcNow.ToString(
                "yyyyMMdd_HHmmss_fff",
                CultureInfo.InvariantCulture);

        string csvPath = Path.Combine(folder, name + ".csv");
        string jsonPath = Path.Combine(folder, name + ".json");

        using (var writer = new StreamWriter(csvPath))
        {
            writer.WriteLine(
                "Sample,DetectionMs,DetectionBytes," +
                "PublicationMs,PublicationBytes," +
                "DeliveryMs,DeliveryBytes,PublishedReports");

            for (int i = 0; i < samples.Length; i++)
            {
                var row = samples[i];

            writer.WriteLine(string.Join(",",
                (i + 1).ToString(CultureInfo.InvariantCulture),
                row.DetectionMs.ToString("F6", CultureInfo.InvariantCulture),
                row.DetectionBytes.ToString(CultureInfo.InvariantCulture),
                row.PublicationMs.ToString("F6", CultureInfo.InvariantCulture),
                row.PublicationBytes.ToString(CultureInfo.InvariantCulture),
                row.DeliveryMs.ToString("F6", CultureInfo.InvariantCulture),
                row.DeliveryBytes.ToString(CultureInfo.InvariantCulture),
                row.PublishedReports.ToString(CultureInfo.InvariantCulture)));
            }
        }

        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(
                new
                {
                    Version = "C2.3b",
                    CompaniesPerSide = perSide,
                    Sectors = sectors,
                    CandidateCount = scenario.CandidateCount,
                    Warmup = warmup,
                    MeasuredPasses = repetitions,
                    Runtime = RuntimeInformation.FrameworkDescription,
                    OS = RuntimeInformation.OSDescription,
                    ServerGC = GCSettings.IsServerGC,
                    LogicalProcessors = Environment.ProcessorCount,
                    BuildMilliseconds = buildMs,
                    ColdRoutingMilliseconds = coldRoutingMs,
                    HotRoutingMilliseconds = hotRoutingMs,
                    Searches = scenario.Router.SearchCount,
                    CacheHits = scenario.Router.CacheHitCount,
                    Gc0 = gc0,
                    Gc1 = gc1,
                    Gc2 = gc2,
                    Samples = samples
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        Console.WriteLine($"\nCSV: {csvPath}");
        Console.WriteLine($"JSON: {jsonPath}");

        GC.KeepAlive(scenario);
    }
}
