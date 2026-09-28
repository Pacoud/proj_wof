using WoF.Simulation.Logistics;
using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Tests.Logistics;

public class SupplyNetworkTests
{
    [Fact]
    public void Route_AllowsFuelToReachNeighbouringProvince()
    {
        var provinceA = new Province(1, "Province A");
        var provinceB = new Province(2, "Province B");

        provinceA.ConnectTo(provinceB);

        var depot = new SupplyDepot(
            "Dépôt A",
            provinceA,
            fuelStock: 500,
            fuelTransferPerHour: 20
        );

        var division = new Division(
            "1re Division",
            provinceB,
            fuel: 20,
            ammunition: 100
        );

        var route = new InfrastructureLink(
            provinceA,
            provinceB,
            InfrastructureType.Road,
            level: 2
        );

        var network = new SupplyNetwork();

        network.AddLink(route);

        network.ProcessFuelSupply(
            new[] { depot },
            new[] { division }
        );

        Assert.Equal(40, division.Fuel);
        Assert.Equal(480, depot.FuelStock);
    }

    [Fact]
    public void DivisionCannotReceiveFuelWithoutRoute()
    {
        var provinceA = new Province(1, "Province A");
        var provinceB = new Province(2, "Province B");

        provinceA.ConnectTo(provinceB);

        var depot = new SupplyDepot(
            "Dépôt A",
            provinceA,
            fuelStock: 500,
            fuelTransferPerHour: 20
        );

        var division = new Division(
            "1re Division",
            provinceB,
            fuel: 20,
            ammunition: 100
        );

        var network = new SupplyNetwork();

        network.ProcessFuelSupply(
            new[] { depot },
            new[] { division }
        );

        Assert.Equal(20, division.Fuel);
        Assert.Equal(500, depot.FuelStock);
    }

    [Fact]
    public void RouteCapacityIsSharedBetweenDivisions()
    {
        var provinceA = new Province(1, "Province A");
        var provinceB = new Province(2, "Province B");

        provinceA.ConnectTo(provinceB);

        var depot = new SupplyDepot(
            "Dépôt A",
            provinceA,
            fuelStock: 500,
            fuelTransferPerHour: 100
        );

        var division1 = new Division(
            "1re Division",
            provinceB,
            fuel: 20,
            ammunition: 100
        );

        var division2 = new Division(
            "2e Division",
            provinceB,
            fuel: 20,
            ammunition: 100
        );

        var route = new InfrastructureLink(
            provinceA,
            provinceB,
            InfrastructureType.Road,
            level: 2
        );

        var network = new SupplyNetwork();

        network.AddLink(route);

        network.ProcessFuelSupply(
            new[] { depot },
            new[] { division1, division2 }
        );

        double totalFuelReceived =
            (division1.Fuel - 20)
            +
            (division2.Fuel - 20);

        Assert.Equal(20, totalFuelReceived);

        Assert.Equal(480, depot.FuelStock);
    }

    [Fact]
    public void NetworkFindsPathAcrossMultipleRoutes()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");
        var provinceC = new Province(3, "C");

        provinceA.ConnectTo(provinceB);
        provinceB.ConnectTo(provinceC);

        var routeAB = new InfrastructureLink(
            provinceA,
            provinceB,
            InfrastructureType.Road,
            level: 2
        );

        var routeBC = new InfrastructureLink(
            provinceB,
            provinceC,
            InfrastructureType.Road,
            level: 1
        );

        var network = new SupplyNetwork();

        network.AddLink(routeAB);
        network.AddLink(routeBC);

        var path = network.FindPath(
            provinceA,
            provinceC
        );

        Assert.NotNull(path);

        Assert.Equal(2, path!.Links.Count);

        Assert.Equal(10, path.Capacity);
    }

    [Fact]
    public void NetworkReturnsNullWhenNoPathExists()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");
        var provinceC = new Province(3, "C");

        provinceA.ConnectTo(provinceB);

        var routeAB = new InfrastructureLink(
            provinceA,
            provinceB,
            InfrastructureType.Road,
            level: 2
        );

        var network = new SupplyNetwork();

        network.AddLink(routeAB);

        var path = network.FindPath(
            provinceA,
            provinceC
        );

        Assert.Null(path);
    }

    [Fact] //Supply qui se réparti sur plusieurs provinces 
    public void FuelCanTravelAcrossMultipleRoutes()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");
        var provinceC = new Province(3, "C");

        provinceA.ConnectTo(provinceB);
        provinceB.ConnectTo(provinceC);

        var routeAB = new InfrastructureLink(
            provinceA,
            provinceB,
            InfrastructureType.Road,
            level: 2
        );

        var routeBC = new InfrastructureLink(
            provinceB,
            provinceC,
            InfrastructureType.Road,
            level: 1
        );

        var depot = new SupplyDepot(
            "Dépôt A",
            provinceA,
            fuelStock: 500,
            fuelTransferPerHour: 100
        );

        var division = new Division(
            "1re Division",
            provinceC,
            fuel: 20,
            ammunition: 100
        );

        var network = new SupplyNetwork();

        network.AddLink(routeAB);
        network.AddLink(routeBC);

        network.ProcessFuelSupply(
            new[] { depot },
            new[] { division }
        );

        Assert.Equal(30, division.Fuel);
        Assert.Equal(490, depot.FuelStock);
    }

    [Fact]  //pathfinding, deux divisions empruntent le meme chemin/route
    public void PathCapacityIsSharedBetweenDivisions()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");
        var provinceC = new Province(3, "C");

        provinceA.ConnectTo(provinceB);
        provinceB.ConnectTo(provinceC);

        var routeAB = new InfrastructureLink(
            provinceA,
            provinceB,
            InfrastructureType.Road,
            level: 2
        );

        var routeBC = new InfrastructureLink(
            provinceB,
            provinceC,
            InfrastructureType.Road,
            level: 1
        );

        var depot = new SupplyDepot(
            "Dépôt A",
            provinceA,
            fuelStock: 500,
            fuelTransferPerHour: 100
        );

        var division1 = new Division(
            "1re Division",
            provinceC,
            fuel: 20,
            ammunition: 100
        );

        var division2 = new Division(
            "2e Division",
            provinceC,
            fuel: 20,
            ammunition: 100
        );

        var network = new SupplyNetwork();

        network.AddLink(routeAB);
        network.AddLink(routeBC);

        network.ProcessFuelSupply(
            new[] { depot },
            new[]
            {
                division1,
                division2
            }
        );

        double totalReceived =
            (division1.Fuel - 20)
            +
            (division2.Fuel - 20);

        Assert.Equal(10, totalReceived);
        Assert.Equal(490, depot.FuelStock);
    }

        [Fact]
    public void WidestPathChoosesHighestCapacityRoute()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");
        var provinceC = new Province(3, "C");
        var provinceD = new Province(4, "D");

        provinceA.ConnectTo(provinceB);
        provinceB.ConnectTo(provinceD);

        provinceA.ConnectTo(provinceC);
        provinceC.ConnectTo(provinceD);

        var routeAB =
            new InfrastructureLink(
                provinceA,
                provinceB,
                InfrastructureType.Road,
                level: 1
            );

        var routeBD =
            new InfrastructureLink(
                provinceB,
                provinceD,
                InfrastructureType.Road,
                level: 1
            );

        var routeAC =
            new InfrastructureLink(
                provinceA,
                provinceC,
                InfrastructureType.Road,
                level: 3
            );

        var routeCD =
            new InfrastructureLink(
                provinceC,
                provinceD,
                InfrastructureType.Road,
                level: 2
            );

        var network =
            new SupplyNetwork();

        network.AddLink(routeAB);
        network.AddLink(routeBD);
        network.AddLink(routeAC);
        network.AddLink(routeCD);

        var path =
            network.FindWidestPath(
                provinceA,
                provinceD
            );

        Assert.NotNull(path);

        Assert.Equal(
            20,
            path!.Capacity
        );

        Assert.Contains(routeAC, path.Links);
        Assert.Contains(routeCD, path.Links);

        Assert.DoesNotContain(
            routeAB,
            path.Links
        );
    }

        [Fact]  // vérif que lorsque le dépot est saturé, un autre chemin alternatif est utilisé
    public void NetworkUsesAlternativePathWhenBestPathIsSaturated()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");
        var c = new Province(3, "C");
        var d = new Province(4, "D");

        a.ConnectTo(b);
        b.ConnectTo(d);

        a.ConnectTo(c);
        c.ConnectTo(d);

        var ab = new InfrastructureLink(
            a,
            b,
            InfrastructureType.Road,
            level: 2
        );
        var bd = new InfrastructureLink(
            b,
            d,
            InfrastructureType.Road,
            level: 2
        );

        var ac = new InfrastructureLink(
            a,
            c,
            InfrastructureType.Road,
            level: 1
        );
        var cd = new InfrastructureLink(
            c,
            d,
            InfrastructureType.Road,
            level: 1
        );

        var depot =
            new SupplyDepot(
                "Dépôt",
                a,
                fuelStock: 500,
                fuelTransferPerHour: 100
            );

        var division1 =
            new Division(
                "Division 1",
                d,
                fuel: 0,
                ammunition: 100
            );

        var division2 =
            new Division(
                "Division 2",
                d,
                fuel: 0,
                ammunition: 100
            );

        var network = new SupplyNetwork();

        network.AddLink(ab);
        network.AddLink(bd);
        network.AddLink(ac);
        network.AddLink(cd);

        network.ProcessFuelSupply(
            new[] { depot },
            new[]
            {
                division1,
                division2
            }
        );

        double totalReceived =
            division1.Fuel +
            division2.Fuel;

        Assert.Equal(
            30,
            totalReceived
        );
    }

    [Fact]
    public void RailwayHasHigherCapacityThanRoad()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");

        a.ConnectTo(b);

        var road = new InfrastructureLink(
            a,
            b,
            InfrastructureType.Road,
            level: 2
        );

        var railway = new InfrastructureLink(
            a,
            b,
            InfrastructureType.Railway,
            level: 2
        );

        Assert.True(
            railway.TransportCapacityPerHour >
            road.TransportCapacityPerHour
        );
    }

    [Fact]
    public void HigherInfrastructureLevelIncreasesCapacity()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");

        a.ConnectTo(b);

        var roadLevel1 = new InfrastructureLink(
            a,
            b,
            InfrastructureType.Road,
            1
        );

        var roadLevel3 = new InfrastructureLink(
            a,
            b,
            InfrastructureType.Road,
            3
        );

        Assert.True(
            roadLevel3.TransportCapacityPerHour >
            roadLevel1.TransportCapacityPerHour
        );
    }


}
