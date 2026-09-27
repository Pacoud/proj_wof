using WoF.Simulation.Core;
using WoF.Simulation.World;
using WoF.Simulation.Military;

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

        division.TryMoveTo(ProvinceB);

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

    division.TryMoveTo(provinceB);

    simulation.Tick();
    simulation.Tick();
    simulation.Tick();

    Assert.Equal(3, simulation.Clock.CurrentHour);

    Assert.Equal(provinceB, division.Position);

    Assert.False(division.IsMoving);
    }




}

