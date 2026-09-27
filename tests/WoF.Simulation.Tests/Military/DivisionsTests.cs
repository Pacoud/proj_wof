using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military;

public class DivisionTests
{
    [Fact]
    public void Division_CanMoveToNeighbouringProvince()
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

        bool success = division.MoveTo(provinceB);

        Assert.True(success);
        Assert.Equal(provinceB, division.Position);
        Assert.Equal(92, division.Fuel);
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

        bool success = division.MoveTo(provinceC);

        Assert.False(success);
        Assert.Equal(provinceA, division.Position);
        Assert.Equal(100, division.Fuel);
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

        bool success = division.MoveTo(provinceB);

        Assert.False(success);
        Assert.Equal(provinceA, division.Position);
        Assert.Equal(5, division.Fuel);
    }
}