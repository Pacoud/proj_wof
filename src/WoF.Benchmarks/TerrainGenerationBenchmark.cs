using System.Diagnostics;
using System.Globalization;
using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Benchmarks;

/// <summary>
/// Mesure la génération d'une province entière, pour chaque type de terrain,
/// puis la construction d'un champ de bataille d'environ 2 000 cases et
/// celle de la province entière, un itinéraire d'un bord à l'autre,
/// la lecture de l'altitude du sol et la ligne de vue sur le relief.
/// Affiche aussi les compteurs fonctionnels, pour comparer deux versions.
///
/// Usage : terrain [répétitions=20] [échauffement=3] [graineDuMonde=20261122]
/// </summary>
public static class TerrainGenerationBenchmark
{
    private const int TargetBattlefieldCells = 2000;

    public static int Run(string[] args)
    {
        int repetitions = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 20;
        int warmup = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
        ulong worldSeed = args.Length > 2 ? ulong.Parse(args[2], CultureInfo.InvariantCulture) : 20261122UL;

        if (repetitions <= 0 || warmup < 0)
        {
            Console.WriteLine("Usage: terrain [repetitions] [warmup] [worldSeed]");
            return 1;
        }

        Console.WriteLine(
            $"Terrain benchmark | repetitions {repetitions} | warmup {warmup} | worldSeed {worldSeed}");
        Console.WriteLine();
        Console.WriteLine(
            "Type      | median ms | min ms | max ms | alloc KB | woods % | marsh % | hills % | mount. % | urban cells | villages | towns | cities | river edges | bridges | road cells | crossings | GS full/partial");

        foreach (var terrain in Enum.GetValues<TerrainType>())
        {
            var descriptor = CreateDescriptor(terrain, worldSeed);

            for (int i = 0; i < warmup; i++)
                ProvinceTerrainGenerator.Generate(descriptor);

            var times = new double[repetitions];
            long bytes = 0;
            ProvinceHexGrid grid = null!;

            for (int i = 0; i < repetitions; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();

                grid = ProvinceTerrainGenerator.Generate(descriptor);

                watch.Stop();
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                times[i] = watch.Elapsed.TotalMilliseconds;
            }

            var partition = GrandSectorPartition.Build(grid);
            Array.Sort(times);

            Console.WriteLine(string.Join(" | ",
                terrain.ToString().PadRight(9),
                Format(times[times.Length / 2]),
                Format(times[0]),
                Format(times[^1]),
                (bytes / repetitions / 1024).ToString(CultureInfo.InvariantCulture),
                Percent(grid, c => (grid.GetFeatures(c) & TerrainCellFeature.Woods) != 0),
                Percent(grid, c => grid.GetTerrain(c) == TerrainType.Marsh),
                Percent(grid, c => grid.GetTerrain(c) == TerrainType.Hills),
                Percent(grid, c => grid.GetTerrain(c) == TerrainType.Mountain),
                Count(grid, c => grid.GetTerrain(c) == TerrainType.Urban),
                grid.Localities.Count(l => l.Kind == TerrainLocalityKind.Village),
                grid.Localities.Count(l => l.Kind == TerrainLocalityKind.Town),
                grid.Localities.Count(l => l.Kind == TerrainLocalityKind.City),
                CountEdges(grid, (c, d) => grid.IsRiverEdge(c, d)),
                CountEdges(grid, (c, d) => grid.HasBridge(c, d)),
                Count(grid, c => (grid.GetFeatures(c) & TerrainCellFeature.Road) != 0),
                grid.BorderCrossings.Count,
                $"{partition.Sectors.Count(s => !s.IsPartial)}/{partition.Sectors.Count(s => s.IsPartial)}"));
        }

        Console.WriteLine();
        MeasureBuild(worldSeed, repetitions, warmup);
        MeasureWholeProvince(worldSeed, repetitions, warmup);
        MeasureGround(worldSeed, repetitions, warmup);
        MeasureSight(TerrainType.Plains, worldSeed, repetitions, warmup);
        MeasureSight(TerrainType.Hills, worldSeed, repetitions, warmup);

        return 0;
    }

    /// <summary>
    /// Tous les grands secteurs : un secteur par case. Puis le plus court
    /// itinéraire entre les deux bords, au milieu du cadre.
    /// </summary>
    private static void MeasureWholeProvince(ulong worldSeed, int repetitions, int warmup)
    {
        var grid = ProvinceTerrainGenerator.Generate(
            CreateDescriptor(TerrainType.Plains, worldSeed));

        var partition = GrandSectorPartition.Build(grid);
        var all = partition.Sectors.Select(s => s.Id).ToArray();

        ProvinceBattlefieldLayout layout = null!;

        var build = Measure(repetitions, warmup, () =>
            layout = TacticalBattlefieldBuilder.Build(grid, partition, all));

        Console.WriteLine(
            $"Province build | GS {all.Length} | cells {layout.CellIndices.Count} " +
            $"| connections {layout.Battlefield.Connections.Count} | {build}");

        int row = grid.Rows / 2;
        var origin = layout.GetSector(row * grid.Columns);
        var destination = layout.GetSector(row * grid.Columns + grid.Columns - 1);

        TacticalSectorRoute route = null!;

        var find = Measure(repetitions, warmup, () =>
            route = TacticalRouteFinder.Find(layout.Battlefield, origin, destination, _ => true));

        Console.WriteLine(
            $"Route | hops {route.Sectors.Count - 1} | distance {route.TotalDistanceMeters:0} m | {find}");
    }

    /// <summary>Altitude du sol en 200 000 points pseudo-aléatoires du cadre.</summary>
    private static void MeasureGround(ulong worldSeed, int repetitions, int warmup)
    {
        const int Queries = 200_000;

        var grid = ProvinceTerrainGenerator.Generate(
            CreateDescriptor(TerrainType.Hills, worldSeed));

        double width = grid.Columns * ProvinceHexGrid.SpacingMeters;
        double height = grid.Rows * ProvinceHexGrid.SpacingMeters * HexCoord.RowSpacingFactor;

        var xs = new double[Queries];
        var ys = new double[Queries];
        ulong state = 0x9E3779B97F4A7C15UL;

        for (int i = 0; i < Queries; i++)
        {
            xs[i] = NextUnit(ref state) * width;
            ys[i] = NextUnit(ref state) * height;
        }

        double sum = 0;
        int found = 0;

        var timing = Measure(repetitions, warmup, () =>
        {
            sum = 0;
            found = 0;

            for (int i = 0; i < Queries; i++)
            {
                if (grid.TryGetGroundElevation(xs[i], ys[i], out double elevation))
                {
                    sum += elevation;
                    found++;
                }
            }
        });

        Console.WriteLine(
            $"Ground | queries {Queries} | found {found} | mean {sum / found:0.000} m | {timing}");
    }

    /// <summary>
    /// 40 compagnies par camp, au centre de cases tirées dans une fenêtre
    /// de 40 × 40 cases au milieu de la province ; lignes de vue de chaque
    /// paire ennemie à 1 500 m au plus.
    /// </summary>
    private static void MeasureSight(TerrainType terrain, ulong worldSeed, int repetitions, int warmup)
    {
        const int CompaniesPerSide = 40;
        const int Window = 40;
        const double MaxRangeMeters = 1500;

        var grid = ProvinceTerrainGenerator.Generate(CreateDescriptor(terrain, worldSeed));
        var partition = GrandSectorPartition.Build(grid);
        var layout = TacticalBattlefieldBuilder.Build(
            grid, partition, partition.Sectors.Select(s => s.Id));

        var engine = new SimulationEngine();
        var west = new Country(1, "West");
        var east = new Country(2, "East");
        var province = new Province(10, "Benchmark", terrain);

        var westDivision = CreateDivision(west, province, CompaniesPerSide);
        var eastDivision = CreateDivision(east, province, CompaniesPerSide);

        engine.AddDivision(westDivision);
        engine.AddDivision(eastDivision);
        engine.DeclareWar(west, east);
        engine.Tick();

        var battle = new TacticalBattle(engine.Engagements.ActiveEngagements.Single(), layout.Battlefield);
        var map = new TacticalGeometryMap(battle);
        layout.DefineGeometry(map);

        int firstColumn = (grid.Columns - Window) / 2;
        int firstRow = (grid.Rows - Window) / 2;
        ulong state = 0xD1B54A32D192ED03UL;

        CompanyDeployment[] Deploy(Division division, double facing)
        {
            return division.Battalions[0].Companies.Select(company =>
            {
                int column = firstColumn + (int)(NextUnit(ref state) * Window);
                int row = firstRow + (int)(NextUnit(ref state) * Window);
                var sector = layout.GetSector(row * grid.Columns + column);

                var deployment = battle.DeployCompany(company, sector, CompanyDeploymentRole.FrontLine);
                map.PlaceCompany(deployment, layout.GetCellCenter(sector), facing, 20);

                return deployment;
            }).ToArray();
        }

        var westCompanies = Deploy(westDivision, 90);
        var eastCompanies = Deploy(eastDivision, 270);

        var pairs = new List<(CompanyDeployment Observer, CompanyDeployment Target)>();

        foreach (var observer in westCompanies)
        {
            var a = map.GetPosition(observer)!.Point;

            foreach (var target in eastCompanies)
            {
                var b = map.GetPosition(target)!.Point;
                double dx = b.XMeters - a.XMeters;
                double dy = b.YMeters - a.YMeters;

                if (Math.Sqrt(dx * dx + dy * dy) <= MaxRangeMeters)
                    pairs.Add((observer, target));
            }
        }

        int clear = 0;
        int terrainBlocked = 0;

        var timing = Measure(repetitions, warmup, () =>
        {
            clear = 0;
            terrainBlocked = 0;

            foreach (var (observer, target) in pairs)
            {
                var status = TacticalSightEvaluator.Evaluate(map, observer, target).Status;

                if (status == TacticalSightStatus.ClearOfKnownObstacles)
                    clear++;
                else if (status == TacticalSightStatus.BlockedByTerrain)
                    terrainBlocked++;
            }
        });

        Console.WriteLine(
            $"Sight {terrain} | pairs {pairs.Count} (<= {MaxRangeMeters:0} m) | clear {clear} " +
            $"| blocked by terrain {terrainBlocked} | {timing}");
    }

    private static Division CreateDivision(Country country, Province province, int companies)
    {
        var division = new Division(
            $"{country.Name} Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: companies * 100,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(1, "Battalion", BattalionType.Infantry);

        for (int i = 1; i <= companies; i++)
        {
            battalion.AddCompany(new Company(
                i, $"Company {i}", CompanyType.Infantry, new CompanyComposition(manpower: 100)));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    /// <summary>
    /// Échauffement puis répétitions chronométrées ; temps médian, minimal,
    /// maximal et allocations moyennes par répétition.
    /// </summary>
    private static string Measure(int repetitions, int warmup, Action action)
    {
        for (int i = 0; i < warmup; i++)
            action();

        var times = new double[repetitions];
        long bytes = 0;

        for (int i = 0; i < repetitions; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();

            action();

            watch.Stop();
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            times[i] = watch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);

        return $"median {Format(times[times.Length / 2])} ms | min {Format(times[0])} ms " +
               $"| max {Format(times[^1])} ms | alloc {bytes / repetitions / 1024} KB";
    }

    /// <summary>Générateur pseudo-aléatoire fixe (SplitMix64), dans [0, 1).</summary>
    private static double NextUnit(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;

        return (z >> 11) * (1.0 / (1UL << 53));
    }

    private static void MeasureBuild(ulong worldSeed, int repetitions, int warmup)
    {
        var grid = ProvinceTerrainGenerator.Generate(
            CreateDescriptor(TerrainType.Plains, worldSeed));

        var partition = GrandSectorPartition.Build(grid);
        var requested = ChooseRequestedSectors(partition);

        for (int i = 0; i < warmup; i++)
            TacticalBattlefieldBuilder.Build(grid, partition, requested);

        var times = new double[repetitions];
        long bytes = 0;
        ProvinceBattlefieldLayout layout = null!;

        for (int i = 0; i < repetitions; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();

            layout = TacticalBattlefieldBuilder.Build(grid, partition, requested);

            watch.Stop();
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            times[i] = watch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);

        Console.WriteLine(
            $"Build | requested GS {requested.Length} | included GS {layout.IncludedGrandSectorIds.Count} " +
            $"| cells {layout.CellIndices.Count} | connections {layout.Battlefield.Connections.Count} " +
            $"| median {Format(times[times.Length / 2])} ms | min {Format(times[0])} ms " +
            $"| max {Format(times[^1])} ms | alloc {bytes / repetitions / 1024} KB");
    }

    /// <summary>
    /// Grands secteurs complets les plus proches du centre, ajoutés jusqu'à ce que
    /// les secteurs inclus (avec leurs voisins) atteignent environ 2 000 cases.
    /// </summary>
    internal static int[] ChooseRequestedSectors(GrandSectorPartition partition)
    {
        var center = partition.GetSectorOfCell(
            (partition.Grid.Rows / 2) * partition.Grid.Columns
            + partition.Grid.Columns / 2).Center;

        var ordered = partition.Sectors
            .Where(s => !s.IsPartial)
            .OrderBy(s => s.Center.DistanceTo(center))
            .ThenBy(s => s.Id)
            .ToArray();

        var requested = new List<int>();
        var included = new HashSet<int>();

        foreach (var sector in ordered)
        {
            requested.Add(sector.Id);
            included.Add(sector.Id);
            included.UnionWith(sector.NeighborIds);

            int cells = included.Sum(id => partition.GetSector(id).Cells.Count);

            if (cells >= TargetBattlefieldCells)
                break;
        }

        return requested.ToArray();
    }

    internal static ProvinceTerrainDescriptor CreateDescriptor(TerrainType terrain, ulong worldSeed)
    {
        var neighbors = Enumerable.Range(0, 6)
            .Select(i => new ProvinceNeighborBorder(
                neighborProvinceId: 11 + i,
                bearingDegrees: 60.0 * i,
                roadLevel: 1 + i % 2));

        return new ProvinceTerrainDescriptor(10, worldSeed, terrain, neighbors);
    }

    private static string Format(double ms) =>
        ms.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Percent(ProvinceHexGrid grid, Func<int, bool> predicate) =>
        (100.0 * Count(grid, predicate) / grid.CellCount).ToString("0.0", CultureInfo.InvariantCulture);

    private static int Count(ProvinceHexGrid grid, Func<int, bool> predicate)
    {
        int count = 0;

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            if (predicate(cell))
                count++;
        }

        return count;
    }

    /// <summary>Bords comptés une fois (directions 0 à 2 depuis chaque case).</summary>
    private static int CountEdges(ProvinceHexGrid grid, Func<int, int, bool> predicate)
    {
        int count = 0;

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            for (int d = 0; d < 3; d++)
            {
                if (predicate(cell, d))
                    count++;
            }
        }

        return count;
    }
}
