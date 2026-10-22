
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalPersonnelContactTests
{
    private static readonly TacticalHitProfile HitProfile =
        new(2, 1, 0.6);

    private static readonly TacticalDefenseProfile DefenseProfile =
        new(1, 0.6, 0.3, 0.1, 0.5, 0.7);

    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalPerceptionState Perception,
        DiplomacySystem Diplomacy,
        CompanyDeployment Shooter,
        CompanyDeployment? SecondShooter,
        CompanyDeployment Target,
        Division French,
        Division German,
        BattleSector Sector);

    private static Division MakeDivision(
        Country country,
        Province province,
        int battalions)
    {
        var division = new Division(
            $"{country.Name} Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: battalions * 100,
                infantryEquipment: battalions * 100,
                artillery: 0,
                tanks: 0));

        for (int i = 1; i <= battalions; i++)
        {
            var battalion = new Battalion(
                i, $"Battalion {i}", BattalionType.Infantry);

            battalion.AddCompany(new Company(
                1, $"Company {i}", CompanyType.Infantry,
                new CompanyComposition(
                    manpower: 100,
                    infantryEquipment: 100,
                    ammunition: new AmmunitionTotals(
                        smallArms: 1000))));

            division.AddBattalion(battalion);
        }

        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario(
        int exposed = 30,
        double depth = 30,
        double frontage = 40,
        double reportedY = 0,
        bool secondShooter = false)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battle");

        var french = MakeDivision(
            france, province, secondShooter ? 2 : 1);

        var german = MakeDivision(germany, province, 1);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { french, german }, currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var battlefield = new TacticalBattlefield();
        var sector = new BattleSector(
            1, "Main", TerrainType.Plains,
            features: SectorFeature.Buildings);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var shooter = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        CompanyDeployment? second = null;

        if (secondShooter)
        {
            second = battle.DeployCompany(
                french.Battalions[1].Companies[0],
                sector,
                CompanyDeploymentRole.FrontLine);
        }

        var target = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(
            sector, 100, 0, radiusMeters: 500);

        geometry.PlaceCompany(
            shooter,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        if (second is not null)
        {
            geometry.PlaceCompany(
                second,
                new TacticalPoint(0, 10, 100),
                facingDegrees: 90,
                frontageMeters: 40);
        }

        geometry.PlaceCompany(
            target,
            new TacticalPoint(100, 0, 100),
            facingDegrees: 270,
            frontageMeters: frontage);

        var perception = new TacticalPerceptionState(battle);

        var observations = new List<TacticalObservationEvent>
        {
            new(
                shooter,
                target,
                new TacticalPoint(100, reportedY, 100),
                sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 10)
        };

        if (second is not null)
        {
            observations.Add(new TacticalObservationEvent(
                second,
                target,
                new TacticalPoint(100, reportedY, 100),
                sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 10));
        }

        perception.Apply(1, observations);

        battle.PersonnelExposure.Set(
            target,
            new TacticalPersonnelExposure(
                exposed: exposed,
                sheltered: 100 - exposed,
                reserve: 0,
                exposedDepthMeters: depth));

        return new Scenario(
            battle,
            geometry,
            perception,
            diplomacy,
            shooter,
            second,
            target,
            french,
            german,
            sector);
    }

    private static void Fire(Scenario s)
    {
        var orders = new List<TacticalFireOrder>
        {
            new(
                s.Shooter,
                s.Target,
                new TacticalDirectFireProfile(250),
                WeaponsCommitted: 10,
                RoundsPerWeapon: 3)
        };

        if (s.SecondShooter is not null)
        {
            orders.Add(new TacticalFireOrder(
                s.SecondShooter,
                s.Target,
                new TacticalDirectFireProfile(250),
                WeaponsCommitted: 10,
                RoundsPerWeapon: 3));
        }

        var phase = s.Battle.DirectFire.Execute(
            orders,
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            hour: 1);

        Assert.Equal(orders.Count, phase.Events.Count);
    }

    private static TacticalPersonnelContactPhaseResult Resolve(
        Scenario s,
        ulong seed = 42)
    {
        return s.Battle.DirectFire.ResolvePersonnelLastPhase(
            HitProfile,
            DefenseProfile,
            seed);
    }

    [Fact]
    public void Exposure_MustMatchExistingPersonnel()
    {
        var s = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.PersonnelExposure.Set(
                s.Target,
                new TacticalPersonnelExposure(
                    30, 40, 20, 30)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalPersonnelExposure(
                -1, 50, 51, 30));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalPersonnelExposure(
                30, 40, 30, 0));

        var distribution =
            s.Battle.PersonnelExposure.Get(s.Target);

        Assert.Equal(100, distribution.Total);
        Assert.Equal(30, distribution.Exposed);
    }

    [Fact]
    public void NoExposedSoldiers_ProducesNoPersonnelContacts()
    {
        var s = CreateScenario(exposed: 0);

        Fire(s);
        var result = Resolve(s);

        var contact = Assert.Single(result.Events);
        var target = Assert.Single(result.Targets);

        Assert.Equal(0, contact.ProbabilityPerRound);
        Assert.Equal(0, contact.ContactEvents);
        Assert.Equal(0, target.ExpectedDistinctPersonnel);
    }

    [Fact]
    public void MoreExposedSoldiers_IncreaseContactProbability()
    {
        var sparse = CreateScenario(exposed: 10);
        var dense = CreateScenario(exposed: 80);

        Fire(sparse);
        Fire(dense);

        var first = Assert.Single(Resolve(sparse).Events);
        var second = Assert.Single(Resolve(dense).Events);

        Assert.True(
            second.ProbabilityPerRound >
            first.ProbabilityPerRound);
    }

    [Fact]
    public void ShootingAlongWideFront_CanReachFlank()
    {
        // Point vise 60 m sur le cote du centre reel.
        var narrow = CreateScenario(
            exposed: 80,
            frontage: 20,
            reportedY: 60);

        var wide = CreateScenario(
            exposed: 80,
            frontage: 200,
            reportedY: 60);

        Fire(narrow);
        Fire(wide);

        var a = Assert.Single(Resolve(narrow).Events);
        var b = Assert.Single(Resolve(wide).Events);

        // Front etroit : point vise hors emprise.
        // Front large : point vise dans l'emprise.
        Assert.True(
            b.ProbabilityPerRound >
            a.ProbabilityPerRound);
    }

    [Fact]
    public void SameSeedAndSituation_AreReproducible()
    {
        var first = CreateScenario(exposed: 80);
        var second = CreateScenario(exposed: 80);

        Fire(first);
        Fire(second);

        var a = Assert.Single(Resolve(first, 1234).Events);
        var b = Assert.Single(Resolve(second, 1234).Events);

        Assert.Equal(
            a.ProbabilityPerRound,
            b.ProbabilityPerRound);

        Assert.Equal(a.ContactEvents, b.ContactEvents);
    }

    [Fact]
    public void ChangingExposureAfterFire_DoesNotRewriteHistory()
    {
        var original = CreateScenario(exposed: 80);
        var changed = CreateScenario(exposed: 80);

        Fire(original);
        Fire(changed);

        changed.Battle.PersonnelExposure.Set(
            changed.Target,
            new TacticalPersonnelExposure(
                exposed: 5,
                sheltered: 95,
                reserve: 0,
                exposedDepthMeters: 30));

        changed.Battle.DefensiveState.Set(
            changed.Target,
            new TacticalDefensiveDisposition(
                TacticalStance.Prone,
                TacticalCover.Building));

        changed.Perception.Apply(
            2,
            Array.Empty<TacticalObservationEvent>());

        var a = Assert.Single(Resolve(original, 7).Events);
        var b = Assert.Single(Resolve(changed, 7).Events);

        Assert.Equal(80, b.ExposedPersonnel);
        Assert.Equal(a.ProbabilityPerRound,
            b.ProbabilityPerRound);

        Assert.Equal(a.ContactEvents, b.ContactEvents);
    }

    [Fact]
    public void SeveralShooters_ProduceOneTargetSummary()
    {
        var s = CreateScenario(
            exposed: 80,
            secondShooter: true);

        Fire(s);
        var result = Resolve(s);

        Assert.Equal(2, result.Events.Count);

        var summary = Assert.Single(result.Targets);

        Assert.Same(s.Target, summary.Target);

        Assert.Equal(
            result.Events.Sum(e => e.ContactEvents),
            summary.ContactEvents);

        Assert.InRange(
            summary.ExpectedDistinctPersonnel,
            0,
            Math.Min(
                80.0,
                (double)summary.ContactEvents));
    }

    [Fact]
    public void MultipleSectorCoverage_DoesNotDuplicatePopulation()
    {
        var s = CreateScenario(
            exposed: 80,
            frontage: 200);

        var neighbor = new BattleSector(
            2, "Neighbor", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(neighbor);
        s.Battle.Battlefield.Connect(
            s.Sector, neighbor, 100);

        s.Geometry.DefineSector(
            neighbor, 100, 80, radiusMeters: 30);

        var index = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        Assert.Contains(
            s.Target,
            index.GetCovering(neighbor));

        Fire(s);
        var result = Resolve(s);

        Assert.Same(
            s.Target,
            Assert.Single(result.Targets).Target);

        Assert.Equal(
            100,
            s.German.GetCurrentResourceTotals()
                .Personnel.Available);
    }

    [Fact]
    public void ContactResolution_DoesNotApplyCasualties()
    {
        var s = CreateScenario(exposed: 100);

        Fire(s);

        var attackerBefore =
            s.French.GetCurrentResourceTotals();

        var defenderBefore =
            s.German.GetCurrentResourceTotals();

        Resolve(s);

        Assert.Equal(
            attackerBefore,
            s.French.GetCurrentResourceTotals());

        Assert.Equal(
            defenderBefore,
            s.German.GetCurrentResourceTotals());

        Assert.Equal(
            970,
            attackerBefore.Ammunition.SmallArms);

        Assert.Equal(
            100,
            defenderBefore.Personnel.Available);
    }

    [Fact]
    public void PersonnelPhase_CannotBeResolvedTwice()
    {
        var s = CreateScenario();

        Fire(s);
        Resolve(s);

        Assert.Throws<InvalidOperationException>(() =>
            Resolve(s));
    }

    [Fact]
    public void DistinctPersonnelEstimate_RespectsBounds()
    {
        Assert.Equal(
            0,
            TacticalPersonnelContactResolver.ExpectedDistinct(0, 0));

        Assert.Equal(
            1,
            TacticalPersonnelContactResolver.ExpectedDistinct(1, 50));

        Assert.InRange(
            TacticalPersonnelContactResolver.ExpectedDistinct(10, 100),
            0,
            10);

        Assert.InRange(
            TacticalPersonnelContactResolver.ExpectedDistinct(100, 1),
            0,
            1);
    }
}
