
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalRouteFinderTests
{
    private static (
        TacticalBattlefield Field,
        BattleSector A,
        BattleSector B,
        BattleSector C,
        BattleSector D) CreateGraph()
    {
        var field = new TacticalBattlefield();

        var a = new BattleSector(1, "A", TerrainType.Plains);
        var b = new BattleSector(2, "B", TerrainType.Plains);
        var c = new BattleSector(3, "C", TerrainType.Plains);
        var d = new BattleSector(4, "D", TerrainType.Plains);

        field.AddSector(a);
        field.AddSector(b);
        field.AddSector(c);
        field.AddSector(d);

        field.Connect(a, b, 1000);
        field.Connect(b, d, 5000);

        field.Connect(a, c, 1500);
        field.Connect(c, d, 2000);

        return (field, a, b, c, d);
    }

    [Fact]
    public void ChoosesShortestDistanceNotFewestConnections()
    {
        var g = CreateGraph();

        var route = TacticalRouteFinder.Find(
            g.Field, g.A, g.D, _ => true);

        Assert.Equal(3500, route.TotalDistanceMeters);
        Assert.Equal(
            new[] { g.A, g.C, g.D },
            route.Sectors);
    }

    [Fact]
    public void AvoidsForbiddenIntermediateSector()
    {
        var g = CreateGraph();

        var route = TacticalRouteFinder.Find(
            g.Field, g.A, g.D,
            sector => !ReferenceEquals(sector, g.C));

        Assert.Equal(6000, route.TotalDistanceMeters);
        Assert.Equal(
            new[] { g.A, g.B, g.D },
            route.Sectors);
    }

    [Fact]
    public void RejectsUnreachableDestination()
    {
        var g = CreateGraph();

        Assert.Throws<InvalidOperationException>(() =>
            TacticalRouteFinder.Find(
                g.Field,
                g.A,
                g.D,
                sector =>
                    !ReferenceEquals(sector, g.B)
                    && !ReferenceEquals(sector, g.C)));
    }

    [Fact]
    public void RejectsForeignSectorsAndIdenticalEndpoints()
    {
        var g = CreateGraph();

        var foreign = new BattleSector(
            9, "Foreign", TerrainType.Plains);

        Assert.Throws<InvalidOperationException>(() =>
            TacticalRouteFinder.Find(
                g.Field, g.A, foreign, _ => true));

        Assert.Throws<InvalidOperationException>(() =>
            TacticalRouteFinder.Find(
                g.Field, g.A, g.A, _ => true));
    }
}
