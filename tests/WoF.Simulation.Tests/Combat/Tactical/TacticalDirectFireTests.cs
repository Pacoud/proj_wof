
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalDirectFireTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalPerceptionState Perception,
        DiplomacySystem Diplomacy,
        CompanyDeployment French,
        CompanyDeployment German,
        CompanyDeployment FrenchReserve,
        BattleSector Sector,
        Division FrenchDivision,
        Division GermanDivision);

    private static Division MakeDivision(
        Country country,
        Province province,
        int count)
    {
        var division = new Division(
            $"{country.Name} Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: count * 100,
                infantryEquipment: count * 100,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Battalion", BattalionType.Infantry);

        for (int i = 1; i <= count; i++)
        {
            battalion.AddCompany(new Company(
                i,
                $"Company {i}",
                CompanyType.Infantry,
                new CompanyComposition(
                    manpower: 100,
                    infantryEquipment: 100)));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario(
        double frenchFacing = 90,
        bool placeGerman = true)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battle");

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
        var sector = new BattleSector(
            1, "Plaine", TerrainType.Plains);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var frenchFront = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var reserve = battle.DeployCompany(
            french.Battalions[0].Companies[1],
            sector,
            CompanyDeploymentRole.Reserve);

        var germanFront = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);
        geometry.DefineSector(
            sector, 50, 0, radiusMeters: 500);

        geometry.PlaceCompany(
            frenchFront,
            new TacticalPoint(0, 0, 100),
            facingDegrees: frenchFacing,
            frontageMeters: 40);

        geometry.PlaceCompany(
            reserve,
            new TacticalPoint(0, 30, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        if (placeGerman)
        {
            geometry.PlaceCompany(
                germanFront,
                new TacticalPoint(100, 0, 100),
                facingDegrees: 270,
                frontageMeters: 40);
        }

        return new Scenario(
            battle,
            geometry,
            new TacticalPerceptionState(battle),
            diplomacy,
            frenchFront,
            germanFront,
            reserve,
            sector,
            french,
            german);
    }

    private static TacticalDirectFireDecision Evaluate(
        Scenario s,
        long hour = 1,
        double range = 200,
        double arc = 120,
        CompanyDeployment? attacker = null,
        CompanyDeployment? target = null)
    {
        return TacticalDirectFireEvaluator.Evaluate(
            s.Battle,
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            attacker ?? s.French,
            target ?? s.German,
            hour,
            new TacticalDirectFireProfile(range, arc));
    }

    private static void MarkLocated(Scenario s, long hour)
    {
        s.Perception.Apply(hour, new[]
        {
            new TacticalObservationEvent(
                s.French,
                s.German,
                new TacticalPoint(100, 0, 100),
                s.Sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 0)
        });
    }

    [Fact]
    public void ActualDetection_AllowsDirectFireWithoutDamage()
    {
        var s = CreateScenario();

        var frenchBefore =
            s.FrenchDivision.GetCurrentResourceTotals();

        var germanBefore =
            s.GermanDivision.GetCurrentResourceTotals();

        var signatures = new TacticalSignatureFrame(s.Battle);

        var visible = new TacticalUnitSignature(
            TacticalActivity.Stationary,
            TacticalConcealment.None);

        signatures.Set(s.French, visible);
        signatures.Set(s.German, visible);

        TacticalDetectionSystem.Update(
            s.Battle,
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            signatures,
            currentHour: 1,
            new TacticalDetectionProfile(
                maxVisualRangeMeters: 200,
                maxAcousticRangeMeters: 0,
                fieldOfViewDegrees: 120));

        var decision = Evaluate(s);

        Assert.True(decision.CanFire);
        Assert.Equal(
            TacticalDirectFireStatus.Allowed,
            decision.Status);

        // C2.4a n'effectue aucun tir ni aucune perte.
        Assert.Equal(
            frenchBefore,
            s.FrenchDivision.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.GermanDivision.GetCurrentResourceTotals());
    }

    [Fact]
    public void UnknownSuspectedAndStaleTargets_CannotBeFiredUpon()
    {
        var s = CreateScenario();

        Assert.Equal(
            TacticalDirectFireStatus.NoCurrentLocalization,
            Evaluate(s).Status);

        s.Perception.Apply(1, new[]
        {
            new TacticalObservationEvent(
                s.French,
                s.German,
                ObservedPosition: null,
                ObservedSectorId: s.Sector.Id,
                KnowledgeLevel: TacticalKnowledgeLevel.Suspected,
                PositionUncertaintyMeters: null)
        });

        Assert.Equal(
            TacticalDirectFireStatus.NoCurrentLocalization,
            Evaluate(s).Status);

        MarkLocated(s, hour: 2);

        Assert.True(Evaluate(s, hour: 2).CanFire);

        s.Perception.Apply(
            3,
            Array.Empty<TacticalObservationEvent>());

        Assert.Equal(
            TacticalDirectFireStatus.NoCurrentLocalization,
            Evaluate(s, hour: 3).Status);

        // La mémoire conserve néanmoins la dernière
        // position connue de l'ennemi.
        var memory = Assert.Single(
            s.Perception.GetReports(s.French));

        Assert.Equal(
            TacticalKnowledgeLevel.Located,
            memory.BestKnownLevel);

        Assert.False(memory.IsCurrentlyObserved);
    }

    [Fact]
    public void WeaponRangeAndFiringArc_AreEnforced()
    {
        var normal = CreateScenario();
        MarkLocated(normal, 1);

        Assert.Equal(
            TacticalDirectFireStatus.OutOfRange,
            Evaluate(normal, range: 50).Status);

        var turnedAway = CreateScenario(frenchFacing: 0);
        MarkLocated(turnedAway, 1);

        Assert.Equal(
            TacticalDirectFireStatus.OutsideFiringArc,
            Evaluate(turnedAway, arc: 120).Status);
    }

    [Fact]
    public void ObstacleBlocksFireEvenWithKnownTarget()
    {
        var s = CreateScenario();
        MarkLocated(s, 1);

        s.Geometry.AddLineObstacle(
            new TacticalLineObstacle(
                1, "Crete",
                TacticalLineObstacleKind.Ridge,
                50, -20,
                50, 20,
                topElevationMeters: 110));

        Assert.Equal(
            TacticalDirectFireStatus.LineOfSightUnavailable,
            Evaluate(s).Status);
    }

    [Fact]
    public void LostWeaponsAndPersonnel_PreventDirectFire()
    {
        var unarmed = CreateScenario();
        MarkLocated(unarmed, 1);

        unarmed.French.Company.Composition!
            .InfantryEquipment.ApplyLoss(100);

        Assert.Equal(
            TacticalDirectFireStatus.NoInfantryEquipment,
            Evaluate(unarmed).Status);

        var depleted = CreateScenario();
        MarkLocated(depleted, 1);

        depleted.French.Company.Composition!
            .Manpower.ApplyCasualties(
                new PersonnelCasualtyReport(
                    KilledInAction: 100,
                    WoundedInAction: 0,
                    MissingOrCaptured: 0));

        Assert.Equal(
            TacticalDirectFireStatus.NoPersonnel,
            Evaluate(depleted).Status);
    }

    [Fact]
    public void FriendlyUnitsReservesAndSelfTarget_AreRejected()
    {
        var s = CreateScenario();

        Assert.Equal(
            TacticalDirectFireStatus.SameUnit,
            Evaluate(
                s,
                attacker: s.French,
                target: s.French).Status);

        Assert.Equal(
            TacticalDirectFireStatus.NotEnemies,
            Evaluate(
                s,
                attacker: s.French,
                target: s.FrenchReserve).Status);

        Assert.Equal(
            TacticalDirectFireStatus.NotFrontLine,
            Evaluate(
                s,
                attacker: s.FrenchReserve,
                target: s.German).Status);
    }

    [Fact]
    public void ForeignBattleAndUnknownPositions_AreRejected()
    {
        var s = CreateScenario();
        var other = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            Evaluate(s, target: other.German));

        Assert.Throws<InvalidOperationException>(() =>
            TacticalDirectFireEvaluator.Evaluate(
                s.Battle,
                other.Geometry,
                s.Diplomacy,
                s.Perception,
                s.French,
                s.German,
                1,
                new TacticalDirectFireProfile(200)));

        var missingPosition = CreateScenario(
            placeGerman: false);

        MarkLocated(missingPosition, 1);

        Assert.Equal(
            TacticalDirectFireStatus.MissingPosition,
            Evaluate(missingPosition).Status);
    }

    [Fact]
    public void InvalidFireProfiles_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalDirectFireProfile(0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalDirectFireProfile(double.NaN));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalDirectFireProfile(200, 361));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalDirectFireProfile(200, 0));
    }
}
