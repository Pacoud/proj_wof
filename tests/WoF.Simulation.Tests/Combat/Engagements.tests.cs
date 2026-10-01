using WoF.Simulation.Logistics;
using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;
using WoF.Simulation.Core;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Fuel;

namespace WoF.Simulation.Tests.Combat;

public class EngagementsTests
{

    [Fact]
    public void EnemyDefenderPreventsProvinceCapture()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

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
                owner: germany
            );

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                3
            );

        var attacker =
            new Division(
                "French Division",
                a,
                100,
                100,
                country: france
            );

        var defender =
            new Division(
                "German Division",
                b,
                100,
                100,
                country: germany
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(attacker);
        simulation.AddDivision(defender);

        simulation.AddInfrastructureLink(
            road
        );

        simulation.DeclareWar(
            france,
            germany
        );

        simulation.TryOrderMoveTo(
            attacker,
            b
        );

        while (attacker.IsInTransit)
        {
            simulation.Tick();
        }

        Assert.True(
            attacker.IsEngaged
        );

        Assert.True(
            defender.IsEngaged
        );

        Assert.Same(
            germany,
            b.Controller
        );

        Assert.Single(
            simulation
                .Engagements
                .ActiveEngagements
        );
    }

    [Fact]
    public void OpposingDivisionsEngageWhenTheyMeetOnConnection()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

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
                owner: germany
            );

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var french =
            new Division(
                "French Division",
                a,
                100,
                100,
                country: france
            );

        var german =
            new Division(
                "German Division",
                b,
                100,
                100,
                country: germany
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(french);
        simulation.AddDivision(german);

        simulation.AddInfrastructureLink(
            road
        );

        simulation.DeclareWar(
            france,
            germany
        );

        simulation.TryOrderMoveTo(
            french,
            b
        );

        simulation.TryOrderMoveTo(
            german,
            a
        );

        simulation.Tick();
        simulation.Tick();

        Assert.True(
            french.IsEngaged
        );

        Assert.True(
            german.IsEngaged
        );

        Assert.Equal(
            DivisionOperationalState.Engaged,
            french.OperationalState
        );
    }

    [Fact]
    public void EngagedDivisionDoesNotContinueMovement()
    {
        var (simulation, french, _) =
            CreateOpposingTransitScenario();

        simulation.Tick();
        simulation.Tick();

        double progress =
            french.Transit!.Progress;

        simulation.Tick();

        Assert.Equal(
            progress,
            french.Transit.Progress
        );

        Assert.True(
            french.IsEngaged
        );
    }


    [Fact]
    public void ArmoredDivisionConsumesFuelDuringEngagement()
    {
        var (simulation, armored) =
            CreateArmoredProvinceEngagementScenario(
                armoredFuel: 100
            );

        double expectedFuelDemand =
        CompositionCombatFuelCalculator
            .Calculate(
                armored.Composition
            )
            .Total;

        double fuelBeforeCombatTick =
            armored.Fuel;

        // Le premier tick détecte l'engagement.
        simulation.Tick();

        Assert.Equal(
        fuelBeforeCombatTick,
        armored.Fuel
        );

        simulation.Tick();

        Assert.Equal(
            fuelBeforeCombatTick - expectedFuelDemand, armored.Fuel,
            2
        );

        Assert.True(
            armored.IsEngaged
        );
    }

    [Fact]
    public void EngagementContinuesWhenDivisionRunsOutOfFuel()
    {
        var (simulation, armored) =
            CreateArmoredProvinceEngagementScenario(
                armoredFuel: 3
            );

        // Le premier tick détecte l'engagement.
        simulation.Tick();

        // Le second tick consomme le carburant de combat.
        simulation.Tick();

        Assert.Equal(
            0,
            armored.Fuel
        );

        Assert.True(
            armored.IsEngaged
        );

        Assert.Equal(
            DivisionOperationalState.Engaged,
            armored.OperationalState
        );
    }

    private static (
        SimulationEngine Simulation,
        Division French,
        Division German)
        CreateOpposingTransitScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var a = new Province(1, "A", owner: france);
        var b = new Province(2, "B", owner: germany);

        a.ConnectTo(b);

        var road = new InfrastructureLink(
            a,
            b,
            InfrastructureType.Road,
            level: 2
        );

        var french = new Division(
            "French Division",
            a,
            fuel: 100,
            ammunition: 100,
            country: france
        );

        var german = new Division(
            "German Division",
            b,
            fuel: 100,
            ammunition: 100,
            country: germany
        );

        var simulation = new SimulationEngine();

        simulation.AddDivision(french);
        simulation.AddDivision(german);
        simulation.AddInfrastructureLink(road);
        simulation.DeclareWar(france, germany);

        simulation.TryOrderMoveTo(french, b);
        simulation.TryOrderMoveTo(german, a);

        return (simulation, french, german);
    }

    private static (
        SimulationEngine Simulation,
        Division Armored)
        CreateArmoredProvinceEngagementScenario(
            double armoredFuel)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(
            1,
            "Battlefield",
            owner: france
        );

        var armored = new Division(
            "French Armored Division",
            province,
            fuel: armoredFuel,
            ammunition: 100,
            type: DivisionType.Armored,
            country: france
        );

        var enemy = new Division(
            "German Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: germany
        );

        var simulation = new SimulationEngine();

        simulation.AddDivision(armored);
        simulation.AddDivision(enemy);
        simulation.DeclareWar(france, germany);

        return (simulation, armored);
    }


    [Fact]
    public void ArrivingDivisionIsAttackerAndStationaryEnemyIsDefender()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

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
                owner: germany
            );

        a.ConnectTo(b);

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                level: 2
            );

        var french =
            new Division(
                "French Division",
                a,
                100,
                100,
                country: france
            );

        var german =
            new Division(
                "German Division",
                b,
                100,
                100,
                country: germany
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(french);
        simulation.AddDivision(german);

        simulation.AddInfrastructureLink(
            road
        );

        simulation.DeclareWar(
            france,
            germany
        );

        simulation.TryOrderMoveTo(
            french,
            b
        );

        int safety = 20;

        while (!french.IsEngaged
            && safety-- > 0)
        {
            simulation.Tick();
        }

        Assert.True(
            safety > 0
        );

        var engagement =
            Assert.Single(
                simulation
                    .Engagements
                    .ActiveEngagements
            );

        Assert.Equal(
            EngagementType.ProvinceBattle,
            engagement.Type
        );

        Assert.Equal(
            EngagementRole.Attacker,
            engagement.GetRole(french)
        );

        Assert.Equal(
            EngagementRole.Defender,
            engagement.GetRole(german)
        );

        Assert.Contains(
            french,
            engagement.Attackers
        );

        Assert.Contains(
            german,
            engagement.Defenders
        );
    }

    [Fact]
    public void MeetingEngagementHasNoAttackerOrDefenderRoles()
    {
        var (
            simulation,
            french,
            german
        ) =
            CreateOpposingTransitScenario();

        simulation.Tick();
        simulation.Tick();

        var engagement =
            Assert.Single(
                simulation
                    .Engagements
                    .ActiveEngagements
            );

        Assert.Equal(
            EngagementType.MeetingEngagement,
            engagement.Type
        );

        Assert.Equal(
            EngagementRole.None,
            engagement.GetRole(french)
        );

        Assert.Equal(
            EngagementRole.None,
            engagement.GetRole(german)
        );

        Assert.Empty(
            engagement.Attackers
        );

        Assert.Empty(
            engagement.Defenders
        );
    }


    [Fact]
    public void CoLocatedDivisionsWithoutArrivalHaveNoForcedRoles()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var province =
            new Province(
                1,
                "Battlefield",
                owner: france
            );

        var french =
            new Division(
                "French Division",
                province,
                100,
                100,
                country: france
            );

        var german =
            new Division(
                "German Division",
                province,
                100,
                100,
                country: germany
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(french);
        simulation.AddDivision(german);

        simulation.DeclareWar(
            france,
            germany
        );

        simulation.Tick();

        var engagement =
            Assert.Single(
                simulation
                    .Engagements
                    .ActiveEngagements
            );

        Assert.Equal(
            EngagementType.ProvinceBattle,
            engagement.Type
        );

        Assert.Equal(
            EngagementRole.None,
            engagement.GetRole(french)
        );

        Assert.Equal(
            EngagementRole.None,
            engagement.GetRole(german)
        );
    }

}
