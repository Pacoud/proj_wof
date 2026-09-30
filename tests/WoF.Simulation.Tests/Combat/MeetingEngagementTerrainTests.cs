using WoF.Simulation.Logistics;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;
using WoF.Simulation.Core;
using WoF.Simulation.Diplomacy;


namespace WoF.Simulation.Tests.Combat;

public class MeetingEngagementTerrainTests{

    [Fact]
    public void MeetingEngagementAtSixtyPercentUsesDestinationTerrain()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var mountain =
            new Province(
                1,
                "Mountain A",
                TerrainType.Mountain,
                owner: france
            );

        var plains =
            new Province(
                2,
                "Plains B",
                TerrainType.Plains,
                owner: germany
            );

        mountain.ConnectTo(
            plains
        );

        var french =
            new Division(
                "French Division",
                mountain,
                100,
                100,
                country: france
            );

        var german =
            new Division(
                "German Division",
                plains,
                100,
                100,
                country: germany
            );

        var plan =
            new MovementPlan(
                DurationHours: 5,
                FuelPerHour: 0
            );

        Assert.True(
            french.TryStartMovement(
                plains,
                plan
            )
        );

        // France arrive à 40 %.
        french.AdvanceOneHour();
        french.AdvanceOneHour();

        Assert.Equal(
            0.40,
            french.Transit!.Progress,
            2
        );

        Assert.True(
            german.TryStartMovement(
                mountain,
                plan
            )
        );

        // Allemagne arrive à 20 % depuis B.
        german.AdvanceOneHour();

        // Tick commun suivant :
        // France : 40 -> 60 %
        // Allemagne : 20 -> 40 % depuis B,
        // soit 80 -> 60 % depuis A.
        french.AdvanceOneHour();
        german.AdvanceOneHour();

        var diplomacy =
            new DiplomacySystem();

        diplomacy.DeclareWar(
            france,
            germany
        );

        var engagementSystem =
            new EngagementSystem(
                diplomacy
            );

        engagementSystem
            .DetectTransitEngagements(
                new[]
                {
                    french,
                    german
                },
                currentHour: 3
            );

        var engagement =
            Assert.Single(
                engagementSystem
                    .ActiveEngagements
            );

        Assert.Equal(
            EngagementType.MeetingEngagement,
            engagement.Type
        );

        Assert.NotNull(
            engagement.ConnectionProgress
        );

        Assert.Equal(
            0.60,
            engagement.ConnectionProgress!.Value,
            2
        );

        Assert.Equal(
            TerrainType.Plains,
            engagement.BattleTerrain
        );
    }


    [Fact]
    public void ProvinceBattleUsesProvinceTerrain()
    {
        var province =
            new Province(
                1,
                "Alpine Province",
                TerrainType.Mountain
            );

        var engagement =
            new Engagement(
                id: 1,
                startHour: 0,
                province: province
            );

        Assert.Equal(
            EngagementType.ProvinceBattle,
            engagement.Type
        );

        Assert.Equal(
            TerrainType.Mountain,
            engagement.BattleTerrain
        );
    }

    [Fact]
    public void ArmoredDivisionIsMorePenalizedByMountainThanInfantry()
    {
        double infantryModifier =
            CombatTerrainModifier
                .GetPressureModifier(
                    DivisionType.Infantry,
                    TerrainType.Mountain
                );

        double armoredModifier =
            CombatTerrainModifier
                .GetPressureModifier(
                    DivisionType.Armored,
                    TerrainType.Mountain
                );

        Assert.Equal(
            0.90,
            infantryModifier
        );

        Assert.Equal(
            0.45,
            armoredModifier
        );

        Assert.True(
            armoredModifier
            <
            infantryModifier
        );
    }

    private static double RunArmoredVsInfantryCombat(
    TerrainType terrain)
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var province =
            new Province(
                1,
                "Battlefield",
                terrain,
                owner: france
            );

        var armored =
            new Division(
                "French Armored",
                province,
                100,
                100,
                type: DivisionType.Armored,
                country: france
            );

        var infantry =
            new Division(
                "German Infantry",
                province,
                100,
                100,
                type: DivisionType.Infantry,
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

        // Détection de l'engagement.
        simulation.Tick();

        // Premier vrai tick de combat.
        simulation.Tick();

        return infantry.Organization;
    }


    [Fact]
    public void ArmoredDivisionAppliesLessPressureInMountainThanPlains()
    {
        double infantryOrganizationAfterPlains =
            RunArmoredVsInfantryCombat(
                TerrainType.Plains
            );

        double infantryOrganizationAfterMountain =
            RunArmoredVsInfantryCombat(
                TerrainType.Mountain
            );

        Assert.True(
            infantryOrganizationAfterPlains
            <
            infantryOrganizationAfterMountain
        );
    }
}