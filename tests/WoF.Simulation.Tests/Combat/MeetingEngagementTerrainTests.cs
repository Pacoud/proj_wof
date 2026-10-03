using WoF.Simulation.Logistics;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;
using WoF.Simulation.Core;
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Combat.Power;


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
    public void MountainPenalizesArmoredPowerMoreThanSmallArms()
    {
        TerrainCombatModifiers modifiers =
            CombatTerrainModifier
                .GetModifiers(
                    TerrainType.Mountain
                );

        Assert.Equal(
            0.90,
            modifiers.SmallArms,
            2
        );

        Assert.Equal(
            0.75,
            modifiers.Artillery,
            2
        );

        Assert.Equal(
            0.45,
            modifiers.Armored,
            2
        );

        Assert.True(
            modifiers.Armored
            <
            modifiers.SmallArms
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


    [Fact]
    public void MeetingEngagementSynchronizesBothDivisionsAtExactContactPoint()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var a =
            new Province(
                1,
                "A",
                TerrainType.Mountain,
                owner: france
            );

        var b =
            new Province(
                2,
                "B",
                TerrainType.Plains,
                owner: germany
            );

        a.ConnectTo(b);

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

        var frenchPlan =
            new MovementPlan(
                DurationHours: 4,
                FuelPerHour: 0
            );

        var germanPlan =
            new MovementPlan(
                DurationHours: 5,
                FuelPerHour: 0
            );

        Assert.True(
            french.TryStartMovement(
                b,
                frenchPlan
            )
        );

        Assert.True(
            german.TryStartMovement(
                a,
                germanPlan
            )
        );

        // Tick 1
        french.AdvanceOneHour();
        german.AdvanceOneHour();

        // Tick 2
        french.AdvanceOneHour();
        german.AdvanceOneHour();

        // Positions :
        //
        // FR = 0.50 depuis A
        // DE = 0.40 depuis B
        //    = 0.60 depuis A
        //
        // Ils ne se sont pas encore croisés.

        // Tick 3
        //
        // FR : 0.50 -> 0.75
        // DE : 0.60 -> 0.40 dans le référentiel A -> B
        french.AdvanceOneHour();
        german.AdvanceOneHour();

        var diplomacy =
            new DiplomacySystem();

        diplomacy.DeclareWar(
            france,
            germany
        );

        var system =
            new EngagementSystem(
                diplomacy
            );

        system.DetectTransitEngagements(
            new[]
            {
                french,
                german
            },
            currentHour: 3
        );

        var engagement =
            Assert.Single(
                system.ActiveEngagements
            );

        double contact =
            engagement.ConnectionProgress!.Value;

        // Le contact réel vaut environ 0.555555...
        Assert.Equal(
            0.555556,
            contact,
            6
        );

        Assert.NotNull(
            french.Transit
        );

        Assert.NotNull(
            german.Transit
        );

        // France utilise le référentiel A -> B.
        Assert.Equal(
            contact,
            french.Transit!.Progress,
            6
        );

        // Allemagne utilise B -> A.
        Assert.Equal(
            1.0 - contact,
            german.Transit!.Progress,
            6
        );

        Assert.True(
            french.Transit.IsPaused
        );

        Assert.True(
            german.Transit.IsPaused
        );
    }

    [Fact]
    public void TestReverseMethodfractionedposition()
    {
         var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var a =
            new Province(
                1,
                "A",
                TerrainType.Mountain,
                owner: france
            );

        var b =
            new Province(
                2,
                "B",
                TerrainType.Plains,
                owner: germany
            );

        a.ConnectTo(b);

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

        var frenchPlan =
            new MovementPlan(
                DurationHours: 4,
                FuelPerHour: 0
            );

        var germanPlan =
            new MovementPlan(
                DurationHours: 5,
                FuelPerHour: 0
            );
            
        Assert.True(french.TryStartMovement(b, frenchPlan));
        Assert.True(german.TryStartMovement(a, germanPlan));

        for (int hour = 0; hour < 3; hour++)
        {
            french.AdvanceOneHour();
            german.AdvanceOneHour();
        }

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var system = new EngagementSystem(diplomacy);
        system.DetectTransitEngagements(
            new[] { french, german },
            currentHour: 3
        );

        var engagement = Assert.Single(system.ActiveEngagements);
        Assert.NotNull(engagement.ConnectionProgress);
        Assert.NotNull(german.Transit);

        double positionBeforeReverse =
            engagement.ConnectionProgress!.Value;

        Assert.Equal(0.555556, positionBeforeReverse, 6);
        Assert.Equal(1.0 - positionBeforeReverse, german.Transit!.Progress, 6);

        german.Transit.Reverse();

        Assert.Same(a, german.Transit.Origin);
        Assert.Same(b, german.Transit.Destination);
        Assert.Equal(positionBeforeReverse, german.Transit.Progress, 6);
        Assert.Equal(german.Transit.Progress, german.Transit.PreviousProgress, 6);
        Assert.False(german.Transit.IsPaused);
    }



        [Fact]
    public void DivisionReachingProvinceCannotPassThroughEnemyLeavingThatProvince()
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

        a.ConnectTo(
            b
        );

        var french =
            new Division(
                "French",
                a,
                fuel: 100,
                ammunition: 100,
                country: france
            );

        var german =
            new Division(
                "German",
                b,
                fuel: 100,
                ammunition: 100,
                country: germany
            );

        var germanPlan =
            new MovementPlan(
                DurationHours: 10,
                FuelPerHour: 0
            );

        var frenchPlan =
            new MovementPlan(
                DurationHours: 5,
                FuelPerHour: 0
            );

        Assert.True(
            german.TryStartMovement(
                a,
                germanPlan
            )
        );

        // Allemagne atteint 90 % vers A.
        for (int i = 0; i < 9; i++)
        {
            german.AdvanceOneHour();
        }

        Assert.Equal(
            0.90,
            german.Transit!.Progress,
            2
        );

        Assert.True(
            french.TryStartMovement(
                b,
                frenchPlan
            )
        );

        var diplomacy =
            new DiplomacySystem();

        diplomacy.DeclareWar(
            france,
            germany
        );

        // Tick commun.
        german.AdvanceOneHour();
        french.AdvanceOneHour();

        var engagements =
            new EngagementSystem(
                diplomacy
            );

        engagements.DetectTransitEngagements(
            new[]
            {
                french,
                german
            },
            currentHour: 10
        );

        var engagement =
            Assert.Single(
                engagements.ActiveEngagements
            );

        Assert.Equal(
            EngagementType.MeetingEngagement,
            engagement.Type
        );

        Assert.True(
            french.IsEngaged
        );

        Assert.True(
            german.IsEngaged
        );

        Assert.NotNull(
            french.Transit
        );

        Assert.NotNull(
            german.Transit
        );

        Assert.False(
            french.Transit!.IsCompleted
        );

        Assert.False(
            german.Transit!.IsCompleted
        );
    }
}
