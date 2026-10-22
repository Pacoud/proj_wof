
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalImpactResolutionTests
{
    private static readonly TacticalHitProfile NormalHit =
        new(2, 1, 0.6);

    private static readonly TacticalDefenseProfile NormalDefense =
        new(1, 0.6, 0.3, 0.1, 0.5, 0.7);

    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalPerceptionState Perception,
        DiplomacySystem Diplomacy,
        CompanyDeployment Shooter,
        CompanyDeployment Target,
        Division French,
        Division German,
        BattleSector Sector);

    private static Division MakeDivision(
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
            1, "Company", CompanyType.Infantry,
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
        double actualTargetX = 100,
        double reportedTargetX = 100)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battle");

        var french = MakeDivision(france, province);
        var german = MakeDivision(germany, province);

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
            1, "Main", TerrainType.Plains,
            features: SectorFeature.Buildings);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var shooter = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector, CompanyDeploymentRole.FrontLine);

        var target = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector, CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);
        geometry.DefineSector(
            sector, 100, 0, radiusMeters: 500);

        geometry.PlaceCompany(
            shooter,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        geometry.PlaceCompany(
            target,
            new TacticalPoint(actualTargetX, 0, 100),
            facingDegrees: 270,
            frontageMeters: 40);

        var perception = new TacticalPerceptionState(battle);

        perception.Apply(1, new[]
        {
            new TacticalObservationEvent(
                shooter, target,
                new TacticalPoint(reportedTargetX, 0, 100),
                sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 10)
        });

        return new Scenario(
            battle, geometry, perception, diplomacy,
            shooter, target, french, german, sector);
    }

    private static void Fire(Scenario s, long hour = 1)
    {
        var result = s.Battle.DirectFire.Execute(
            new[]
            {
                new TacticalFireOrder(
                    s.Shooter, s.Target,
                    new TacticalDirectFireProfile(250),
                    WeaponsCommitted: 10,
                    RoundsPerWeapon: 3)
            },
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            hour);

        Assert.Single(result.Events);
    }

    private static TacticalImpactEvent Resolve(
        Scenario s,
        ulong seed = 42,
        TacticalHitProfile? profile = null)
    {
        var result = s.Battle.DirectFire.ResolveLastPhase(
            profile ?? NormalHit,
            NormalDefense,
            seed);

        return Assert.Single(result.Events);
    }

    [Fact]
    public void Resolution_ProducesIntegerImpactsWithinBounds()
    {
        var s = CreateScenario();
        Fire(s);

        var impact = Resolve(s);

        Assert.Equal(1, impact.Hour);
        Assert.Equal(30, impact.RoundsExpended);
        Assert.InRange(impact.Impacts, 0, 30);
        Assert.InRange(impact.ImpactProbabilityPerRound, 0, 1);
        Assert.Same(s.Shooter, impact.Shooter);
    }

    [Fact]
    public void ExtremelyAccurateProfile_CanResolveAllRounds()
    {
        var s = CreateScenario();
        Fire(s);

        var profile = new TacticalHitProfile(
            minimumDispersionMeters: 0.1,
            dispersionAt100Meters: 0,
            effectiveTargetRadiusMeters: 100);

        var result = Resolve(s, profile: profile);

        Assert.Equal(1, result.ImpactProbabilityPerRound);
        Assert.Equal(30, result.Impacts);
    }

    [Fact]
    public void RealAimOffset_ReducesImpactProbability()
    {
        var centered = CreateScenario(
            actualTargetX: 100,
            reportedTargetX: 100);

        var displaced = CreateScenario(
            actualTargetX: 150,
            reportedTargetX: 100);

        Fire(centered);
        Fire(displaced);

        var first = Resolve(centered);
        var second = Resolve(displaced);

        Assert.Equal(0, first.RealAimOffsetMeters);
        Assert.Equal(50, second.RealAimOffsetMeters, 6);

        Assert.True(
            second.ImpactProbabilityPerRound <
            first.ImpactProbabilityPerRound);
    }

    [Fact]
    public void SameSeedAndConditions_GiveSameResult()
    {
        var a = CreateScenario();
        var b = CreateScenario();

        Fire(a);
        Fire(b);

        var first = Resolve(a, seed: 12345);
        var second = Resolve(b, seed: 12345);

        Assert.Equal(first.Impacts, second.Impacts);
        Assert.Equal(
            first.ImpactProbabilityPerRound,
            second.ImpactProbabilityPerRound);
    }

    [Fact]
    public void SnapshotSurvivesPerceptionAndDefenseChanges()
    {
        var unchanged = CreateScenario();
        var changed = CreateScenario();

        // Le tireur engage une compagnie a decouvert.
        Fire(unchanged);
        Fire(changed);

        // Apres le tir, la compagnie entre dans
        // un batiment et l'ancienne piste perime.
        changed.Battle.DefensiveState.Set(
            changed.Target,
            new TacticalDefensiveDisposition(
                TacticalStance.Prone,
                TacticalCover.Building));

        changed.Perception.Apply(
            2,
            Array.Empty<TacticalObservationEvent>());

        var first = Resolve(unchanged, seed: 42);
        var second = Resolve(changed, seed: 42);

        Assert.Equal(
            first.ImpactProbabilityPerRound,
            second.ImpactProbabilityPerRound);

        Assert.Equal(first.Impacts, second.Impacts);

    }

    [Fact]
    public void SamePhase_CannotBeResolvedTwice()
    {
        var s = CreateScenario();
        Fire(s);

        Resolve(s);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.ResolveLastPhase(
                NormalHit, NormalDefense, seed: 42));
    }

    [Fact]
    public void ResolvingImpacts_DoesNotCausePersonnelLosses()
    {
        var s = CreateScenario();
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

        Assert.Equal(970, attackerBefore.Ammunition.SmallArms);
        Assert.Equal(100, defenderBefore.Personnel.Available);
    }

    [Fact]
    public void ResolutionRequiresAnExecutedPhase()
    {
        var s = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.ResolveLastPhase(
                NormalHit, NormalDefense, seed: 42));
    }

    [Fact]
    public void NewHour_AllowsNewResolution()
    {
        var s = CreateScenario();

        Fire(s, 1);
        Resolve(s, seed: 1);

        s.Perception.Apply(2, new[]
        {
            new TacticalObservationEvent(
                s.Shooter, s.Target,
                new TacticalPoint(100, 0, 100),
                s.Sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 10)
        });

        Fire(s, 2);

        var second = Resolve(s, seed: 2);

        Assert.Equal(2, second.Hour);

        Assert.Equal(
            940,
            s.French.GetCurrentResourceTotals()
                .Ammunition.SmallArms);
    }
}
