
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalSectorPresenceTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        BattleSector S1,
        BattleSector S2,
        BattleSector S3,
        BattleSector S4,
        CompanyDeployment FrenchA,
        CompanyDeployment FrenchB,
        CompanyDeployment German,
        Division FrenchDivision,
        Division GermanDivision);

    private static Division MakeDivision(
        Country country,
        Province province,
        int battalionCount)
    {
        var division = new Division(
            name: $"{country.Name} Division",
            position: province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: battalionCount * 100,
                infantryEquipment: battalionCount * 90,
                artillery: 0,
                tanks: 0));

        for (int i = 1; i <= battalionCount; i++)
        {
            var battalion = new Battalion(
                i, $"Battalion {i}", BattalionType.Infantry);

            battalion.AddCompany(new Company(
                1,
                $"Company {i}",
                CompanyType.Infantry,
                new CompanyComposition(
                    manpower: 100,
                    infantryEquipment: 90,
                    ammunition: new AmmunitionTotals(
                        smallArms: 800))));

            division.AddBattalion(battalion);
        }

        division.EnableHierarchicalResources();
        return division;
    }

    private static Scenario CreateScenario(
        bool positionSecondCompany = true)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var french = MakeDivision(france, province, 2);
        var german = MakeDivision(germany, province, 1);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var battlefield = new TacticalBattlefield();

        var s1 = new BattleSector(
            1, "S1", TerrainType.Plains);
        var s2 = new BattleSector(
            2, "S2", TerrainType.Plains);
        var s3 = new BattleSector(
            3, "S3", TerrainType.Plains);
        var s4 = new BattleSector(
            4, "S4", TerrainType.Plains);

        battlefield.AddSector(s1);
        battlefield.AddSector(s2);
        battlefield.AddSector(s3);
        battlefield.AddSector(s4);

        battlefield.Connect(s1, s2, 200);
        battlefield.Connect(s2, s3, 200);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var frenchA = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            s2,
            CompanyDeploymentRole.FrontLine);

        var frenchB = battle.DeployCompany(
            french.Battalions[1].Companies[0],
            s2,
            CompanyDeploymentRole.Support);

        var germanA = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            s2,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(s1, 0, 0, 100);
        geometry.DefineSector(s2, 200, 0, 100);
        geometry.DefineSector(s3, 400, 0, 100);

        // S4 a une enveloppe qui chevauche S2,
        // mais n'est pas connecte dans le graphe.
        geometry.DefineSector(s4, 200, 0, 100);

        geometry.PlaceCompany(
            frenchA,
            new TacticalPoint(200, 0, 100),
            facingDegrees: 0,
            frontageMeters: 300);

        if (positionSecondCompany)
        {
            geometry.PlaceCompany(
                frenchB,
                new TacticalPoint(200, 15, 100),
                facingDegrees: 0,
                frontageMeters: 40);
        }

        geometry.PlaceCompany(
            germanA,
            new TacticalPoint(200, -15, 100),
            facingDegrees: 180,
            frontageMeters: 50);

        return new Scenario(
            battle, geometry,
            s1, s2, s3, s4,
            frenchA, frenchB, germanA,
            french, german);
    }

    [Fact]
    public void WideCompany_CoversThreeSectorsWithoutDuplication()
    {
        var s = CreateScenario();

        var index = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        Assert.Empty(index.GetAssigned(s.S1));
        Assert.Empty(index.GetAssigned(s.S3));

        Assert.Equal(3, index.GetAssigned(s.S2).Count);

        Assert.Same(
            s.FrenchA,
            Assert.Single(index.GetCovering(s.S1)));

        Assert.Same(
            s.FrenchA,
            Assert.Single(index.GetCovering(s.S3)));

        Assert.Equal(3, index.GetCovering(s.S2).Count);

        // S4 ne constitue pas un voisin valide.
        Assert.Empty(index.GetCovering(s.S4));
    }

    [Fact]
    public void CompaniesFromDifferentBattalions_ShareSector()
    {
        var s = CreateScenario();

        var index = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        var assigned = index.GetAssigned(s.S2);

        Assert.Contains(s.FrenchA, assigned);
        Assert.Contains(s.FrenchB, assigned);
        Assert.Contains(s.German, assigned);

        Assert.NotSame(
            s.FrenchA.Company.ParentBattalion,
            s.FrenchB.Company.ParentBattalion);

        Assert.Same(
            s.FrenchDivision,
            s.FrenchA.Company.ParentBattalion!
                .ParentDivision);

        Assert.Same(
            s.FrenchDivision,
            s.FrenchB.Company.ParentBattalion!
                .ParentDivision);

        Assert.Same(
            s.GermanDivision,
            s.German.Company.ParentBattalion!
                .ParentDivision);
    }

    [Fact]
    public void Footprint_UsesFacingAndFrontage()
    {
        var s = CreateScenario();

        var index = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        var footprint = index.GetFootprint(s.FrenchA);

        Assert.True(footprint.HasValue);

        var value = footprint.Value;

        Assert.Same(s.FrenchA, value.Deployment);
        Assert.Equal(300, value.FrontageMeters);

        Assert.Equal(50, value.LeftEnd.XMeters, 6);
        Assert.Equal(350, value.RightEnd.XMeters, 6);

        Assert.Equal(0, value.LeftEnd.YMeters, 6);
        Assert.Equal(0, value.RightEnd.YMeters, 6);
    }

    [Fact]
    public void UnpositionedCompany_IsAssignedButNotCovering()
    {
        var s = CreateScenario(
            positionSecondCompany: false);

        var index = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        Assert.Equal(3, index.GetAssigned(s.S2).Count);
        Assert.Equal(2, index.GetCovering(s.S2).Count);

        Assert.Null(index.GetFootprint(s.FrenchB));
    }

    [Fact]
    public void GeometryChange_RequiresIndexRebuild()
    {
        var s = CreateScenario(
            positionSecondCompany: false);

        var oldIndex = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        Assert.Equal(2, oldIndex.GetCoverageCount(s.S2));

        s.Geometry.PlaceCompany(
            s.FrenchB,
            new TacticalPoint(200, 15, 100),
            facingDegrees: 0,
            frontageMeters: 40);

        Assert.Throws<InvalidOperationException>(() =>
            oldIndex.GetCovering(s.S2));

        var newIndex = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        Assert.Equal(3, newIndex.GetCoverageCount(s.S2));
    }

    [Fact]
    public void ForeignGeometryOrSector_IsRejected()
    {
        var s = CreateScenario();
        var other = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            new TacticalSectorPresenceIndex(
                s.Battle,
                other.Geometry));

        var index = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        Assert.Throws<InvalidOperationException>(() =>
            index.GetAssigned(other.S2));

        Assert.Throws<InvalidOperationException>(() =>
            index.GetFootprint(other.FrenchA));
    }

    [Fact]
    public void Index_DoesNotDuplicatePersonnelOrAmmunition()
    {
        var s = CreateScenario();

        var before =
            s.FrenchDivision.GetCurrentResourceTotals();

        var index = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        // La meme compagnie couvre trois secteurs,
        // sans devenir trois objets militaires.
        Assert.Same(
            s.FrenchA,
            Assert.Single(index.GetCovering(s.S1)));

        Assert.Same(
            s.FrenchA,
            Assert.Single(index.GetCovering(s.S3)));

        Assert.Equal(
            before,
            s.FrenchDivision.GetCurrentResourceTotals());

        Assert.Equal(200, before.Personnel.Available);
        Assert.Equal(1600, before.Ammunition.SmallArms);

        Assert.True(
            s.FrenchA.Company.Composition!.Ammunition
                .TryConsume(AmmunitionType.SmallArms, 100));

        var after =
            s.FrenchDivision.GetCurrentResourceTotals();

        Assert.Equal(200, after.Personnel.Available);
        Assert.Equal(1500, after.Ammunition.SmallArms);

        // L'index reference toujours la meme compagnie.
        Assert.Same(
            s.FrenchA.Company,
            Assert.Single(index.GetCovering(s.S1)).Company);
    }

    [Fact]
    public void EmptySector_RemainsQueryable()
    {
        var s = CreateScenario();

        var index = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        Assert.Empty(index.GetAssigned(s.S4));
        Assert.Empty(index.GetCovering(s.S4));
        Assert.Equal(0, index.GetCoverageCount(s.S4));
    }
}
