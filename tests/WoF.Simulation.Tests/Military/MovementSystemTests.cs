using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;
using WoF.Simulation.Core;

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

    [Fact]
    public void MotorizedDivisionMovesFasterThanInfantryOnPlains()
    {
        var a = new Province(
            1,
            "A",
            TerrainType.Plains
        );

        var b = new Province(
            2,
            "B",
            TerrainType.Plains
        );

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                level: 2
            );

        int infantryDuration =
            MovementSystem.CalculateMovementDuration(
                a,
                b,
                new[] { road },
                DivisionType.Infantry
            );

        int motorizedDuration =
            MovementSystem.CalculateMovementDuration(
                a,
                b,
                new[] { road },
                DivisionType.Motorized
            );

        Assert.Equal(4, infantryDuration);
        Assert.Equal(3, motorizedDuration);

        Assert.True(
            motorizedDuration < infantryDuration
        );
    }

    [Fact]
    public void ArmoredDivisionIsStronglySlowedByMountainTerrain()
    {
        var a = new Province(
            1,
            "A",
            TerrainType.Plains
        );

        var mountain = new Province(
            2,
            "Mountain",
            TerrainType.Mountain
        );

        a.ConnectTo(mountain);

        var road =
            new InfrastructureLink(
                a,
                mountain,
                InfrastructureType.Road,
                level: 2
            );

        int infantryDuration =
            MovementSystem.CalculateMovementDuration(
                a,
                mountain,
                new[] { road },
                DivisionType.Infantry
            );

        int armoredDuration =
            MovementSystem.CalculateMovementDuration(
                a,
                mountain,
                new[] { road },
                DivisionType.Armored
            );

        Assert.Equal(8, infantryDuration);
        Assert.Equal(9, armoredDuration);

        Assert.True(
            armoredDuration > infantryDuration
        );
    }

    [Fact]
    public void DivisionDefaultsToInfantry()
    {
        var province =
            new Province(1, "A");

        var division =
            new Division(
                "1re Division",
                province,
                fuel: 100,
                ammunition: 100
            );

        Assert.Equal(
            DivisionType.Infantry,
            division.Type
        );
    }

    [Fact]
    public void SimulationEngineUsesDivisionTypeForMovement()
    {
        var a = new Province(
            1,
            "A",
            TerrainType.Plains
        );

        var b = new Province(
            2,
            "B",
            TerrainType.Plains
        );

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                level: 2
            );

        var division =
            new Division(
                name: "Division motorisée",
                position: a,
                fuel: 100,
                ammunition: 100,
                type: DivisionType.Motorized
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(road);

        bool accepted =
            simulation.TryMoveDivision(
                division,
                b
            );

        Assert.True(accepted);

        Assert.Equal(
            3,
            division.Transit!.TotalHours
        );

        simulation.Tick();
        simulation.Tick();

        Assert.Null(division.CurrentProvince);

        simulation.Tick();

        Assert.Equal(b, division.CurrentProvince);
    }



    [Fact]
    public void HeavierDivisionTypesConsumeMoreFuel()
    {
        var a = new Province(
            1,
            "A",
            TerrainType.Plains
        );

        var b = new Province(
            2,
            "B",
            TerrainType.Plains
        );

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                level: 2
            );

        var links = new[] { road };

        var infantry =
            MovementSystem.CreateMovementPlan(
                a,
                b,
                links,
                DivisionType.Infantry
            );

        var motorized =
            MovementSystem.CreateMovementPlan(
                a,
                b,
                links,
                DivisionType.Motorized
            );

        var armored =
            MovementSystem.CreateMovementPlan(
                a,
                b,
                links,
                DivisionType.Armored
            );

        Assert.True(
            infantry.EstimatedFuelCost
            < motorized.EstimatedFuelCost
        );

        Assert.True(
            motorized.EstimatedFuelCost
            < armored.EstimatedFuelCost
        );
    }

    [Fact]
    public void MountainMovementConsumesMoreFuelThanPlains()
    {
        var origin =
            new Province(
                1,
                "A",
                TerrainType.Plains
            );

        var plains =
            new Province(
                2,
                "Plains",
                TerrainType.Plains
            );

        var mountain =
            new Province(
                3,
                "Mountain",
                TerrainType.Mountain
            );

        origin.ConnectTo(plains);
        origin.ConnectTo(mountain);

        var plainsRoad =
            new InfrastructureLink(
                origin,
                plains,
                InfrastructureType.Road,
                2
            );

        var mountainRoad =
            new InfrastructureLink(
                origin,
                mountain,
                InfrastructureType.Road,
                2
            );

        var plainsPlan =
            MovementSystem.CreateMovementPlan(
                origin,
                plains,
                new[] { plainsRoad },
                DivisionType.Armored
            );

        var mountainPlan =
            MovementSystem.CreateMovementPlan(
                origin,
                mountain,
                new[] { mountainRoad },
                DivisionType.Armored
            );

        Assert.True(
            mountainPlan.EstimatedFuelCost
            > plainsPlan.EstimatedFuelCost
        );
    }


    [Fact]
    public void BetterRoadReducesFuelConsumption()
    {
        var a =
            new Province(1, "A");

        var b =
            new Province(2, "B");

        a.ConnectTo(b);

        var roadL1 =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                1
            );

        var roadL3 =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                3
            );

        var badRoadPlan =
            MovementSystem.CreateMovementPlan(
                a,
                b,
                new[] { roadL1 },
                DivisionType.Armored
            );

        var goodRoadPlan =
            MovementSystem.CreateMovementPlan(
                a,
                b,
                new[] { roadL3 },
                DivisionType.Armored
            );

        Assert.True(
            goodRoadPlan.EstimatedFuelCost
            < badRoadPlan.EstimatedFuelCost
        );
    }


    [Fact]
    public void StartingMovementDoesNotConsumeFuelImmediately()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var division =
            new Division(
                "Division blindée",
                a,
                fuel: 100,
                ammunition: 100,
                type: DivisionType.Armored
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(road);

        bool accepted =
            simulation.TryMoveDivision(
                division,
                b
            );

        Assert.True(accepted);

        Assert.Equal(
            100,
            division.Fuel
        );
    }

    [Fact]
    public void MovingDivisionConsumesFuelEachTick()
    {
        var a =
            new Province(
                1,
                "A",
                TerrainType.Plains
            );

        var b =
            new Province(
                2,
                "B",
                TerrainType.Plains
            );

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var division =
            new Division(
                "Division blindée",
                a,
                fuel: 100,
                ammunition: 100,
                type: DivisionType.Armored
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(road);

        simulation.TryMoveDivision(
            division,
            b
        );

        simulation.Tick();

        Assert.Equal(
            96.2,
            division.Fuel
        );

        simulation.Tick();

        Assert.Equal(
            92.4,
            division.Fuel
        );
    }


    [Fact]
    public void StoppedDivisionDoesNotConsumeFuelOrProgress()
    {
        var a =
            new Province(1, "A");

        var b =
            new Province(2, "B");

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var division =
            new Division(
                "Division blindée",
                a,
                fuel: 100,
                ammunition: 100,
                type: DivisionType.Armored
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(road);

        simulation.TryMoveDivision(
            division,
            b
        );

        simulation.Tick();

        double fuelAfterFirstHour =
            division.Fuel;

        int remainingHours =
            division.Transit!
                .RemainingHours;

        bool stopped = division.StopMovement();

        Assert.True(stopped);

        simulation.Tick();
        simulation.Tick();
        simulation.Tick();

        Assert.Equal(
            fuelAfterFirstHour,
            division.Fuel
        );

        Assert.Equal(
            remainingHours,
            division.Transit!
                .RemainingHours
        );

        Assert.False(
            division.IsMoving
        );

        Assert.True(
            division.IsInTransit
        );
    }



    [Fact]
    public void DivisionCanResumePausedMovement()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var division =
            new Division(
                "Division blindée",
                a,
                100,
                100,
                type: DivisionType.Armored
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(road);

        simulation.TryMoveDivision(
            division,
            b
        );

        simulation.Tick();

        division.StopMovement();

        int remainingBeforePause =
            division.Transit!
                .RemainingHours;

        simulation.Tick();

        Assert.Equal(
            remainingBeforePause,
            division.Transit!
                .RemainingHours
        );

        bool resumed = division.ResumeMovement();

        Assert.True(resumed);

        simulation.Tick();

        Assert.Equal(
            remainingBeforePause - 1,
            division.Transit!
                .RemainingHours
        );
    }

    [Fact]
    public void DivisionStopsWhenItRunsOutOfFuelDuringMovement()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var division =
            new Division(
                "Division blindée",
                a,
                fuel: 5,
                ammunition: 100,
                type: DivisionType.Armored
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(road);

        bool accepted =
            simulation.TryMoveDivision(
                division,
                b
            );

        Assert.True(accepted);

        simulation.Tick();

        Assert.Equal(
            1.2,
            division.Fuel
        );

        int remaining =
            division.Transit!
                .RemainingHours;

        simulation.Tick();

        Assert.False(
            division.IsMoving
        );

        Assert.True(
            division.IsInTransit
        );

        Assert.Equal(
            remaining,
            division.Transit!
                .RemainingHours
        );

        Assert.Equal(
            1.2,
            division.Fuel
        );
    }


    [Fact]
    public void StartingMovementRemovesDivisionFromOriginProvince()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var division =
            new Division(
                "1re Division",
                a,
                100,
                100
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(road);

        bool accepted =
            simulation.TryMoveDivision(
                division,
                b
            );

        Assert.True(accepted);

        Assert.Null(
            division.CurrentProvince
        );

        Assert.NotNull(
            division.Transit
        );

        Assert.Equal(
            a,
            division.Transit!.Origin
        );

        Assert.Equal(
            b,
            division.Transit.Destination
        );

        Assert.Equal(
            0,
            division.Transit.Progress
        );
    }


    [Fact]
    public void TransitProgressIncreasesEachTick()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var division =
            new Division(
                "1re Division",
                a,
                100,
                100
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(road);

        simulation.TryMoveDivision(
            division,
            b
        );

        simulation.Tick();

        Assert.Equal(
            0.25,
            division.Transit!.Progress
        );

        simulation.Tick();

        Assert.Equal(
            0.50,
            division.Transit.Progress
        );
    }

    [Fact]
    public void CompletedTransitPlacesDivisionInDestination()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var division =
            new Division(
                "1re Division",
                a,
                100,
                100
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);
        simulation.AddInfrastructureLink(road);

        simulation.TryMoveDivision(
            division,
            b
        );

        simulation.Tick();
        simulation.Tick();
        simulation.Tick();
        simulation.Tick();

        Assert.False(
            division.IsInTransit
        );

        Assert.Null(
            division.Transit
        );

        Assert.Equal(
            b,
            division.CurrentProvince
        );
    }

    [Fact]
    public void DestinationCanBeQueuedWhileDivisionIsInTransit()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");
        var c = new Province(3, "C");

        a.ConnectTo(b);
        b.ConnectTo(c);

        var roadAB =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var roadBC =
            new InfrastructureLink(
                b,
                c,
                InfrastructureType.Road,
                2
            );

        var division =
            new Division(
                "1re Division",
                a,
                100,
                100
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);

        simulation.AddInfrastructureLink(
            roadAB
        );

        simulation.AddInfrastructureLink(
            roadBC
        );

        simulation.TryMoveDivision(
            division,
            b
        );

        simulation.Tick();

        bool queued =
            simulation.TryMoveDivision(
                division,
                c
            );

        Assert.True(queued);

        Assert.Equal(
            c,
            division.PendingDestination
        );
    }

    [Fact]
    public void QueuedDestinationStartsAfterCurrentTransitCompletes()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");
        var c = new Province(3, "C");

        a.ConnectTo(b);
        b.ConnectTo(c);

        var roadAB =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var roadBC =
            new InfrastructureLink(
                b,
                c,
                InfrastructureType.Road,
                2
            );

        var division =
            new Division(
                "1re Division",
                a,
                100,
                100
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(division);

        simulation.AddInfrastructureLink(roadAB);
        simulation.AddInfrastructureLink(roadBC);

        simulation.TryMoveDivision(
            division,
            b
        );

        simulation.Tick();

        simulation.TryMoveDivision(
            division,
            c
        );

        // Fin du trajet A -> B
        simulation.Tick();
        simulation.Tick();
        simulation.Tick();

        // Dès l'arrivée en B,
        // le trajet B -> C a été créé.
        Assert.True(
            division.IsInTransit
        );

        Assert.Equal(
            b,
            division.Transit!.Origin
        );

        Assert.Equal(
            c,
            division.Transit.Destination
        );

        Assert.Equal(
            0,
            division.Transit.Progress
        );

        Assert.Null(
            division.PendingDestination
        );
    }
}
