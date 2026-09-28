using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military;

public class DivisionTests
{
    [Fact]
    public void Division_CanStartMovingToNeighbouringProvince()
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

        var movementPlan = new MovementPlan(
            DurationHours: 3,
            FuelPerHour: 8
        );

        bool success = division.TryStartMovement(provinceB, movementPlan);

        Assert.True(success);
        Assert.Null(division.CurrentProvince);
        Assert.Equal(100, division.Fuel);
        Assert.True(division.IsMoving);
        Assert.Equal(provinceA, division.Transit!.Origin);
        Assert.Equal(provinceB, division.Transit.Destination);
        Assert.Equal(3, division.Transit.RemainingHours);
    }

    [Fact]
    public void Division_CannotMoveToNonNeighbouringProvince()
    {
        var provinceA = new Province(1, "Province A");
        var provinceC = new Province(3, "Province C");

        var division = new Division(
            name: "1re Division",
            position: provinceA,
            fuel: 100,
            ammunition: 100
        );

        var movementPlan = new MovementPlan(
            DurationHours: 3,
            FuelPerHour: 8
        );

        bool success = division.TryStartMovement(provinceC, movementPlan);

        Assert.False(success);
        Assert.Equal(provinceA, division.CurrentProvince);
        Assert.Equal(100, division.Fuel);
        Assert.False(division.IsMoving);
    }

    [Fact]
    public void Division_CannotMoveWithoutEnoughFuel()
    {
        var provinceA = new Province(1, "Province A");
        var provinceB = new Province(2, "Province B");

        provinceA.ConnectTo(provinceB);

        var division = new Division(
            name: "1re Division",
            position: provinceA,
            fuel: 5,
            ammunition: 100
        );

        var movementPlan = new MovementPlan(
            DurationHours: 3,
            FuelPerHour: 8
        );

        bool success = division.TryStartMovement(provinceB, movementPlan);

        Assert.False(success);
        Assert.Equal(provinceA, division.CurrentProvince);
        Assert.Equal(5, division.Fuel);
        Assert.False(division.IsMoving);
    }
}
