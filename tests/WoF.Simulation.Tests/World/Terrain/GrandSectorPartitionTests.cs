using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.World.Terrain;

public sealed class GrandSectorPartitionTests
{
    private static GrandSectorPartition CreatePartition()
    {
        var grid = ProvinceTerrainGenerator.Generate(
            ProvinceTerrainGeneratorTests.Descriptor());

        return GrandSectorPartition.Build(grid);
    }

    [Fact]
    public void EveryCell_BelongsToExactlyOneGrandSector()
    {
        var partition = CreatePartition();
        var grid = partition.Grid;
        var seen = new int[grid.CellCount];

        foreach (var sector in partition.Sectors)
        {
            foreach (int cell in sector.Cells)
            {
                seen[cell]++;
                Assert.Same(sector, partition.GetSectorOfCell(cell));
            }
        }

        Assert.All(seen, count => Assert.Equal(1, count));

        Assert.Equal(
            Enumerable.Range(1, partition.Sectors.Count),
            partition.Sectors.Select(s => s.Id));
    }

    [Fact]
    public void FullGrandSectors_AreRadiusThreeHexagons()
    {
        var partition = CreatePartition();
        var grid = partition.Grid;

        var full = partition.Sectors.Where(s => !s.IsPartial).ToArray();

        // Environ 490 grands secteurs par province, plus des secteurs partiels au bord.
        Assert.InRange(full.Length, 430, 500);
        Assert.InRange(partition.Sectors.Count, 480, 560);

        foreach (var sector in partition.Sectors)
        {
            Assert.InRange(sector.Cells.Count, 1, GrandSector.FullCellCount);

            Assert.All(sector.Cells, c =>
                Assert.True(grid.GetCoord(c).DistanceTo(sector.Center) <= GrandSector.Radius));
        }

        Assert.All(full, s => Assert.Equal(37, s.Cells.Count));
    }

    [Fact]
    public void Neighbours_AreSymmetricAndInteriorSectorsHaveSix()
    {
        var partition = CreatePartition();
        var grid = partition.Grid;

        foreach (var sector in partition.Sectors)
        {
            foreach (int neighborId in sector.NeighborIds)
                Assert.Contains(sector.Id, partition.GetSector(neighborId).NeighborIds);

            bool interior = !sector.IsPartial
                && sector.NeighborIds.All(id => !partition.GetSector(id).IsPartial);

            if (interior)
                Assert.Equal(6, sector.NeighborIds.Count);
        }
    }

    [Fact]
    public void Summary_ReflectsItsCells()
    {
        var partition = CreatePartition();
        var grid = partition.Grid;

        foreach (var sector in partition.Sectors)
        {
            var union = sector.Cells.Aggregate(
                TerrainCellFeature.None, (f, c) => f | grid.GetFeatures(c));

            Assert.Equal(union, sector.Features);
            Assert.Equal(sector.Cells.Any(grid.HasAnyRiverEdge), sector.HasRiver);
            Assert.Equal(sector.Cells.Average(c => (double)grid.GetElevation(c)),
                sector.MeanElevationMeters, 6);

            int dominantCount = sector.Cells.Count(c => grid.GetTerrain(c) == sector.DominantTerrain);

            Assert.All(Enum.GetValues<WoF.Simulation.World.TerrainType>(), t =>
                Assert.True(sector.Cells.Count(c => grid.GetTerrain(c) == t) <= dominantCount));
        }
    }

    [Fact]
    public void BorderSectors_ContainTheNeighbourBorderCells()
    {
        var partition = CreatePartition();
        var grid = partition.Grid;

        foreach (var neighbor in grid.Descriptor.Neighbors)
        {
            var sectors = partition.GetBorderSectors(neighbor.NeighborProvinceId);
            var borderCells = grid.GetBorderCells(neighbor.NeighborProvinceId);

            Assert.NotEmpty(sectors);

            Assert.All(borderCells, c =>
                Assert.Contains(partition.GetSectorOfCell(c), sectors));

            Assert.All(sectors, s =>
                Assert.Contains(s.Cells, c => borderCells.Contains(c)));
        }
    }
}
