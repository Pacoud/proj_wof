using System.Diagnostics;
using System.Globalization;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Benchmarks;

/// <summary>
/// Mesure la génération d'une province entière, pour chaque type de terrain,
/// puis la construction d'un champ de bataille d'environ 2 000 cases.
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

        return 0;
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
