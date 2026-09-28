using WoF.Simulation.Logistics;
using WoF.Simulation.Military;
using WoF.Simulation.World;

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

        var route = new SupplyRoute(
            provinceA,
            provinceB,
            fuelCapacityPerHour: 15
        );

        var network = new SupplyNetwork();

        network.AddRoute(route);

        network.ProcessFuelSupply(
            new[] { depot },
            new[] { division }
        );

        Assert.Equal(35, division.Fuel);
        Assert.Equal(485, depot.FuelStock);
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

        var route = new SupplyRoute(
            provinceA,
            provinceB,
            fuelCapacityPerHour: 20
        );

        var network = new SupplyNetwork();

        network.AddRoute(route);

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

        var routeAB = new SupplyRoute(
            provinceA,
            provinceB,
            20
        );

        var routeBC = new SupplyRoute(
            provinceB,
            provinceC,
            12
        );

        var network = new SupplyNetwork();

        network.AddRoute(routeAB);
        network.AddRoute(routeBC);

        var path = network.FindPath(
            provinceA,
            provinceC
        );

        Assert.NotNull(path);

        Assert.Equal(2, path!.Routes.Count);

        Assert.Equal(12, path.Capacity);
    }

    [Fact]
    public void NetworkReturnsNullWhenNoPathExists()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");
        var provinceC = new Province(3, "C");

        provinceA.ConnectTo(provinceB);

        var routeAB = new SupplyRoute(
            provinceA,
            provinceB,
            20
        );

        var network = new SupplyNetwork();

        network.AddRoute(routeAB);

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

        var routeAB = new SupplyRoute(
            provinceA,
            provinceB,
            fuelCapacityPerHour: 20
        );

        var routeBC = new SupplyRoute(
            provinceB,
            provinceC,
            fuelCapacityPerHour: 12
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

        network.AddRoute(routeAB);
        network.AddRoute(routeBC);

        network.ProcessFuelSupply(
            new[] { depot },
            new[] { division }
        );

        Assert.Equal(32, division.Fuel);
        Assert.Equal(488, depot.FuelStock);
    }

    [Fact]  //pathfinding, deux divisions empruntent le meme chemin/route
    public void PathCapacityIsSharedBetweenDivisions()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");
        var provinceC = new Province(3, "C");

        provinceA.ConnectTo(provinceB);
        provinceB.ConnectTo(provinceC);

        var routeAB = new SupplyRoute(
            provinceA,
            provinceB,
            20
        );

        var routeBC = new SupplyRoute(
            provinceB,
            provinceC,
            12
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

        network.AddRoute(routeAB);
        network.AddRoute(routeBC);

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

        Assert.Equal(12, totalReceived);
        Assert.Equal(488, depot.FuelStock);
    }

}