using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Tests.World;

public class ProvinceControlTests
{
    [Fact]
    public void ControllerDefaultsToOwner()
    {
        var france =
            new Country(1, "France");

        var province =
            new Province(
                id: 1,
                name: "A",
                owner: france
            );

        Assert.Same(
            france,
            province.Owner
        );

        Assert.Same(
            france,
            province.Controller
        );
    }

    [Fact]
    public void ChangingControllerDoesNotChangeOwner()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var province =
            new Province(
                id: 1,
                name: "A",
                owner: france
            );

        province.ChangeController(
            germany
        );

        Assert.Same(
            france,
            province.Owner
        );

        Assert.Same(
            germany,
            province.Controller
        );
    }


    [Fact]
    public void ArrivingDivisionCapturesEnemyControlledProvince()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var provinceA =
            new Province(
                1,
                "A",
                owner: france
            );

        var provinceB =
            new Province(
                2,
                "B",
                owner: germany
            );

        provinceA.ConnectTo(
            provinceB
        );

        var road =
            new InfrastructureLink(
                provinceA,
                provinceB,
                InfrastructureType.Road,
                3
            );

        var division =
            new Division(
                name: "1re Division",
                position: provinceA,
                fuel: 100,
                ammunition: 100,
                country: france
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(
            division
        );

        simulation.AddInfrastructureLink(
            road
        );

        simulation.TryOrderMoveTo(
            division,
            provinceB
        );

        while (division.IsInTransit)
        {
            simulation.Tick();
        }

        Assert.Same(
            germany,
            provinceB.Owner
        );

        Assert.Same(
            france,
            provinceB.Controller
        );
    }


    [Fact]
    public void FriendlyProvinceKeepsSameController()
    {
        var france =
            new Country(1, "France");

        var a =
            new Province(
                1,
                "A",
                owner: france
            );

        var b =
            new Province(
                2,
                "B",
                owner: france
            );

        a.ConnectTo(b);

        var division =
            new Division(
                "1re Division",
                a,
                100,
                100,
                country: france
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(
            division
        );

        simulation.TryOrderMoveTo(
            division,
            b
        );

        while (division.IsInTransit)
        {
            simulation.Tick();
        }

        Assert.Same(
            france,
            b.Owner
        );

        Assert.Same(
            france,
            b.Controller
        );
    }
}
