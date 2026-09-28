using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Tests.Military;

public class MovementSystemTests
{
    [Fact]
    public void HigherRoadLevelReducesMovementDuration()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");

        provinceA.ConnectTo(provinceB);

        var roadLevel1 = new InfrastructureLink(
            provinceA,
            provinceB,
            InfrastructureType.Road,
            level: 1
        );

        var roadLevel3 = new InfrastructureLink(
            provinceA,
            provinceB,
            InfrastructureType.Road,
            level: 3
        );

        int slowDuration = MovementSystem.CalculateMovementDuration(
            provinceA,
            provinceB,
            new[] { roadLevel1 }
        );

        int fastDuration = MovementSystem.CalculateMovementDuration(
            provinceA,
            provinceB,
            new[] { roadLevel3 }
        );

        Assert.Equal(5, slowDuration);
        Assert.Equal(3, fastDuration);
        Assert.True(fastDuration < slowDuration);
    }

    [Fact]
    public void MovementWithoutRoadUsesBaseDuration()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");

        provinceA.ConnectTo(provinceB);

        int duration = MovementSystem.CalculateMovementDuration(
            provinceA,
            provinceB,
            Array.Empty<InfrastructureLink>()
        );

        Assert.Equal(6, duration);
    }

    [Fact]
    public void RailwayDoesNotSpeedUpNormalMovement()
    {
        var provinceA = new Province(1, "A");
        var provinceB = new Province(2, "B");

        provinceA.ConnectTo(provinceB);

        var railway = new InfrastructureLink(
            provinceA,
            provinceB,
            InfrastructureType.Railway,
            level: 3
        );

        int duration = MovementSystem.CalculateMovementDuration(
            provinceA,
            provinceB,
            new[] { railway }
        );

        Assert.Equal(6, duration);
    }
}
