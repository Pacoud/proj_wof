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

        [Fact]
        public void MountainTerrainSlowsMovement()
        {
            var origin = new Province(
                1,
                "A",
                TerrainType.Plains
            );

            var plains = new Province(
                2,
                "Plaine",
                TerrainType.Plains
            );

            var mountain = new Province(
                3,
                "Montagne",
                TerrainType.Mountain
            );

            origin.ConnectTo(plains);
            origin.ConnectTo(mountain);

            var roadToPlains =
                new InfrastructureLink(
                    origin,
                    plains,
                    InfrastructureType.Road,
                    level: 3
                );

            var roadToMountain =
                new InfrastructureLink(
                    origin,
                    mountain,
                    InfrastructureType.Road,
                    level: 3
                );

            int plainsDuration =
                MovementSystem.CalculateMovementDuration(
                    origin,
                    plains,
                    new[] { roadToPlains }
                );

            int mountainDuration =
                MovementSystem.CalculateMovementDuration(
                    origin,
                    mountain,
                    new[] { roadToMountain }
                );

            Assert.Equal(3, plainsDuration);
            Assert.Equal(6, mountainDuration);

            Assert.True(
                mountainDuration > plainsDuration
            );
        }

        [Fact]
    public void BetterRoadStillImprovesMovementInMountainTerrain()
    {
        var origin = new Province(
            1,
            "A"
        );

        var mountain = new Province(
            2,
            "Montagne",
            TerrainType.Mountain
        );

        origin.ConnectTo(mountain);

        var roadLevel1 =
            new InfrastructureLink(
                origin,
                mountain,
                InfrastructureType.Road,
                level: 1
            );

        var roadLevel3 =
            new InfrastructureLink(
                origin,
                mountain,
                InfrastructureType.Road,
                level: 3
            );

        int level1Duration =
            MovementSystem.CalculateMovementDuration(
                origin,
                mountain,
                new[] { roadLevel1 }
            );

        int level3Duration =
            MovementSystem.CalculateMovementDuration(
                origin,
                mountain,
                new[] { roadLevel3 }
            );

        Assert.Equal(10, level1Duration);
        Assert.Equal(6, level3Duration);

        Assert.True(
            level3Duration < level1Duration
        );
    }


    [Fact]
    public void MarshWithoutRoadIsVerySlow()
    {
        var origin = new Province(
            1,
            "A"
        );

        var marsh = new Province(
            2,
            "Marais",
            TerrainType.Marsh
        );

        origin.ConnectTo(marsh);

        int duration =
            MovementSystem.CalculateMovementDuration(
                origin,
                marsh,
                Array.Empty<InfrastructureLink>()
            );

        Assert.Equal(10, duration);
    }

}
