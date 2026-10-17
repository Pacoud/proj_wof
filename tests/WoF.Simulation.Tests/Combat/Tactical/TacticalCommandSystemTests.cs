
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public class TacticalCommandSystemTests
{
    private static Division CreateDivision(
        string name,
        Province province,
        Country country,
        int battalionCount)
    {
        var division = new Division(
            name,
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: battalionCount * 400,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        for (int b = 1; b <= battalionCount; b++)
        {
            var battalion = new Battalion(
                b,
                $"Battalion {b}",
                BattalionType.Infantry);

            for (int c = 1; c <= 4; c++)
            {
                battalion.AddCompany(new Company(
                    c,
                    $"Company {c}",
                    CompanyType.Infantry,
                    new CompanyComposition(manpower: 100)));
            }

            division.AddBattalion(battalion);
        }

        division.EnableHierarchicalResources();
        return division;
    }

    private static (
        TacticalBattle Battle,
        Division French,
        Division German,
        BattleSector Hill,
        BattleSector Forest,
        BattleSector Village,
        BattleSector Rear)
        CreateScenario(int frenchBattalions = 5)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(1, "Battle Province");

        var french = CreateDivision(
            "French Division", province,
            france, frenchBattalions);

        var german = CreateDivision(
            "German Division", province,
            germany, 1);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var field = new TacticalBattlefield();

        var hill = new BattleSector(
            1, "Hill", TerrainType.Hills,
            elevationMeters: 150);

        var forest = new BattleSector(
            2, "Forest", TerrainType.Forest,
            features: SectorFeature.Woods);

        var village = new BattleSector(
            3, "Village", TerrainType.Urban,
            features: SectorFeature.Buildings);

        var rear = new BattleSector(
            4, "Rear", TerrainType.Plains,
            features: SectorFeature.Road);

        field.AddSector(hill);
        field.AddSector(forest);
        field.AddSector(village);
        field.AddSector(rear);

        field.Connect(hill, forest, 800);
        field.Connect(hill, village, 1200);
        field.Connect(forest, rear, 600);

        return (
            new TacticalBattle(engagement, field),
            french,
            german,
            hill,
            forest,
            village,
            rear);
    }

    [Fact]
    public void DefenseOrder_DeploysBattalionsByPriorityAndReserve()
    {
        var (battle, french, _, hill, forest, village, rear) =
            CreateScenario();

        var before = french.GetCurrentResourceTotals();

        var order = new DivisionTacticalOrder(
            division: french,
            posture: TacticalPosture.Defend,
            prioritySector: hill,
            secondarySectors: new[] { forest, village },
            reserveSector: rear,
            reserveBattalions: 1);

        var plan = TacticalCommandSystem.CreatePlan(
            battle, order);

        // La préparation ne change rien.
        Assert.Empty(battle.Deployments);
        Assert.Equal(20, plan.Assignments.Count);

        var expectedSectors = new[]
        {
            hill,
            forest,
            village,
            hill,
            rear
        };

        for (int i = 0; i < french.Battalions.Count; i++)
        {
            var battalion = french.Battalions[i];

            var assignments = plan.Assignments
                .Where(a => ReferenceEquals(
                    a.Company.ParentBattalion,
                    battalion))
                .ToArray();

            Assert.Equal(4, assignments.Length);

            Assert.All(assignments, a =>
                Assert.Same(expectedSectors[i], a.Sector));

            if (i == 4)
            {
                Assert.All(assignments, a =>
                    Assert.Equal(
                        CompanyDeploymentRole.Reserve,
                        a.Role));
            }
            else
            {
                Assert.Equal(
                    new[]
                    {
                        CompanyDeploymentRole.FrontLine,
                        CompanyDeploymentRole.Support,
                        CompanyDeploymentRole.Reserve,
                        CompanyDeploymentRole.Reserve
                    },
                    assignments.Select(a => a.Role));
            }
        }

        var created = TacticalCommandSystem.ApplyPlan(plan);

        Assert.Equal(20, created.Count);
        Assert.Equal(20, battle.Deployments.Count);

        Assert.Equal(
            before,
            french.GetCurrentResourceTotals());

        foreach (var assignment in plan.Assignments)
        {
            var deployment =
                battle.FindDeployment(assignment.Company);

            Assert.NotNull(deployment);
            Assert.Same(assignment.Sector, deployment.Sector);
            Assert.Equal(assignment.Role, deployment.Role);
        }
    }

    [Fact]
    public void AttackPosture_AssignsMoreFrontlineCompanies()
    {
        var (battle, french, _, hill, _, _, rear) =
            CreateScenario(frenchBattalions: 1);

        var order = new DivisionTacticalOrder(
            french,
            TacticalPosture.Attack,
            hill,
            Array.Empty<BattleSector>(),
            rear,
            reserveBattalions: 0);

        var first = TacticalCommandSystem.CreatePlan(
            battle, order);

        var second = TacticalCommandSystem.CreatePlan(
            battle, order);

        // Même ordre, même résultat.
        Assert.Equal(
            first.Assignments,
            second.Assignments);

        Assert.Equal(
            new[]
            {
                CompanyDeploymentRole.FrontLine,
                CompanyDeploymentRole.FrontLine,
                CompanyDeploymentRole.Support,
                CompanyDeploymentRole.Reserve
            },
            first.Assignments.Select(a => a.Role));

        TacticalCommandSystem.ApplyPlan(first);

        Assert.Equal(4, battle.Deployments.Count);
    }

    [Fact]
    public void InvalidOrders_DoNotDeployAnyCompany()
    {
        var (battle, french, _, hill, _, _, rear) =
            CreateScenario();

        // Même ID, mais objet secteur étranger.
        var foreignSector = new BattleSector(
            1, "Foreign Hill", TerrainType.Hills);

        var foreignOrder = new DivisionTacticalOrder(
            french,
            TacticalPosture.Defend,
            foreignSector,
            Array.Empty<BattleSector>(),
            rear,
            reserveBattalions: 1);

        Assert.Throws<InvalidOperationException>(() =>
            TacticalCommandSystem.CreatePlan(
                battle, foreignOrder));

        // Même secteur demandé deux fois.
        var duplicatedOrder = new DivisionTacticalOrder(
            french,
            TacticalPosture.Defend,
            hill,
            new[] { hill },
            rear,
            reserveBattalions: 1);

        Assert.Throws<InvalidOperationException>(() =>
            TacticalCommandSystem.CreatePlan(
                battle, duplicatedOrder));

        // Impossible de placer tous les bataillons
        // en réserve dans cette première politique.
        var allReserveOrder = new DivisionTacticalOrder(
            french,
            TacticalPosture.Defend,
            hill,
            Array.Empty<BattleSector>(),
            rear,
            reserveBattalions: 5);

        Assert.Throws<InvalidOperationException>(() =>
            TacticalCommandSystem.CreatePlan(
                battle, allReserveOrder));

        Assert.Empty(battle.Deployments);
    }

    [Fact]
    public void StalePlan_IsRejectedBeforePartialDeployment()
    {
        var (battle, french, _, hill, forest, _, rear) =
            CreateScenario();

        var order = new DivisionTacticalOrder(
            french,
            TacticalPosture.Defend,
            hill,
            new[] { forest },
            rear,
            reserveBattalions: 1);

        var plan = TacticalCommandSystem.CreatePlan(
            battle, order);

        // Un autre déploiement intervient après
        // la création du plan.
        var first = plan.Assignments[0];

        battle.DeployCompany(
            first.Company,
            first.Sector,
            first.Role);

        Assert.Throws<InvalidOperationException>(() =>
            TacticalCommandSystem.ApplyPlan(plan));

        // Aucune autre compagnie ne doit avoir été
        // partiellement déployée.
        Assert.Single(battle.Deployments);

        Assert.Null(
            battle.FindDeployment(
                french.Battalions[1].Companies[0]));
    }
}
