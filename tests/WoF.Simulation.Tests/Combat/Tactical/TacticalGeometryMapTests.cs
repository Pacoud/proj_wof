
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalGeometryMapTests
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
                manpower: 200,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Bataillon 1", BattalionType.Infantry);

        for (int i = 1; i <= 2; i++)
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
        Division French,
        Division German,
        BattleSector Hill,
        BattleSector Forest)
        CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(
            1, "Champ de bataille",
            TerrainType.Hills);

        var french = MakeDivision(
            "Division française", france, province);

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

        var field = new TacticalBattlefield();

        var hill = new BattleSector(
            1, "Colline", TerrainType.Hills,
            elevationMeters: 150);

        var forest = new BattleSector(
            2, "Forêt", TerrainType.Forest,
            elevationMeters: 90);

        field.AddSector(hill);
        field.AddSector(forest);
        field.Connect(hill, forest, 700);

        return (
            new TacticalBattle(engagement, field),
            french,
            german,
            hill,
            forest);
    }

    private static Company CompanyAt(
        Division division,
        int index = 0)
    {
        return division.Battalions[0].Companies[index];
    }

    [Fact]
    public void InvalidCoordinatesAndSectorGeometry_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalPoint(double.NaN, 0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalPoint(0, double.PositiveInfinity, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalPoint(0, 0, double.NaN));

        var (battle, _, _, hill, _) = CreateScenario();
        var geometry = new TacticalGeometryMap(battle);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            geometry.DefineSector(hill, 0, 0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            geometry.DefineSector(
                hill, 0, 0, double.PositiveInfinity));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            geometry.DefineSector(
                hill, double.NaN, 0, 100));

        var sector = geometry.DefineSector(
            hill, 0, 0, 100);

        Assert.True(sector.Contains(
            new TacticalPoint(30, 40, 150)));

        Assert.False(sector.Contains(
            new TacticalPoint(101, 0, 150)));

        Assert.Same(
            sector,
            geometry.GetSectorGeometry(hill));

        Assert.Throws<InvalidOperationException>(() =>
            geometry.DefineSector(hill, 0, 0, 100));

        // Même identifiant, mais objet étranger.
        Assert.Throws<InvalidOperationException>(() =>
            geometry.DefineSector(
                new BattleSector(
                    1, "Autre colline", TerrainType.Hills),
                0, 0, 100));

        Assert.Single(geometry.SectorGeometries);
    }

    [Fact]
    public void CompanyMustBeInsideItsSector_AndPositionIsUnique()
    {
        var (battle, french, _, hill, _) =
            CreateScenario();

        var deployment = battle.DeployCompany(
            CompanyAt(french),
            hill,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(
            hill, 0, 0, radiusMeters: 100);

        Assert.Throws<InvalidOperationException>(() =>
            geometry.PlaceCompany(
                deployment,
                new TacticalPoint(150, 0, 150),
                facingDegrees: 90,
                frontageMeters: 50));

        Assert.Empty(geometry.CompanyPositions);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            geometry.PlaceCompany(
                deployment,
                new TacticalPoint(20, 0, 150),
                facingDegrees: 360,
                frontageMeters: 50));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            geometry.PlaceCompany(
                deployment,
                new TacticalPoint(20, 0, 150),
                facingDegrees: 90,
                frontageMeters: -10));

        var position = geometry.PlaceCompany(
            deployment,
            new TacticalPoint(20, 0, 150),
            facingDegrees: 90,
            frontageMeters: 50);

        Assert.Same(deployment, position.Deployment);
        Assert.Equal(90, position.FacingDegrees);
        Assert.Equal(50, position.FrontageMeters);

        Assert.Throws<InvalidOperationException>(() =>
            geometry.PlaceCompany(
                deployment,
                new TacticalPoint(30, 0, 150),
                facingDegrees: 180,
                frontageMeters: 50));

        Assert.Single(geometry.CompanyPositions);
        Assert.Same(
            position,
            geometry.GetPosition(deployment));
    }

    [Fact]
    public void UnknownSectorAndForeignDeployment_AreRejected()
    {
        var (battle, french, _, hill, forest) =
            CreateScenario();

        var hillDeployment = battle.DeployCompany(
            CompanyAt(french, 0),
            hill,
            CompanyDeploymentRole.FrontLine);

        var forestDeployment = battle.DeployCompany(
            CompanyAt(french, 1),
            forest,
            CompanyDeploymentRole.Reserve);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(hill, 0, 0, 100);

        // La forêt existe dans la bataille,
        // mais sa géométrie n'est pas encore définie.
        Assert.Throws<InvalidOperationException>(() =>
            geometry.PlaceCompany(
                forestDeployment,
                new TacticalPoint(300, 0, 90),
                facingDegrees: 90,
                frontageMeters: 50));

        var otherScenario = CreateScenario();

        var foreignDeployment =
            otherScenario.Battle.DeployCompany(
                CompanyAt(otherScenario.French),
                otherScenario.Hill,
                CompanyDeploymentRole.FrontLine);

        Assert.Throws<InvalidOperationException>(() =>
            geometry.PlaceCompany(
                foreignDeployment,
                new TacticalPoint(0, 0, 150),
                facingDegrees: 90,
                frontageMeters: 50));

        // Un déploiement valide reste positionnable.
        geometry.PlaceCompany(
            hillDeployment,
            new TacticalPoint(0, 0, 150),
            facingDegrees: 90,
            frontageMeters: 50);

        Assert.Single(geometry.CompanyPositions);
    }

    [Fact]
    public void SpatialRelation_MeasuresDistanceAndDirection()
    {
        var (battle, french, german, hill, forest) =
            CreateScenario();

        var a = battle.DeployCompany(
            CompanyAt(french),
            hill,
            CompanyDeploymentRole.FrontLine);

        var b = battle.DeployCompany(
            CompanyAt(german),
            forest,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(hill, 0, 0, 100);
        geometry.DefineSector(forest, 300, 0, 100);

        // A fait face à l'est.
        geometry.PlaceCompany(
            a,
            new TacticalPoint(0, 0, 150),
            facingDegrees: 90,
            frontageMeters: 50);

        // B fait face à l'ouest.
        geometry.PlaceCompany(
            b,
            new TacticalPoint(300, 0, 190),
            facingDegrees: 270,
            frontageMeters: 50);

        var relation = geometry.Measure(a, b);

        Assert.Equal(
            300.0,
            relation.HorizontalDistanceMeters);

        Assert.Equal(
            40.0,
            relation.ElevationDifferenceMeters);

        Assert.Equal(
            double.Hypot(300, 40),
            relation.StraightLineDistanceMeters,
            precision: 8);

        Assert.Equal(90.0, relation.BearingDegrees);
        Assert.Equal(0.0, relation.RelativeBearingDegrees);

        var reverse = geometry.Measure(b, a);

        Assert.Equal(
            -40.0,
            reverse.ElevationDifferenceMeters);

        Assert.Equal(270.0, reverse.BearingDegrees);
        Assert.Equal(0.0, reverse.RelativeBearingDegrees);
    }

    [Fact]
    public void SameHorizontalPosition_HasNoDefinedBearing()
    {
        var (battle, french, german, hill, _) =
            CreateScenario();

        var a = battle.DeployCompany(
            CompanyAt(french),
            hill,
            CompanyDeploymentRole.FrontLine);

        var b = battle.DeployCompany(
            CompanyAt(german),
            hill,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(hill, 0, 0, 100);

        geometry.PlaceCompany(
            a,
            new TacticalPoint(0, 0, 150),
            facingDegrees: 90,
            frontageMeters: 50);

        geometry.PlaceCompany(
            b,
            new TacticalPoint(0, 0, 150),
            facingDegrees: 270,
            frontageMeters: 50);

        var relation = geometry.Measure(a, b);

        Assert.Equal(
            0.0,
            relation.HorizontalDistanceMeters);

        Assert.Null(relation.BearingDegrees);
        Assert.Null(relation.RelativeBearingDegrees);
    }

    [Fact]
    public void GeometryQueries_DoNotModifyMilitaryResources()
    {
        var (battle, french, german, hill, forest) =
            CreateScenario();

        var a = battle.DeployCompany(
            CompanyAt(french),
            hill,
            CompanyDeploymentRole.FrontLine);

        var b = battle.DeployCompany(
            CompanyAt(german),
            forest,
            CompanyDeploymentRole.FrontLine);

        var frenchBefore =
            french.GetCurrentResourceTotals();

        var germanBefore =
            german.GetCurrentResourceTotals();

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(hill, 0, 0, 100);
        geometry.DefineSector(forest, 300, 0, 100);

        geometry.PlaceCompany(
            a,
            new TacticalPoint(0, 0, 150),
            facingDegrees: 90,
            frontageMeters: 50);

        geometry.PlaceCompany(
            b,
            new TacticalPoint(300, 0, 90),
            facingDegrees: 270,
            frontageMeters: 50);

        var first = geometry.Measure(a, b);
        var second = geometry.Measure(a, b);

        Assert.Equal(first, second);

        Assert.Equal(
            frenchBefore,
            french.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            german.GetCurrentResourceTotals());

        Assert.Equal(2, battle.Deployments.Count);
        Assert.Equal(2, geometry.CompanyPositions.Count);
    }
}
