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
            new SupplyRoute(
                provinceA,
                provinceB,
                5
            );

        var routeBD =
            new SupplyRoute(
                provinceB,
                provinceD,
                5
            );

        var routeAC =
            new SupplyRoute(
                provinceA,
                provinceC,
                30
            );

        var routeCD =
            new SupplyRoute(
                provinceC,
                provinceD,
                20
            );

        var network =
            new SupplyNetwork();

        network.AddRoute(routeAB);
        network.AddRoute(routeBD);
        network.AddRoute(routeAC);
        network.AddRoute(routeCD);

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

        Assert.Contains(routeAC, path.Routes);
        Assert.Contains(routeCD, path.Routes);

        Assert.DoesNotContain(
            routeAB,
            path.Routes
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

        var ab = new SupplyRoute(a, b, 20);
        var bd = new SupplyRoute(b, d, 20);

        var ac = new SupplyRoute(a, c, 15);
        var cd = new SupplyRoute(c, d, 15);

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

        network.AddRoute(ab);
        network.AddRoute(bd);
        network.AddRoute(ac);
        network.AddRoute(cd);

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
            35,
            totalReceived
        );
    }

}