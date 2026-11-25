using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.Combat;

public sealed class ProvinceBattlefieldGridCacheTests
{
    private const ulong WorldSeed = 20261125;

    /// <summary>A à l'ouest de B, une route entre elles.</summary>
    private static (Province A, Province B, ProvinceBattlefieldGridCache Cache) Setup()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B", TerrainType.Forest);
        a.ConnectTo(b);
        var catalog = new ProvinceTerrainCatalog(WorldSeed);
        catalog.SetBorders(a, new[] { new ProvinceNeighborBorder(b.Id, 90, 1) });
        catalog.SetBorders(b, new[] { new ProvinceNeighborBorder(a.Id, 270, 1) });
        return (a, b, new ProvinceBattlefieldGridCache(catalog));
    }

    [Fact]
    public void EngagementsInTheSameProvinceShareOneGrid()
    {
        var (a, _, cache) = Setup();

        var first = cache.GetOrGenerate(new Engagement(1, 0, a));
        var second = cache.GetOrGenerate(new Engagement(2, 5, a));

        Assert.Same(first, second);
        Assert.Same(a, first.Province);
        Assert.Same(first.Grid, first.Partition.Grid);
        Assert.Equal(a.Id, first.Grid.Descriptor.ProvinceId);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void MeetingAtSixtyPercentUsesTheGridOfB()
    {
        var (a, b, cache) = Setup();

        var grid = cache.GetOrGenerate(new Engagement(1, 0, a, b, 0.6));

        Assert.Same(b, grid.Province);
        Assert.Equal(b.Id, grid.Grid.Descriptor.ProvinceId);
        Assert.Equal(TerrainType.Forest, grid.Grid.Descriptor.DominantTerrain);
        Assert.Same(grid, cache.GetOrGenerate(b));
        Assert.False(cache.Contains(a));
    }

    [Fact]
    public void ReleasedGridIsRegeneratedIdentically()
    {
        var (a, _, cache) = Setup();
        var first = cache.GetOrGenerate(a);

        Assert.True(cache.Release(a));
        Assert.False(cache.Release(a));
        Assert.Equal(0, cache.Count);

        var second = cache.GetOrGenerate(a);

        Assert.NotSame(first, second);
        Assert.True(first.Grid.ElevationData.SequenceEqual(second.Grid.ElevationData));
        Assert.True(first.Grid.TerrainData.SequenceEqual(second.Grid.TerrainData));
        Assert.True(first.Grid.FeatureData.SequenceEqual(second.Grid.FeatureData));
        Assert.True(first.Grid.RiverEdgeData.SequenceEqual(second.Grid.RiverEdgeData));
        Assert.True(first.Grid.BridgeEdgeData.SequenceEqual(second.Grid.BridgeEdgeData));
        Assert.Equal(first.Grid.BorderCrossings, second.Grid.BorderCrossings);
        Assert.Equal(
            first.Partition.Sectors.Select(s => s.Cells),
            second.Partition.Sectors.Select(s => s.Cells));
    }

    [Fact]
    public void ProvincesWithTheSameIdStayDistinct()
    {
        // Deux provinces homonymes, sans voisine, de terrains différents.
        var plains = new Province(7, "P");
        var mountain = new Province(7, "P", TerrainType.Mountain);
        var catalog = new ProvinceTerrainCatalog(WorldSeed);
        catalog.SetBorders(plains, Array.Empty<ProvinceNeighborBorder>());
        catalog.SetBorders(mountain, Array.Empty<ProvinceNeighborBorder>());
        var cache = new ProvinceBattlefieldGridCache(catalog);

        var first = cache.GetOrGenerate(plains);
        var second = cache.GetOrGenerate(mountain);

        Assert.NotSame(first.Grid, second.Grid);
        Assert.Equal(TerrainType.Plains, first.Grid.Descriptor.DominantTerrain);
        Assert.Equal(TerrainType.Mountain, second.Grid.Descriptor.DominantTerrain);
        Assert.Equal(2, cache.Count);

        Assert.True(cache.Release(plains));
        Assert.True(cache.Contains(mountain));
        Assert.Same(second, cache.GetOrGenerate(mountain));
    }

    [Fact]
    public void InvalidBordersPreventGenerationAndLeaveNothingInTheCache()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");
        a.ConnectTo(b);
        var catalog = new ProvinceTerrainCatalog(WorldSeed);
        catalog.SetBorders(a, new[] { new ProvinceNeighborBorder(b.Id, 90, 1) });
        catalog.SetBorders(b, new[] { new ProvinceNeighborBorder(a.Id, 180, 1) });
        var cache = new ProvinceBattlefieldGridCache(catalog);

        Assert.Throws<InvalidOperationException>(() => cache.GetOrGenerate(a));
        Assert.Equal(0, cache.Count);
    }
}