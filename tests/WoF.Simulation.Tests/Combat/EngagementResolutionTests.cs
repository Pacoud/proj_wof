using WoF.Simulation.Logistics;
using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;
using WoF.Simulation.Core;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Tests.Combat;

public class EngagementsResolutionTests

{


private static (
    SimulationEngine Simulation,
    Division French,
    Division German
) CreateProvinceEngagement(
    DivisionType frenchType = DivisionType.Infantry,
    DivisionType germanType = DivisionType.Infantry,
    double frenchFuel = 100,
    double germanFuel = 100,
    double frenchOrganization = 100,
    double germanOrganization = 100)
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
            name: "French Division",
            position: province,
            fuel: frenchFuel,
            ammunition: 100,
            type: frenchType,
            country: france,
            maxOrganization: frenchOrganization
        );

    var german =
        new Division(
            name: "German Division",
            position: province,
            fuel: germanFuel,
            ammunition: 100,
            type: germanType,
            country: germany,
            maxOrganization: germanOrganization
        );

    var simulation =
        new SimulationEngine();

    simulation.AddDivision(french);
    simulation.AddDivision(german);

    simulation.DeclareWar(
        france,
        germany
    );

    // Premier tick :
    // détection de l'engagement.
    simulation.Tick();

    return (
        simulation,
        french,
        german
    );

    }


    [Fact]
    public void CombatTickReducesOrganization()
    {
        var scenario =
            CreateProvinceEngagement();

        Assert.True(
            scenario.French.IsEngaged
        );

        Assert.Equal(
            100,
            scenario.French.Organization
        );

        scenario.Simulation.Tick();

        Assert.True(
            scenario.French.Organization < 100
        );

        Assert.True(
            scenario.German.Organization < 100
        );
    }


    private static double RunArmoredVsInfantryCombatTick(
    double armoredFuel)
    {
        var scenario =
            CreateProvinceEngagement(
                frenchType: DivisionType.Armored,
                germanType: DivisionType.Infantry,
                frenchFuel: armoredFuel
            );

        scenario.Simulation.Tick();

        return scenario.German.Organization;
    }

    [Fact]
    public void FueledArmoredDivisionAppliesMorePressureThanDryArmoredDivision()
    {
        double organizationAgainstFueledArmored =
            RunArmoredVsInfantryCombatTick(
                armoredFuel: 100
            );

        double organizationAgainstDryArmored =
            RunArmoredVsInfantryCombatTick(
                armoredFuel: 0
            );

        Assert.True(
            organizationAgainstFueledArmored
            <
            organizationAgainstDryArmored
        );
    }

    [Fact]
    public void DivisionBecomesBrokenAtZeroOrganization()
    {
        var scenario =
            CreateProvinceEngagement(
                germanOrganization: 5
            );

        scenario.Simulation.Tick();

        Assert.Equal(
            0,
            scenario.German.Organization
        );

        Assert.True(
            scenario.German.IsBroken
        );
    }


    [Fact]
    public void EngagementEndsWhenOnlyOneSideRemainsCombatCapable()
    {
        var scenario =
            CreateProvinceEngagement(
                germanOrganization: 5
            );

        var engagement =
            scenario.French.CurrentEngagement!;

        scenario.Simulation.Tick();

        Assert.False(
            engagement.IsActive
        );

        Assert.False(
            scenario.French.IsEngaged
        );

        Assert.False(
            scenario.German.IsEngaged
        );

        Assert.True(
            scenario.German.IsBroken
        );

        Assert.Equal(
            DivisionOperationalState.Broken,
            scenario.German.OperationalState
        );

        Assert.Equal(
            scenario.French.Country,
            engagement.WinnerCountry
        );
    }


    [Fact]
    public void BrokenDivisionCannotReceiveMovementOrder()
    {
        var scenario =
            CreateProvinceEngagement(
                germanOrganization: 5
            );

        scenario.Simulation.Tick();

        var destination =
            new Province(
                2,
                "Retreat Province",
                owner: scenario.German.Country
            );

        var currentProvince =
            scenario.German.CurrentProvince!;

        currentProvince.ConnectTo(
            destination
        );

        bool accepted =
            scenario.Simulation.TryOrderMoveTo(
                scenario.German,
                destination
            );

        Assert.False(accepted);
    }


    [Fact]
    public void BrokenDivisionDoesNotCreateNewEngagementAfterBattleEnds()
    {
        var scenario =
            CreateProvinceEngagement(
                germanOrganization: 5
            );

        scenario.Simulation.Tick();

        Assert.True(
            scenario.German.IsBroken
        );

        Assert.False(
            scenario.German.IsEngaged
        );

        Assert.False(
            scenario.French.IsEngaged
        );

        int engagementCount =
            scenario.Simulation
                .Engagements
                .Engagements
                .Count;

        scenario.Simulation.Tick();

        Assert.Equal(
            engagementCount,
            scenario.Simulation
                .Engagements
                .Engagements
                .Count
        );

        Assert.False(
            scenario.German.IsEngaged
        );

        Assert.False(
            scenario.French.IsEngaged
        );
    }

        [Fact]
    public void ReducedPhysicalCompositionAppliesLessOrganizationPressure()
    {
        double fullStrengthResult =
            RunCombatAgainstComposition(
                tanks: 180
            );

        double depletedResult =
            RunCombatAgainstComposition(
                tanks: 30
            );

        // Plus l'organisation ennemie est basse,
        // plus la pression reçue était élevée.
        Assert.True(
            fullStrengthResult
            <
            depletedResult
        );
    }

        private static double RunCombatAgainstComposition(
        int tanks)
        {
            var france =
                new Country(
                    1,
                    "France"
                );

            var germany =
                new Country(
                    2,
                    "Germany"
                );

            var province =
                new Province(
                    1,
                    "Battlefield",
                    owner: france
                );

            var armoredComposition =
                new DivisionComposition(
                    new PersonnelPool(
                        authorized: 8_000,
                        current: 8_000
                    ),
                    new StrengthPool(
                        authorized: 5_000,
                        current: 5_000
                    ),
                    new StrengthPool(
                        authorized: 36,
                        current: 36
                    ),
                    new TankPool(
                        authorized: 180,
                        operational: tanks
                    )
                );

            var armored =
                new Division(
                    "French Armored",
                    province,
                    fuel: 100,
                    ammunition: 100,
                    type: DivisionType.Armored,
                    country: france,
                    composition:
                        armoredComposition
                );

            var infantry =
                new Division(
                    "German Infantry",
                    province,
                    fuel: 100,
                    ammunition: 100,
                    country: germany
                );

            var simulation =
                new SimulationEngine();

            simulation.AddDivision(
                armored
            );

            simulation.AddDivision(
                infantry
            );

            simulation.DeclareWar(
                france,
                germany
            );

            // Détection.
            simulation.Tick();

            // Premier tick de combat.
            simulation.Tick();

            return infantry.Organization;
        }
    }
