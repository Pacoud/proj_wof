
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalLineOfSightTests
{
    private static Division MakeDivision(
        string name,
        Country country,
        Province province)
    {
        var division = new Division(
            name, province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: 100,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Bataillon",
            BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "Compagnie",
            CompanyType.Infantry,
            new CompanyComposition(manpower: 100)));

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static (
        TacticalGeometryMap Geometry,
        CompanyDeployment Observer,
        CompanyDeployment Target,
        Division French,
        Division German)
        CreateScenario(
            double observerGround = 100,
            double targetGround = 100,
            double targetX = 100,
            bool placeTarget = true)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Test");

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
        var sector = new BattleSector(
            1, "Colline", TerrainType.Hills);

        field.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, field);

        var observer = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var target = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(
            sector, 50, 0, radiusMeters: 150);

        geometry.PlaceCompany(
            observer,
            new TacticalPoint(0, 0, observerGround),
            facingDegrees: 90,
            frontageMeters: 40);

        if (placeTarget)
        {
            geometry.PlaceCompany(
                target,
                new TacticalPoint(targetX, 0, targetGround),
                facingDegrees: 270,
                frontageMeters: 40);
        }

        return (geometry, observer, target, french, german);
    }

    private static TacticalLineObstacle Ridge(
        int id,
        double x,
        double topElevation)
    {
        return new TacticalLineObstacle(
            id,
            $"Crête {id}",
            TacticalLineObstacleKind.Ridge,
            x, -20,
            x, 20,
            topElevation);
    }

    [Fact]
    public void HighRidge_BlocksObservation()
    {
        var (geometry, observer, target, _, _) =
            CreateScenario();

        var ridge = Ridge(1, 50, 103);
        geometry.AddLineObstacle(ridge);

        var result = TacticalSightEvaluator.Evaluate(
            geometry, observer, target);

        Assert.Equal(
            TacticalSightStatus.BlockedByObstacle,
            result.Status);

        Assert.Same(ridge, result.BlockingObstacle);
        Assert.Equal(100.0,
            result.HorizontalDistanceMeters);

        Assert.Equal(50.0,
            result.ObstructionDistanceMeters);
    }

    [Fact]
    public void LowRidge_DoesNotBlockKnownCorridor()
    {
        var (geometry, observer, target, _, _) =
            CreateScenario();

        // Les yeux des observateurs sont à 101,7 m.
        geometry.AddLineObstacle(
            Ridge(1, 50, 101));

        var result = TacticalSightEvaluator.Evaluate(
            geometry, observer, target);

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            result.Status);

        Assert.Null(result.BlockingObstacle);
        Assert.Null(result.ObstructionDistanceMeters);
    }

    [Fact]
    public void OffAxisOrBeyondTarget_DoesNotBlock()
    {
        var (geometry, observer, target, _, _) =
            CreateScenario();

        // Obstacle au nord du corridor Y = 0.
        geometry.AddLineObstacle(
            new TacticalLineObstacle(
                1, "Mur au nord",
                TacticalLineObstacleKind.Wall,
                50, 20,
                50, 40,
                topElevationMeters: 200));

        // Obstacle après la cible X = 100.
        geometry.AddLineObstacle(Ridge(2, 150, 200));

        var result = TacticalSightEvaluator.Evaluate(
            geometry, observer, target);

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            result.Status);
    }

    [Fact]
    public void NearestBlockingObstacle_IsReturned()
    {
        var (geometry, observer, target, _, _) =
            CreateScenario();

        // Le plus éloigné est ajouté en premier.
        geometry.AddLineObstacle(Ridge(1, 80, 110));
        geometry.AddLineObstacle(Ridge(2, 20, 110));

        var result = TacticalSightEvaluator.Evaluate(
            geometry, observer, target);

        Assert.Equal(
            TacticalSightStatus.BlockedByObstacle,
            result.Status);

        Assert.Equal(2, result.BlockingObstacle?.Id);

        Assert.Equal(
            20.0,
            result.ObstructionDistanceMeters);
    }

    [Fact]
    public void SlopedSightline_InterpolatesObserverAltitude()
    {
        var (geometry, observer, target, _, _) =
            CreateScenario(
                observerGround: 100,
                targetGround: 200);

        // À mi-chemin, la ligne est à 151,7 m.
        geometry.AddLineObstacle(Ridge(1, 50, 140));

        var first = TacticalSightEvaluator.Evaluate(
            geometry, observer, target);

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            first.Status);

        geometry.AddLineObstacle(Ridge(2, 50, 160));

        var second = TacticalSightEvaluator.Evaluate(
            geometry, observer, target);

        Assert.Equal(
            TacticalSightStatus.BlockedByObstacle,
            second.Status);

        Assert.Equal(2, second.BlockingObstacle?.Id);
    }

    [Fact]
    public void CollinearObstacle_CanBlockCorridor()
    {
        var (geometry, observer, target, _, _) =
            CreateScenario();

        // Le segment suit l'axe entre les compagnies.
        geometry.AddLineObstacle(
            new TacticalLineObstacle(
                1, "Mur longitudinal",
                TacticalLineObstacleKind.Wall,
                30, 0,
                70, 0,
                topElevationMeters: 110));

        var result = TacticalSightEvaluator.Evaluate(
            geometry, observer, target);

        Assert.Equal(
            TacticalSightStatus.BlockedByObstacle,
            result.Status);

        Assert.Equal(
            30.0,
            result.ObstructionDistanceMeters);
    }

    [Fact]
    public void MissingOrCoincidentPositions_AreUnknown()
    {
        var missing = CreateScenario(
            placeTarget: false);

        var unknown = TacticalSightEvaluator.Evaluate(
            missing.Geometry,
            missing.Observer,
            missing.Target);

        Assert.Equal(
            TacticalSightStatus.Unknown,
            unknown.Status);

        var coincident = CreateScenario(targetX: 0);

        var samePoint = TacticalSightEvaluator.Evaluate(
            coincident.Geometry,
            coincident.Observer,
            coincident.Target);

        Assert.Equal(
            TacticalSightStatus.Unknown,
            samePoint.Status);

        Assert.Null(samePoint.BlockingObstacle);
    }

    [Fact]
    public void InvalidObstaclesAndEyeHeights_AreRejected()
    {
        var (geometry, observer, target, _, _) =
            CreateScenario();

        Assert.Throws<ArgumentException>(() =>
            new TacticalLineObstacle(
                1, "Dégénéré",
                TacticalLineObstacleKind.Ridge,
                10, 10,
                10, 10,
                110));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalLineObstacle(
                2, "Altitude invalide",
                TacticalLineObstacleKind.Wall,
                10, 0,
                10, 20,
                double.NaN));

        geometry.AddLineObstacle(Ridge(1, 50, 110));

        Assert.Throws<InvalidOperationException>(() =>
            geometry.AddLineObstacle(Ridge(1, 70, 110)));

        Assert.Single(geometry.LineObstacles);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TacticalSightEvaluator.Evaluate(
                geometry, observer, target,
                observerEyeHeightMeters: -1));
    }

    [Fact]
    public void SightQueries_AreReadOnlyAndDeterministic()
    {
        var (geometry, observer, target, french, german) =
            CreateScenario();

        geometry.AddLineObstacle(Ridge(1, 50, 110));

        var frenchBefore = french.GetCurrentResourceTotals();
        var germanBefore = german.GetCurrentResourceTotals();

        var first = TacticalSightEvaluator.Evaluate(
            geometry, observer, target);

        var second = TacticalSightEvaluator.Evaluate(
            geometry, observer, target);

        Assert.Equal(first, second);

        Assert.Equal(
            frenchBefore,
            french.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            german.GetCurrentResourceTotals());

        Assert.Single(geometry.LineObstacles);
        Assert.Equal(2, geometry.CompanyPositions.Count);
    }
}
