
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalDeploymentMembershipTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        CompanyDeployment Observer,
        CompanyDeployment Target,
        BattleSector Sector);

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
                manpower: 100,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Bataillon", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1,
            "Compagnie",
            CompanyType.Infantry,
            new CompanyComposition(manpower: 100)));

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Allemagne");

        var province = new Province(1, "Test");

        var french = MakeDivision(
            "Division francaise", france, province);

        var german = MakeDivision(
            "Division allemande", germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var battlefield = new TacticalBattlefield();

        var sector = new BattleSector(
            1, "Plaine", TerrainType.Plains);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var observer = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var target = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        return new Scenario(
            battle, observer, target, sector);
    }

    private static CompanyDeployment CreateUnregisteredCopy(
        Scenario scenario)
    {
        // Meme compagnie reelle, mais autre objet
        // de deploiement, jamais inscrit a la bataille.
        return new CompanyDeployment(
            scenario.Observer.Company,
            scenario.Sector,
            CompanyDeploymentRole.FrontLine);
    }

    private static TacticalObservationEvent Suspected(
        Scenario scenario,
        CompanyDeployment target)
    {
        return new TacticalObservationEvent(
            scenario.Observer,
            target,
            ObservedPosition: null,
            ObservedSectorId: scenario.Sector.Id,
            KnowledgeLevel: TacticalKnowledgeLevel.Suspected,
            PositionUncertaintyMeters: null);
    }

    [Fact]
    public void Membership_RequiresExactRegisteredDeployment()
    {
        var scenario = CreateScenario();
        var other = CreateScenario();

        Assert.True(
            scenario.Battle.ContainsDeployment(
                scenario.Observer));

        Assert.True(
            scenario.Battle.ContainsDeployment(
                scenario.Target));

        Assert.False(
            scenario.Battle.ContainsDeployment(
                other.Observer));

        Assert.False(
            scenario.Battle.ContainsDeployment(
                CreateUnregisteredCopy(scenario)));

        Assert.Equal(2, scenario.Battle.Deployments.Count);
    }

    [Fact]
    public void Signatures_RejectUnregisteredDeployments()
    {
        var scenario = CreateScenario();
        var other = CreateScenario();

        var frame = new TacticalSignatureFrame(
            scenario.Battle);

        var signature = new TacticalUnitSignature(
            TacticalActivity.Stationary,
            TacticalConcealment.Partial);

        frame.Set(scenario.Observer, signature);

        Assert.True(
            frame.TryGet(
                scenario.Observer, out var stored));

        Assert.Same(signature, stored);

        Assert.Throws<InvalidOperationException>(() =>
            frame.Set(other.Observer, signature));

        Assert.Throws<InvalidOperationException>(() =>
        {
            frame.TryGet(other.Observer, out _);
        });

        Assert.Throws<InvalidOperationException>(() =>
            frame.Set(
                CreateUnregisteredCopy(scenario),
                signature));

        // Une affectation refusee ne remplace pas
        // la signature de l'unite valide.
        Assert.True(
            frame.TryGet(
                scenario.Observer, out stored));

        Assert.Same(signature, stored);
    }

    [Fact]
    public void Perception_RejectsForeignDeploymentWithoutMutation()
    {
        var scenario = CreateScenario();
        var other = CreateScenario();

        var perception = new TacticalPerceptionState(
            scenario.Battle);

        perception.Apply(1, new[]
        {
            Suspected(scenario, scenario.Target)
        });

        var original = Assert.Single(
            perception.GetReports(scenario.Observer));

        Assert.Throws<InvalidOperationException>(() =>
            perception.GetReports(other.Observer));

        Assert.Throws<InvalidOperationException>(() =>
            perception.GetReports(
                CreateUnregisteredCopy(scenario)));

        // La premiere observation est valide.
        // La deuxieme vient d'une bataille etrangere.
        // Toute la passe doit etre rejetee.
        Assert.Throws<InvalidOperationException>(() =>
            perception.Apply(2, new[]
            {
                Suspected(scenario, scenario.Target),
                Suspected(scenario, other.Target)
            }));

        var after = Assert.Single(
            perception.GetReports(scenario.Observer));

        Assert.Equal(original, after);
        Assert.Equal(1L, after.LastObservedHour);
        Assert.True(after.IsCurrentlyObserved);
    }

    [Fact]
    public void CommunicationNetwork_RejectsForeignDeployments()
    {
        var scenario = CreateScenario();
        var other = CreateScenario();

        using var network =
            new TacticalCommunicationNetwork(
                scenario.Battle);

        var firstNode =
            network.ForCompany(scenario.Observer);

        Assert.Same(
            firstNode,
            network.ForCompany(scenario.Observer));

        Assert.Throws<InvalidOperationException>(() =>
            network.ForCompany(other.Observer));

        Assert.Throws<InvalidOperationException>(() =>
            network.ForCompany(
                CreateUnregisteredCopy(scenario)));

        Assert.Same(
            firstNode,
            network.ForCompany(scenario.Observer));
    }
}
