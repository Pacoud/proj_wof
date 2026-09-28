using WoF.Simulation.Core;
using WoF.Simulation.World;
using WoF.Simulation.Military;
using WoF.Simulation.Logistics;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Tests.Core;

public class SimulationEngineTests
{
    [Fact]
    //On vérifie que un tick est bien égal à une heure 
    public void Tick_AdvancesSimulationByOneHour()
    {
        var simulation = new SimulationEngine();

        simulation.Tick();

        Assert.Equal(1, simulation.Clock.CurrentHour);
    }

    [Fact]
    public void Tick_AdvancesDivisionMovement()
    {
        var ProvinceA = new Province(1, "Province A");
        var ProvinceB = new Province(2, "Province B");

        ProvinceA.ConnectTo(ProvinceB);

        var division = new Division(
            name: "1ere Division",
            position: ProvinceA,
            fuel: 100,
            ammunition: 100 
        );

        var simulation = new SimulationEngine();

        simulation.AddDivision(division);

        division.TryMoveTo(ProvinceB, durationHours: 3);

        simulation.Tick();

        Assert.True(division.IsMoving);
        Assert.Equal(2, division.CurrentMovement!.RemainingHours);

    }

    [Fact]
    public void ThreeTicks_MoveDivisionToDestination()
    {
    var provinceA = new Province(1, "Province A");
    var provinceB = new Province(2, "Province B");

    provinceA.ConnectTo(provinceB);

    var division = new Division(
        name: "1re Division",
        position: provinceA,
        fuel: 100,
        ammunition: 100
    );

    var simulation = new SimulationEngine();

    simulation.AddDivision(division);

    division.TryMoveTo(provinceB, durationHours: 3);

    simulation.Tick();
    simulation.Tick();
    simulation.Tick();

    Assert.Equal(3, simulation.Clock.CurrentHour);

    Assert.Equal(provinceB, division.Position);

    Assert.False(division.IsMoving);
    }


    [Fact]
    public void Tick_ProcessesSupplyDepots()
    {
        var province = new Province(1, "Province A");

        var division = new Division(
            "1re Division",
            province,
            fuel: 20,
            ammunition: 100
        );

        var depot = new SupplyDepot(
            "Dépôt A",
            province,
            fuelStock: 500,
            fuelTransferPerHour: 20
        );

        var simulation = new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddSupplyDepot(depot);

        simulation.Tick();

        Assert.Equal(1, simulation.Clock.CurrentHour);

        Assert.Equal(40, division.Fuel);
        Assert.Equal(480, depot.FuelStock);
    }

        [Fact]
    public void TickSuppliesDivisionThroughRoute()
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

        var simulation = new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddSupplyDepot(depot);
        simulation.AddInfrastructureLink(route);

        simulation.Tick();

        Assert.Equal(1, simulation.Clock.CurrentHour);

        Assert.Equal(40, division.Fuel);
        Assert.Equal(480, depot.FuelStock);
    }

    [Fact]
    public void RoadLevelDeterminesDivisionMovementDuration()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");

        provinceA.ConnectTo(provinceB);

        var road = new InfrastructureLink(
            provinceA,
            provinceB,
            InfrastructureType.Road,
            level: 2
        );

        var division = new Division(
            "1re Division",
            provinceA,
            fuel: 100,
            ammunition: 100
        );

        var simulation = new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(road);

        bool accepted = simulation.TryMoveDivision(
            division,
            provinceB
        );

        Assert.True(accepted);
        Assert.True(division.IsMoving);
        Assert.Equal(4, division.CurrentMovement!.TotalHours);

        simulation.Tick();
        simulation.Tick();
        simulation.Tick();

        Assert.Equal(provinceA, division.Position);

        simulation.Tick();

        Assert.Equal(provinceB, division.Position);
        Assert.False(division.IsMoving);
    }

}

