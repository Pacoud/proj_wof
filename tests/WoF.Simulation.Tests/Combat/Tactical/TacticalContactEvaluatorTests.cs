
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalContactEvaluatorTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        DiplomacySystem Diplomacy,
        Division French,
        Division German,
        BattleSector Hill,
        BattleSector Forest)
    {
        public CompanyDeployment DeployFrench(
            BattleSector sector,
            int index = 0,
            CompanyDeploymentRole role =
                CompanyDeploymentRole.FrontLine)
        {
            return Battle.DeployCompany(
                French.Battalions[0].Companies[index],
                sector,
                role);
        }

        public CompanyDeployment DeployGerman(
            BattleSector sector,
            int index = 0,
            CompanyDeploymentRole role =
                CompanyDeploymentRole.FrontLine)
        {
            return Battle.DeployCompany(
                German.Battalions[0].Companies[index],
                sector,
                role);
        }
    }

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
            1, "Bataillon 1",
            BattalionType.Infantry);

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

    private static Scenario CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battle");

        var french = MakeDivision(
            "French Division", france, province);

        var german = MakeDivision(
            "German Division", germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var battlefield = new TacticalBattlefield();

        var hill = new BattleSector(
            1, "Hill", TerrainType.Hills);

        var forest = new BattleSector(
            2, "Forest", TerrainType.Forest);

        battlefield.AddSector(hill);
        battlefield.AddSector(forest);

        // Longueur de liaison topologique :
        // volontairement différente de la
        // distance entre compagnies.
        battlefield.Connect(hill, forest, 900);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(
            hill, 0, 0, radiusMeters: 160);

        geometry.DefineSector(
            forest, 300, 0, radiusMeters: 160);

        return new Scenario(
            battle,
            geometry,
            diplomacy,
            french,
            german,
            hill,
            forest);
    }

    private static (
        CompanyDeployment French,
        CompanyDeployment German)
        DeployPair(
            Scenario scenario,
            bool sameSector = true)
    {
        var french = scenario.DeployFrench(
            scenario.Hill);

        var german = scenario.DeployGerman(
            sameSector
                ? scenario.Hill
                : scenario.Forest);

        return (french, german);
    }

    private static void PositionPair(
        Scenario scenario,
        CompanyDeployment french,
        CompanyDeployment german,
        double germanX = 100,
        double germanAltitude = 100)
    {
        scenario.Geometry.PlaceCompany(
            french,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        scenario.Geometry.PlaceCompany(
            german,
            new TacticalPoint(
                germanX, 0, germanAltitude),
            facingDegrees: 270,
            frontageMeters: 40);
    }

    private static TacticalLineObstacle Ridge(
        int id,
        double x,
        double topElevation)
    {
        return new TacticalLineObstacle(
            id,
            $"Ridge {id}",
            TacticalLineObstacleKind.Ridge,
            x, -20,
            x, 20,
            topElevation);
    }

    [Fact]
    public void SameSector_UnobstructedContactIsEnriched()
    {
        var s = CreateScenario();

        var (french, german) = DeployPair(s);

        PositionPair(s, french, german);

        var contact = Assert.Single(
            TacticalContactEvaluator.Evaluate(
                s.Battle, s.Geometry, s.Diplomacy));

        Assert.Same(french, contact.Candidate.First);
        Assert.Same(german, contact.Candidate.Second);

        Assert.Equal(
            TacticalContactTopology.SameSector,
            contact.Candidate.Topology);

        Assert.Null(
            contact.Candidate.ConnectionDistanceMeters);

        var spatial = Assert.IsType<TacticalSpatialRelation>(
            contact.SpatialRelation);

        Assert.Equal(
            100.0,
            spatial.HorizontalDistanceMeters);

        Assert.Equal(90.0, spatial.BearingDegrees);
        Assert.Equal(0.0, spatial.RelativeBearingDegrees);

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            contact.FirstToSecondSight.Status);

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            contact.SecondToFirstSight.Status);

        Assert.True(contact.HasResolvedGeometry);
    }

    [Fact]
    public void ConnectedSectors_KeepTwoDistinctDistances()
    {
        var s = CreateScenario();

        var (french, german) = DeployPair(
            s, sameSector: false);

        PositionPair(
            s, french, german,
            germanX: 300,
            germanAltitude: 130);

        var contact = Assert.Single(
            TacticalContactEvaluator.Evaluate(
                s.Battle, s.Geometry, s.Diplomacy));

        Assert.Equal(
            TacticalContactTopology.ConnectedSectors,
            contact.Candidate.Topology);

        // Distance abstraite de la liaison entre secteurs.
        Assert.Equal(
            900.0,
            contact.Candidate.ConnectionDistanceMeters!.Value);

        var spatial = Assert.IsType<TacticalSpatialRelation>(
            contact.SpatialRelation);

        // Distance géométrique entre les compagnies.
        Assert.Equal(
            300.0,
            spatial.HorizontalDistanceMeters);

        Assert.Equal(
            30.0,
            spatial.ElevationDifferenceMeters);

        Assert.Equal(
            double.Hypot(300, 30),
            spatial.StraightLineDistanceMeters,
            precision: 8);

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            contact.FirstToSecondSight.Status);
    }

    [Fact]
    public void BlockingRidge_IsReportedInBothDirections()
    {
        var s = CreateScenario();

        var (french, german) = DeployPair(s);

        PositionPair(s, french, german);

        var ridge = Ridge(1, 50, 110);
        s.Geometry.AddLineObstacle(ridge);

        var contact = Assert.Single(
            TacticalContactEvaluator.Evaluate(
                s.Battle, s.Geometry, s.Diplomacy));

        Assert.Equal(
            TacticalSightStatus.BlockedByObstacle,
            contact.FirstToSecondSight.Status);

        Assert.Equal(
            TacticalSightStatus.BlockedByObstacle,
            contact.SecondToFirstSight.Status);

        Assert.Same(
            ridge,
            contact.FirstToSecondSight.BlockingObstacle);

        Assert.Same(
            ridge,
            contact.SecondToFirstSight.BlockingObstacle);

        Assert.Equal(
            50.0,
            contact.FirstToSecondSight
                .ObstructionDistanceMeters!.Value);

        Assert.True(contact.HasResolvedGeometry);
    }

    [Fact]
    public void EachDirection_HasItsOwnNearestObstacle()
    {
        var s = CreateScenario();

        var (french, german) = DeployPair(s);

        PositionPair(s, french, german);

        var nearFrench = Ridge(1, 20, 110);
        var nearGerman = Ridge(2, 80, 110);

        // On inverse volontairement l'ordre d'ajout.
        s.Geometry.AddLineObstacle(nearGerman);
        s.Geometry.AddLineObstacle(nearFrench);

        var contact = Assert.Single(
            TacticalContactEvaluator.Evaluate(
                s.Battle, s.Geometry, s.Diplomacy));

        Assert.Same(
            nearFrench,
            contact.FirstToSecondSight.BlockingObstacle);

        Assert.Same(
            nearGerman,
            contact.SecondToFirstSight.BlockingObstacle);

        Assert.Equal(
            20.0,
            contact.FirstToSecondSight
                .ObstructionDistanceMeters!.Value);

        Assert.Equal(
            20.0,
            contact.SecondToFirstSight
                .ObstructionDistanceMeters!.Value);
    }

    [Fact]
    public void MissingPosition_RemainsUnknown()
    {
        var s = CreateScenario();

        var (french, _) = DeployPair(s);

        // Seule la compagnie française est positionnée.
        s.Geometry.PlaceCompany(
            french,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        var contact = Assert.Single(
            TacticalContactEvaluator.Evaluate(
                s.Battle, s.Geometry, s.Diplomacy));

        Assert.Null(contact.SpatialRelation);

        Assert.Equal(
            TacticalSightStatus.Unknown,
            contact.FirstToSecondSight.Status);

        Assert.Equal(
            TacticalSightStatus.Unknown,
            contact.SecondToFirstSight.Status);

        Assert.False(contact.HasResolvedGeometry);
    }

    [Fact]
    public void CoincidentPositions_DoNotBecomeClearSight()
    {
        var s = CreateScenario();

        var (french, german) = DeployPair(s);

        PositionPair(
            s, french, german,
            germanX: 0);

        var contact = Assert.Single(
            TacticalContactEvaluator.Evaluate(
                s.Battle, s.Geometry, s.Diplomacy));

        var spatial = Assert.IsType<TacticalSpatialRelation>(
            contact.SpatialRelation);

        Assert.Equal(
            0.0,
            spatial.HorizontalDistanceMeters);

        Assert.Null(spatial.BearingDegrees);

        Assert.Equal(
            TacticalSightStatus.Unknown,
            contact.FirstToSecondSight.Status);

        Assert.Equal(
            TacticalSightStatus.Unknown,
            contact.SecondToFirstSight.Status);

        Assert.False(contact.HasResolvedGeometry);
    }

    [Fact]
    public void SupportsAndReserves_AreNotEvaluated()
    {
        var s = CreateScenario();

        s.DeployFrench(
            s.Hill,
            role: CompanyDeploymentRole.Support);

        s.DeployGerman(
            s.Hill,
            role: CompanyDeploymentRole.Reserve);

        // Aucun contact candidat de première ligne.
        Assert.Empty(
            TacticalContactEvaluator.Evaluate(
                s.Battle, s.Geometry, s.Diplomacy));
    }

    [Fact]
    public void Peace_RemovesContactsBeforeGeometryEvaluation()
    {
        var s = CreateScenario();

        var (french, german) = DeployPair(s);
        PositionPair(s, french, german);

        Assert.Single(
            TacticalContactEvaluator.Evaluate(
                s.Battle, s.Geometry, s.Diplomacy));

        Assert.True(s.Diplomacy.MakePeace(
            s.French.Country!,
            s.German.Country!));

        Assert.Empty(
            TacticalContactEvaluator.Evaluate(
                s.Battle, s.Geometry, s.Diplomacy));
    }

    [Fact]
    public void GeometryFromAnotherBattle_IsRejected()
    {
        var first = CreateScenario();
        var second = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            TacticalContactEvaluator.Evaluate(
                first.Battle,
                second.Geometry,
                first.Diplomacy));
    }

    [Fact]
    public void RepeatedEvaluation_DoesNotModifyBattle()
    {
        var s = CreateScenario();

        var (french, german) = DeployPair(s);
        PositionPair(s, french, german);

        s.Geometry.AddLineObstacle(
            Ridge(1, 50, 110));

        var frenchBefore =
            s.French.GetCurrentResourceTotals();

        var germanBefore =
            s.German.GetCurrentResourceTotals();

        int deploymentsBefore =
            s.Battle.Deployments.Count;

        var first = TacticalContactEvaluator.Evaluate(
            s.Battle, s.Geometry, s.Diplomacy);

        var second = TacticalContactEvaluator.Evaluate(
            s.Battle, s.Geometry, s.Diplomacy);

        Assert.Equal(
            first.ToArray(),
            second.ToArray());

        Assert.Equal(
            frenchBefore,
            s.French.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.German.GetCurrentResourceTotals());

        Assert.Equal(
            deploymentsBefore,
            s.Battle.Deployments.Count);

        Assert.Equal(
            2,
            s.Geometry.CompanyPositions.Count);

        Assert.Single(s.Geometry.LineObstacles);
    }
}
