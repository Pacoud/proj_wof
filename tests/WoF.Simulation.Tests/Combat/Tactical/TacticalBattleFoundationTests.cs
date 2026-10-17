
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public class TacticalBattleFoundationTests
{
    private static Division CreateFormation(
        string name,
        Province province,
        Country country)
    {
        var division = new Division(
            name,
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: 200,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "I Battalion", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "Company A", CompanyType.Infantry,
            new CompanyComposition(manpower: 100)));

        battalion.AddCompany(new Company(
            2, "Company B", CompanyType.Infantry,
            new CompanyComposition(manpower: 100)));

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static (
        TacticalBattle Battle,
        Division French,
        Division German,
        BattleSector Forest,
        BattleSector Hill)
        CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Test Battlefield");

        var french = CreateFormation(
            "French Division", province, france);

        var german = CreateFormation(
            "German Division", province, germany);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var system = new EngagementSystem(diplomacy);

        system.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            Assert.Single(system.ActiveEngagements);

        var battlefield = new TacticalBattlefield();

        var forest = new BattleSector(
            1, "Forest", TerrainType.Forest,
            features: SectorFeature.Entrenchments);

        var hill = new BattleSector(
            2, "Hill", TerrainType.Hills,
            elevationMeters: 150,
            features: SectorFeature.Woods);

        battlefield.AddSector(forest);
        battlefield.AddSector(hill);
        battlefield.Connect(forest, hill, 800);

        return (
            new TacticalBattle(engagement, battlefield),
            french, german, forest, hill);
    }

    [Fact]
    public void Battlefield_PreservesTerrainAndConnections()
    {
        var battlefield = new TacticalBattlefield();

        var forest = new BattleSector(
            1, "Forest", TerrainType.Forest,
            features: SectorFeature.Entrenchments);

        var hill = new BattleSector(
            2, "Hill", TerrainType.Hills,
            elevationMeters: 150,
            features: SectorFeature.Woods);

        var village = new BattleSector(
            3, "Village", TerrainType.Urban,
            features: SectorFeature.Buildings | SectorFeature.Road);

        battlefield.AddSector(forest);
        battlefield.AddSector(hill);
        battlefield.AddSector(village);

        battlefield.Connect(forest, hill, 800);
        battlefield.Connect(hill, village, 1200);

        Assert.Equal(3, battlefield.Sectors.Count);
        Assert.Equal(2, battlefield.Connections.Count);

        Assert.True(battlefield.AreConnected(forest, hill));
        Assert.False(battlefield.AreConnected(forest, village));

        Assert.Equal(TerrainType.Hills, hill.DominantTerrain);
        Assert.Equal(150, hill.ElevationMeters);
        Assert.True(hill.Features.HasFlag(SectorFeature.Woods));

        Assert.Contains(
            village,
            battlefield.GetNeighbors(hill));

        Assert.Throws<InvalidOperationException>(() =>
            battlefield.Connect(hill, forest, 800));

        Assert.Throws<InvalidOperationException>(() =>
            battlefield.AddSector(new BattleSector(
                1, "Duplicate", TerrainType.Plains)));

        Assert.Throws<InvalidOperationException>(() =>
            battlefield.Connect(
                forest,
                new BattleSector(3, "Other", TerrainType.Plains),
                100));

        Assert.Equal(2, battlefield.Connections.Count);
    }

    [Fact]
    public void Deployments_KeepRealCompaniesAndResources()
    {
        var (battle, french, german, forest, hill) =
            CreateScenario();

        var frenchCompanies =
            french.Battalions[0].Companies;

        var germanCompany =
            german.Battalions[0].Companies[0];

        var frenchBefore =
            french.GetCurrentResourceTotals();

        var germanBefore =
            german.GetCurrentResourceTotals();

        var front = battle.DeployCompany(
            frenchCompanies[0],
            hill,
            CompanyDeploymentRole.FrontLine);

        var reserve = battle.DeployCompany(
            frenchCompanies[1],
            forest,
            CompanyDeploymentRole.Reserve);

        var enemy = battle.DeployCompany(
            germanCompany,
            hill,
            CompanyDeploymentRole.FrontLine);

        Assert.Equal(3, battle.Deployments.Count);

        Assert.Same(frenchCompanies[0], front.Company);
        Assert.Same(hill, front.Sector);

        Assert.Same(forest, reserve.Sector);
        Assert.Equal(
            CompanyDeploymentRole.Reserve,
            reserve.Role);

        // Deux compagnies de divisions différentes
        // peuvent partager le même ID local.
        Assert.Equal(
            frenchCompanies[0].Id,
            germanCompany.Id);

        Assert.NotSame(front.Company, enemy.Company);

        Assert.Same(
            front,
            battle.FindDeployment(frenchCompanies[0]));

        Assert.Equal(
            2,
            battle.GetBattalionDeployments(
                french.Battalions[0]).Count);

        Assert.Equal(
            frenchBefore,
            french.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            german.GetCurrentResourceTotals());
    }

    [Fact]
    public void InvalidDeployment_DoesNotModifyBattle()
    {
        var (battle, french, _, forest, hill) =
            CreateScenario();

        var company = french.Battalions[0].Companies[0];

        battle.DeployCompany(
            company, forest, CompanyDeploymentRole.FrontLine);

        Assert.Throws<InvalidOperationException>(() =>
            battle.DeployCompany(
                company, hill, CompanyDeploymentRole.Support));

        var outsideSector = new BattleSector(
            1, "Another Forest", TerrainType.Forest);

        Assert.Throws<InvalidOperationException>(() =>
            battle.DeployCompany(
                french.Battalions[0].Companies[1],
                outsideSector,
                CompanyDeploymentRole.Reserve));

        var outsider = CreateFormation(
            "Uninvolved Division",
            new Province(99, "Elsewhere"),
            new Country(3, "Neutral"));

        Assert.Throws<InvalidOperationException>(() =>
            battle.DeployCompany(
                outsider.Battalions[0].Companies[0],
                hill,
                CompanyDeploymentRole.FrontLine));

        Assert.Single(battle.Deployments);
        Assert.Same(
            forest,
            battle.FindDeployment(company)!.Sector);
    }
}
