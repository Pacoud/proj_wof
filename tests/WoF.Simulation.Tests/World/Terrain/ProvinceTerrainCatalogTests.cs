using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.World.Terrain;

public sealed class ProvinceTerrainCatalogTests
{
    private const ulong WorldSeed = 20261125;

    private static (Province A, Province B) Pair()
    {
        var a = new Province(1, "A", TerrainType.Hills);
        var b = new Province(2, "B");
        a.ConnectTo(b);
        return (a, b);
    }

    [Fact]
    public void DescriptorCarriesSeedTerrainAndReciprocalBorders()
    {
        var (a, b) = Pair();
        var catalog = new ProvinceTerrainCatalog(WorldSeed);
        catalog.SetBorders(a, new[] { new ProvinceNeighborBorder(b.Id, 90, 2) });
        catalog.SetBorders(b, new[] { new ProvinceNeighborBorder(a.Id, 270, 2) });

        var descriptor = catalog.GetDescriptor(a);

        Assert.Equal(a.Id, descriptor.ProvinceId);
        Assert.Equal(WorldSeed, descriptor.WorldSeed);
        Assert.Equal(TerrainType.Hills, descriptor.DominantTerrain);
        var border = Assert.Single(descriptor.Neighbors);
        Assert.Equal(new ProvinceNeighborBorder(b.Id, 90, 2), border);
    }

    [Fact]
    public void MissingBordersAreRefused()
    {
        var (a, b) = Pair();
        var catalog = new ProvinceTerrainCatalog(WorldSeed);

        // A n'est pas enregistrée.
        Assert.Throws<InvalidOperationException>(() => catalog.GetDescriptor(a));

        // A est enregistrée sans frontière avec sa voisine réelle B.
        catalog.SetBorders(a, Array.Empty<ProvinceNeighborBorder>());
        Assert.Throws<InvalidOperationException>(() => catalog.GetDescriptor(a));

        // B décrit sa frontière, mais l'autre côté (A) ne la décrit pas.
        catalog.SetBorders(b, new[] { new ProvinceNeighborBorder(a.Id, 270, 1) });
        Assert.Throws<InvalidOperationException>(() => catalog.GetDescriptor(b));
    }

    [Fact]
    public void BorderWithAProvinceThatIsNotANeighborIsRefused()
    {
        var a = new Province(1, "A");
        var catalog = new ProvinceTerrainCatalog(WorldSeed);
        catalog.SetBorders(a, new[] { new ProvinceNeighborBorder(9, 90, 1) });

        Assert.Throws<InvalidOperationException>(() => catalog.GetDescriptor(a));
    }

    [Fact]
    public void DifferentRoadLevelsOnEachSideAreRefused()
    {
        var (a, b) = Pair();
        var catalog = new ProvinceTerrainCatalog(WorldSeed);
        catalog.SetBorders(a, new[] { new ProvinceNeighborBorder(b.Id, 90, 2) });
        catalog.SetBorders(b, new[] { new ProvinceNeighborBorder(a.Id, 270, 1) });

        Assert.Throws<InvalidOperationException>(() => catalog.GetDescriptor(a));
        Assert.Throws<InvalidOperationException>(() => catalog.GetDescriptor(b));
    }

    [Theory]
    [InlineData(90, 270, true)]
    [InlineData(90, 271, true)]        // Tolérance incluse.
    [InlineData(359.6, 180, true)]     // Passage par 360°.
    [InlineData(0.5, 180.9, true)]     // Passage par 0°.
    [InlineData(90, 271.5, false)]
    [InlineData(90, 90, false)]
    [InlineData(0, 90, false)]
    public void BearingsMustBeOppositeOnEachSide(double fromA, double fromB, bool accepted)
    {
        var (a, b) = Pair();
        var catalog = new ProvinceTerrainCatalog(WorldSeed);
        catalog.SetBorders(a, new[] { new ProvinceNeighborBorder(b.Id, fromA, 1) });
        catalog.SetBorders(b, new[] { new ProvinceNeighborBorder(a.Id, fromB, 1) });

        Assert.Equal(accepted, ProvinceTerrainCatalog.AreOpposite(fromA, fromB));

        if (accepted)
        {
            Assert.Equal(a.Id, catalog.GetDescriptor(a).ProvinceId);
            Assert.Equal(b.Id, catalog.GetDescriptor(b).ProvinceId);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => catalog.GetDescriptor(a));
            Assert.Throws<InvalidOperationException>(() => catalog.GetDescriptor(b));
        }
    }

    [Fact]
    public void BordersCannotBeRedefined()
    {
        var a = new Province(1, "A");
        var catalog = new ProvinceTerrainCatalog(WorldSeed);
        catalog.SetBorders(a, Array.Empty<ProvinceNeighborBorder>());

        Assert.Throws<InvalidOperationException>(() =>
            catalog.SetBorders(a, Array.Empty<ProvinceNeighborBorder>()));
    }
}