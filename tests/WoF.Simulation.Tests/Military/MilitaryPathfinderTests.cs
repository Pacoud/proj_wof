using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Tests.Military;

public class MilitaryPathfinderTests
{
    [Fact]
    public void PathfinderChoosesFastestPathRatherThanFewestProvinces()
    {
        var a = new Province(
            1,
            "A",
            TerrainType.Plains
        );

        var b = new Province(
            2,
            "B",
            TerrainType.Mountain
        );

        var c = new Province(
            3,
            "C",
            TerrainType.Plains
        );

        var e = new Province(
            4,
            "E",
            TerrainType.Plains
        );

        var d = new Province(
            5,
            "D",
            TerrainType.Plains
        );

        // Chemin court :
        // A -> B -> D
        a.ConnectTo(b);
        b.ConnectTo(d);

        // Chemin plus long mais rapide :
        // A -> C -> E -> D
        a.ConnectTo(c);
        c.ConnectTo(e);
        e.ConnectTo(d);

        var links = new[]
        {
            new InfrastructureLink(
                a, b,
                InfrastructureType.Road,
                1),

            new InfrastructureLink(
                b, d,
                InfrastructureType.Road,
                1),

            new InfrastructureLink(
                a, c,
                InfrastructureType.Road,
                3),

            new InfrastructureLink(
                c, e,
                InfrastructureType.Road,
                3),

            new InfrastructureLink(
                e, d,
                InfrastructureType.Road,
                3)
        };

        var path =
            MilitaryPathfinder.FindFastestPath(
                a,
                d,
                links,
                DivisionType.Infantry
            );

        Assert.NotNull(path);

        Assert.Equal(
            new[] { a, c, e, d },
            path
        );
    }


    [Fact]
    public void AutomaticRouteQueuesIntermediateProvinces()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");
        var c = new Province(3, "C");

        a.ConnectTo(b);
        b.ConnectTo(c);

        var roadAB =
            new InfrastructureLink(
                a, b,
                InfrastructureType.Road,
                3
            );

        var roadBC =
            new InfrastructureLink(
                b, c,
                InfrastructureType.Road,
                3
            );

        var division =
            new Division(
                "1re Division",
                a,
                100,
                100
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(roadAB);
        simulation.AddInfrastructureLink(roadBC);

        bool accepted =
            simulation.TryOrderMoveTo(
                division,
                c
            );

        Assert.True(accepted);

        // A -> B a commencé.
        Assert.Equal(
            a,
            division.Transit!.Origin
        );

        Assert.Equal(
            b,
            division.Transit.Destination
        );

        // C reste dans la file.
        Assert.Equal(
            c,
            division.NextQueuedDestination
        );
    }


    [Fact]
    public void ExplicitRouteIsRespected()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");
        var c = new Province(3, "C");
        var d = new Province(4, "D");

        a.ConnectTo(b);
        b.ConnectTo(d);

        a.ConnectTo(c);
        c.ConnectTo(d);

        var division =
            new Division(
                "1re Division",
                a,
                100,
                100
            );

        var simulation =
            new SimulationEngine();

        bool accepted =
            simulation.TryOrderExplicitPath(
                division,
                new[] { c, d }
            );

        Assert.True(accepted);

        Assert.Equal(
            c,
            division.Transit!.Destination
        );

        Assert.Equal(
            d,
            division.NextQueuedDestination
        );
    }
}
