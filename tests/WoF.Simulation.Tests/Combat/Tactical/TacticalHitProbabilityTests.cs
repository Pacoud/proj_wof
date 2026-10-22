
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalHitProbabilityTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalPerceptionState Perception,
        DiplomacySystem Diplomacy,
        CompanyDeployment Shooter,
        CompanyDeployment Enemy,
        Division FrenchDivision,
        Division GermanDivision);

    private static readonly TacticalHitProfile TestProfile =
        new(
            minimumDispersionMeters: 0.5,
            dispersionAt100Meters: 1.0,
            effectiveTargetRadiusMeters: 0.5);

    private static Division CreateDivision(
        Country country,
        Province province)
    {
        var division = new Division(
            $"{country.Name} Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: 100,
                infantryEquipment: 100,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Battalion", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1,
            "Company",
            CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 100,
                ammunition: new AmmunitionTotals(
                    smallArms: 1000))));

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario(
        double realTargetX = 100,
        double observedTargetX = 100,
        double uncertainty = 0,
        bool observeBothSides = false)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battle");

        var french = CreateDivision(france, province);
        var german = CreateDivision(germany, province);

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
            1, "Plain", TerrainType.Plains);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var shooter = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var enemy = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(
            sector,
            centerXMeters: 100,
            centerYMeters: 0,
            radiusMeters: 500);

        geometry.PlaceCompany(
            shooter,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        geometry.PlaceCompany(
            enemy,
            new TacticalPoint(realTargetX, 0, 100),
            facingDegrees: 270,
            frontageMeters: 40);

        var perception = new TacticalPerceptionState(battle);

        var observations = new List<TacticalObservationEvent>
        {
            new(
                shooter,
                enemy,
                new TacticalPoint(observedTargetX, 0, 100),
                sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: uncertainty)
        };

        if (observeBothSides)
        {
            observations.Add(new TacticalObservationEvent(
                enemy,
                shooter,
                new TacticalPoint(0, 0, 100),
                sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 0));
        }

        perception.Apply(1, observations);

        return new Scenario(
            battle,
            geometry,
            perception,
            diplomacy,
            shooter,
            enemy,
            french,
            german);
    }

    private static TacticalFireEvent Fire(Scenario s)
    {
        var phase = s.Battle.DirectFire.Execute(
            new[]
            {
                new TacticalFireOrder(
                    s.Shooter,
                    s.Enemy,
                    new TacticalDirectFireProfile(250),
                    WeaponsCommitted: 10,
                    RoundsPerWeapon: 3)
            },
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            hour: 1);

        return Assert.Single(phase.Events);
    }

    private static TacticalHitEstimate Estimate(
        Scenario scenario,
        TacticalFireEvent fireEvent,
        TacticalHitProfile? profile = null)
    {
        return TacticalHitProbabilityEvaluator.Evaluate(
            fireEvent,
            scenario.Geometry,
            scenario.Perception,
            profile ?? TestProfile);
    }

    [Fact]
    public void Probability_MatchesGaussianModel()
    {
        var scenario = CreateScenario();
        var fireEvent = Fire(scenario);

        var estimate = Estimate(scenario, fireEvent);

        // sigma^2 = 0.5^2 + 1^2 = 1.25
        // P = 1 - exp(-0.5^2 / (2 * 1.25))
        double expectedProbability = 1 - Math.Exp(-0.1);

        Assert.Equal(100, estimate.EstimatedRangeMeters, 6);
        Assert.Equal(0, estimate.LocalizationUncertaintyMeters);
        Assert.Equal(Math.Sqrt(1.25),
            estimate.EffectiveDispersionMeters, 6);

        Assert.Equal(expectedProbability,
            estimate.HitProbabilityPerRound, 10);

        Assert.Equal(30 * expectedProbability,
            estimate.ExpectedImpacts, 10);

        Assert.Equal(fireEvent.TargetTrackId,
            estimate.TargetTrackId);
    }

    [Fact]
    public void GreaterDistance_ReducesHitProbability()
    {
        var near = CreateScenario(
            observedTargetX: 80);

        var far = CreateScenario(
            observedTargetX: 180);

        var nearEstimate = Estimate(near, Fire(near));
        var farEstimate = Estimate(far, Fire(far));

        Assert.True(
            nearEstimate.HitProbabilityPerRound >
            farEstimate.HitProbabilityPerRound);
    }

    [Fact]
    public void LocalizationUncertainty_ReducesProbability()
    {
        var precise = CreateScenario(uncertainty: 0);
        var uncertain = CreateScenario(uncertainty: 20);

        var preciseEstimate =
            Estimate(precise, Fire(precise));

        var uncertainEstimate =
            Estimate(uncertain, Fire(uncertain));

        Assert.True(
            preciseEstimate.HitProbabilityPerRound >
            uncertainEstimate.HitProbabilityPerRound);

        Assert.True(
            preciseEstimate.ExpectedImpacts >
            uncertainEstimate.ExpectedImpacts);
    }

    [Fact]
    public void Model_UsesObservedNotRealEnemyPosition()
    {
        // Positions reelles differentes,
        // mais localisation rapportee identique.
        var first = CreateScenario(
            realTargetX: 100,
            observedTargetX: 120);

        var second = CreateScenario(
            realTargetX: 180,
            observedTargetX: 120);

        var firstEstimate = Estimate(first, Fire(first));
        var secondEstimate = Estimate(second, Fire(second));

        Assert.Equal(120, firstEstimate.EstimatedRangeMeters, 6);
        Assert.Equal(120, secondEstimate.EstimatedRangeMeters, 6);

        Assert.Equal(
            firstEstimate.HitProbabilityPerRound,
            secondEstimate.HitProbabilityPerRound);

        Assert.Equal(
            firstEstimate.ExpectedImpacts,
            secondEstimate.ExpectedImpacts);
    }

    [Fact]
    public void OtherObserversTrack_CannotBeUsed()
    {
        var s = CreateScenario(observeBothSides: true);
        var fire = Fire(s);

        var enemyReport = Assert.Single(
            s.Perception.GetReports(s.Enemy));

        var forged = fire with
        {
            TargetTrackId = enemyReport.TrackId
        };

        Assert.Throws<InvalidOperationException>(() =>
            Estimate(s, forged));
    }

    [Fact]
    public void OldContact_CannotBeReevaluatedAsCurrent()
    {
        var s = CreateScenario();
        var fire = Fire(s);

        // L'heure suivante : le contact devient perime.
        s.Perception.Apply(
            2,
            Array.Empty<TacticalObservationEvent>());

        Assert.Throws<InvalidOperationException>(() =>
            Estimate(s, fire));
    }

    [Fact]
    public void Estimation_DoesNotConsumeAgainOrCauseLosses()
    {
        var s = CreateScenario();
        Fire(s);

        var frenchBefore =
            s.FrenchDivision.GetCurrentResourceTotals();

        var germanBefore =
            s.GermanDivision.GetCurrentResourceTotals();

        var first = s.Battle.DirectFire.EstimateLastPhase(
            s.Geometry, s.Perception, TestProfile);

        var second = s.Battle.DirectFire.EstimateLastPhase(
            s.Geometry, s.Perception, TestProfile);

        Assert.Single(first);
        Assert.Equal(first[0], Assert.Single(second));

        Assert.Equal(
            frenchBefore,
            s.FrenchDivision.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.GermanDivision.GetCurrentResourceTotals());

        // La salve a coute 30 cartouches,
        // l'estimation n'en coute aucune.
        Assert.Equal(970, frenchBefore.Ammunition.SmallArms);
        Assert.Equal(100, germanBefore.Personnel.Available);
    }

    [Fact]
    public void ProfileAndAmmunitionType_AreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalHitProfile(0, 1, 0.5));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalHitProfile(0.5, -1, 0.5));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalHitProfile(0.5, 1, double.NaN));

        var s = CreateScenario();
        var fire = Fire(s);

        var unsupported = fire with
        {
            AmmunitionType = AmmunitionType.Tank
        };

        Assert.Throws<NotSupportedException>(() =>
            Estimate(s, unsupported));
    }

    [Fact]
    public void ForeignBattle_CannotProvideFireEstimation()
    {
        var s = CreateScenario();
        var foreign = CreateScenario();

        var fire = Fire(s);

        Assert.Throws<InvalidOperationException>(() =>
            TacticalHitProbabilityEvaluator.Evaluate(
                fire,
                foreign.Geometry,
                foreign.Perception,
                TestProfile));

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.EstimateLastPhase(
                foreign.Geometry,
                foreign.Perception,
                TestProfile));
    }
}
