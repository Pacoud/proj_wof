
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalContactDetectorTests
{
    private static Division MakeDivision(
        string name,
        Country country,
        Province province)
    {
        var division = new Division(
            name,
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: 400,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Bataillon 1", BattalionType.Infantry);

        for (int i = 1; i <= 4; i++)
        {
            battalion.AddCompany(new Company(
                i,
                $"Compagnie {i}",
                CompanyType.Infantry,
                new CompanyComposition(manpower: 100)));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static (
        TacticalBattle Battle,
        DiplomacySystem Diplomacy,
        Division French,
        Division German,
        BattleSector Hill,
        BattleSector Forest,
        BattleSector Village)
        CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(
            1, "Province test", TerrainType.Hills);

        var french = MakeDivision(
            "Division française", france, province);

        var german = MakeDivision(
            "Division allemande", germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var system = new EngagementSystem(diplomacy);

        system.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            Assert.Single(system.ActiveEngagements);

        var field = new TacticalBattlefield();

        var hill = new BattleSector(
            1, "Colline", TerrainType.Hills);

        var forest = new BattleSector(
            2, "Forêt", TerrainType.Forest);

        var village = new BattleSector(
            3, "Village", TerrainType.Urban);

        field.AddSector(hill);
        field.AddSector(forest);
        field.AddSector(village);

        field.Connect(hill, forest, 800);
        field.Connect(forest, village, 600);

        return (
            new TacticalBattle(engagement, field),
            diplomacy,
            french,
            german,
            hill,
            forest,
            village);
    }

    private static Company CompanyAt(
        Division division,
        int index)
    {
        return division.Battalions[0].Companies[index];
    }

    [Fact]
    public void SameSector_DetectsOnePotentialContact()
    {
        var (battle, diplomacy, french, german,
            hill, _, _) = CreateScenario();

        var frenchDeployment = battle.DeployCompany(
            CompanyAt(french, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        var germanDeployment = battle.DeployCompany(
            CompanyAt(german, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        var contacts =
            TacticalContactDetector.Detect(battle, diplomacy);

        var contact = Assert.Single(contacts);

        Assert.Same(frenchDeployment, contact.First);
        Assert.Same(germanDeployment, contact.Second);

        Assert.Equal(
            TacticalContactTopology.SameSector,
            contact.Topology);

        Assert.Null(contact.ConnectionDistanceMeters);
    }

    [Fact]
    public void ConnectedSectors_CreatePotentialContact()
    {
        var (battle, diplomacy, french, german,
            hill, forest, _) = CreateScenario();

        battle.DeployCompany(
            CompanyAt(french, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            CompanyAt(german, 0),
            forest,
            CompanyDeploymentRole.FrontLine);

        var contact = Assert.Single(
            TacticalContactDetector.Detect(
                battle, diplomacy));

        Assert.Equal(
            TacticalContactTopology.ConnectedSectors,
            contact.Topology);

        Assert.Equal(
            800.0,
            contact.ConnectionDistanceMeters!.Value);
    }

    [Fact]
    public void NonAdjacentSectors_DoNotCreateContact()
    {
        var (battle, diplomacy, french, german,
            hill, _, village) = CreateScenario();

        battle.DeployCompany(
            CompanyAt(french, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            CompanyAt(german, 0),
            village,
            CompanyDeploymentRole.FrontLine);

        // Colline -> Forêt -> Village
        // Il n'existe pas de liaison directe
        // entre la colline et le village.
        Assert.Empty(
            TacticalContactDetector.Detect(
                battle, diplomacy));
    }

    [Fact]
    public void SupportsAndReserves_AreNotDirectContacts()
    {
        var (battle, diplomacy, french, german,
            hill, _, _) = CreateScenario();

        battle.DeployCompany(
            CompanyAt(french, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            CompanyAt(german, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            CompanyAt(french, 1),
            hill,
            CompanyDeploymentRole.Support);

        battle.DeployCompany(
            CompanyAt(german, 1),
            hill,
            CompanyDeploymentRole.Support);

        battle.DeployCompany(
            CompanyAt(french, 2),
            hill,
            CompanyDeploymentRole.Reserve);

        battle.DeployCompany(
            CompanyAt(german, 2),
            hill,
            CompanyDeploymentRole.Reserve);

        // Seules les deux compagnies
        // en première ligne sont candidates.
        Assert.Single(
            TacticalContactDetector.Detect(
                battle, diplomacy));
    }

    [Fact]
    public void MultipleFrontlines_AreDetectedWithoutDuplicates()
    {
        var (battle, diplomacy, french, german,
            hill, forest, _) = CreateScenario();

        battle.DeployCompany(
            CompanyAt(french, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            CompanyAt(french, 1),
            forest,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            CompanyAt(german, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            CompanyAt(german, 1),
            forest,
            CompanyDeploymentRole.FrontLine);

        var contacts =
            TacticalContactDetector.Detect(battle, diplomacy);

        // Deux contacts dans les secteurs,
        // deux contacts entre secteurs.
        Assert.Equal(4, contacts.Count);

        Assert.Equal(2, contacts.Count(c =>
            c.Topology ==
            TacticalContactTopology.SameSector));

        Assert.Equal(2, contacts.Count(c =>
            c.Topology ==
            TacticalContactTopology.ConnectedSectors));

        var pairs = contacts
            .Select(c => (c.First, c.Second))
            .ToArray();

        Assert.Equal(
            pairs.Length,
            pairs.Distinct().Count());
    }

    [Fact]
    public void Peace_RemovesHostileContactCandidates()
    {
        var (battle, diplomacy, french, german,
            hill, _, _) = CreateScenario();

        battle.DeployCompany(
            CompanyAt(french, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            CompanyAt(german, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        Assert.Single(
            TacticalContactDetector.Detect(
                battle, diplomacy));

        Assert.True(diplomacy.MakePeace(
            french.Country!,
            german.Country!));

        Assert.Empty(
            TacticalContactDetector.Detect(
                battle, diplomacy));
    }

    [Fact]
    public void CompanyWithoutPersonnel_CannotCreateContact()
    {
        var (battle, diplomacy, french, german,
            hill, _, _) = CreateScenario();

        battle.DeployCompany(
            CompanyAt(french, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        var germanCompany = CompanyAt(german, 0);

        battle.DeployCompany(
            germanCompany,
            hill,
            CompanyDeploymentRole.FrontLine);

        germanCompany.Composition!.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 100,
                WoundedInAction: 0,
                MissingOrCaptured: 0));

        Assert.Empty(
            TacticalContactDetector.Detect(
                battle, diplomacy));
    }

    [Fact]
    public void Detection_DoesNotModifyMilitaryResources()
    {
        var (battle, diplomacy, french, german,
            hill, _, _) = CreateScenario();

        battle.DeployCompany(
            CompanyAt(french, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            CompanyAt(german, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        var frenchBefore = french.GetCurrentResourceTotals();
        var germanBefore = german.GetCurrentResourceTotals();

        var deploymentsBefore = battle.Deployments.Count;

        var first = TacticalContactDetector.Detect(
            battle, diplomacy);

        var second = TacticalContactDetector.Detect(
            battle, diplomacy);

        Assert.Equal(first.ToArray(), second.ToArray());

        Assert.Equal(
            frenchBefore,
            french.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            german.GetCurrentResourceTotals());

        Assert.Equal(
            deploymentsBefore,
            battle.Deployments.Count);
    }
}
