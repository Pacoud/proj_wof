
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalProtectedHitTests
{
    private static readonly TacticalHitProfile HitProfile =
        new(
            minimumDispersionMeters: 0.5,
            dispersionAt100Meters: 1.0,
            effectiveTargetRadiusMeters: 0.5);

    private static readonly TacticalDefenseProfile DefenseProfile =
        new(
            standingExposureFactor: 1.0,
            crouchedExposureFactor: 0.6,
            proneExposureFactor: 0.3,
            vegetationInterceptionFraction: 0.1,
            buildingInterceptionFraction: 0.5,
            entrenchmentInterceptionFraction: 0.7);

    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalPerceptionState Perception,
        DiplomacySystem Diplomacy,
        CompanyDeployment Shooter,
        CompanyDeployment Target,
        Division AttackerDivision,
        Division DefenderDivision,
        BattleSector Sector,
        BattleSector? Neighbor);

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
        SectorFeature features = SectorFeature.None,
        bool addNeighbor = false)
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
            features: features);

        battlefield.AddSector(sector);

        BattleSector? neighbor = null;

        if (addNeighbor)
        {
            neighbor = new BattleSector(
                2, "Neighbor", TerrainType.Plains);

            battlefield.AddSector(neighbor);
            battlefield.Connect(sector, neighbor, 150);
        }

        var battle = new TacticalBattle(
            engagement, battlefield);

        var shooter = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var target = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(
            sector, 50, 0, radiusMeters: 500);

        if (neighbor is not null)
        {
            geometry.DefineSector(
                neighbor, 100, 130, radiusMeters: 40);
        }

        geometry.PlaceCompany(
            shooter,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        geometry.PlaceCompany(
            target,
            new TacticalPoint(100, 0, 100),
            facingDegrees: 270,
            frontageMeters: 300);

        var perception = new TacticalPerceptionState(battle);

        perception.Apply(1, new[]
        {
            new TacticalObservationEvent(
                shooter,
                target,
                new TacticalPoint(100, 0, 100),
                sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 0)
        });

        return new Scenario(
            battle, geometry, perception, diplomacy,
            shooter, target, french, german,
            sector, neighbor);
    }

    private static (
        TacticalHitEstimate Geometric,
        TacticalProtectedHitEstimate Protected)
        FireAndEstimate(Scenario s)
    {
        var phase = s.Battle.DirectFire.Execute(
            new[]
            {
                new TacticalFireOrder(
                    s.Shooter,
                    s.Target,
                    new TacticalDirectFireProfile(200),
                    WeaponsCommitted: 10,
                    RoundsPerWeapon: 3)
            },
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            hour: 1);

        Assert.Single(phase.Events);

        var geometric = Assert.Single(
            s.Battle.DirectFire.EstimateLastPhase(
                s.Geometry, s.Perception, HitProfile));

        var defended = Assert.Single(
            s.Battle.DirectFire.EstimateProtectedLastPhase(
                s.Geometry,
                s.Perception,
                HitProfile,
                DefenseProfile));

        return (geometric, defended);
    }

    [Fact]
    public void DefaultDefense_DoesNotChangeProbability()
    {
        var s = CreateScenario(
            features: SectorFeature.Buildings);

        var (geometric, defended) = FireAndEstimate(s);

        // Un secteur avec batiments ne signifie pas
        // que la compagnie est automatiquement abritee.
        Assert.Equal(TacticalCover.None, defended.TargetCover);

        Assert.Equal(
            geometric.HitProbabilityPerRound,
            defended.UnblockedHitProbabilityPerRound,
            10);
    }

    [Fact]
    public void CrouchedBehindBuilding_MultipliesFactors()
    {
        var s = CreateScenario(
            features: SectorFeature.Buildings);

        s.Battle.DefensiveState.Set(
            s.Target,
            new TacticalDefensiveDisposition(
                TacticalStance.Crouched,
                TacticalCover.Building));

        var (geometric, defended) = FireAndEstimate(s);

        // Exposition 0.6, interception 0.5.
        double expected =
            geometric.HitProbabilityPerRound * 0.6 * 0.5;

        Assert.Equal(
            expected,
            defended.UnblockedHitProbabilityPerRound,
            10);

        Assert.Equal(
            expected * 30,
            defended.ExpectedUnblockedImpacts,
            10);
    }

    [Fact]
    public void PronePosture_ReducesExposedProbability()
    {
        var standing = CreateScenario();
        var prone = CreateScenario();

        prone.Battle.DefensiveState.Set(
            prone.Target,
            new TacticalDefensiveDisposition(
                TacticalStance.Prone,
                TacticalCover.None));

        var first = FireAndEstimate(standing).Protected;
        var second = FireAndEstimate(prone).Protected;

        Assert.True(
            second.UnblockedHitProbabilityPerRound <
            first.UnblockedHitProbabilityPerRound);

        Assert.Equal(
            first.UnblockedHitProbabilityPerRound * 0.3,
            second.UnblockedHitProbabilityPerRound,
            10);
    }

    [Fact]
    public void CoverRequiresAppropriateSectorFeature()
    {
        var s = CreateScenario();

        var building = new TacticalDefensiveDisposition(
            TacticalStance.Crouched,
            TacticalCover.Building);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DefensiveState.Set(s.Target, building));

        var entrenched = new TacticalDefensiveDisposition(
            TacticalStance.Prone,
            TacticalCover.Entrenchment);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DefensiveState.Set(s.Target, entrenched));

        Assert.Equal(
            TacticalCover.None,
            s.Battle.DefensiveState.Get(s.Target).Cover);

        var prepared = CreateScenario(
            features: SectorFeature.Entrenchments);

        prepared.Battle.DefensiveState.Set(
            prepared.Target,
            entrenched);

        Assert.Equal(
            TacticalCover.Entrenchment,
            prepared.Battle.DefensiveState
                .Get(prepared.Target).Cover);
    }

    [Fact]
    public void DefenseState_CannotMixDifferentBattles()
    {
        var s = CreateScenario();
        var other = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DefensiveState.Set(
                other.Target,
                new TacticalDefensiveDisposition(
                    TacticalStance.Prone,
                    TacticalCover.None)));

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DefensiveState.Get(other.Target));
    }

    [Fact]
    public void MultipleSectorCoverage_DoesNotMultiplyProtection()
    {
        var s = CreateScenario(
            features: SectorFeature.Buildings,
            addNeighbor: true);

        var index = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        Assert.Same(
            s.Target,
            Assert.Single(
                index.GetCovering(s.Neighbor!)));

        s.Battle.DefensiveState.Set(
            s.Target,
            new TacticalDefensiveDisposition(
                TacticalStance.Crouched,
                TacticalCover.Building));

        var (geometric, defended) = FireAndEstimate(s);

        // La cible reste une seule compagnie, meme
        // si son emprise traverse deux secteurs.
        Assert.Equal(
            geometric.HitProbabilityPerRound * 0.3,
            defended.UnblockedHitProbabilityPerRound,
            10);
    }

    [Fact]
    public void ProtectionEstimate_DoesNotMutateResources()
    {
        var s = CreateScenario(
            features: SectorFeature.Buildings);

        s.Battle.DefensiveState.Set(
            s.Target,
            new TacticalDefensiveDisposition(
                TacticalStance.Prone,
                TacticalCover.Building));

        FireAndEstimate(s);

        var attackerBefore =
            s.AttackerDivision.GetCurrentResourceTotals();

        var defenderBefore =
            s.DefenderDivision.GetCurrentResourceTotals();

        var estimate = Assert.Single(
            s.Battle.DirectFire.EstimateProtectedLastPhase(
                s.Geometry,
                s.Perception,
                HitProfile,
                DefenseProfile));

        Assert.True(estimate.ExpectedUnblockedImpacts >= 0);

        Assert.Equal(
            attackerBefore,
            s.AttackerDivision.GetCurrentResourceTotals());

        Assert.Equal(
            defenderBefore,
            s.DefenderDivision.GetCurrentResourceTotals());

        // 30 cartouches consommees par le tir,
        // aucune consommation par les estimations.
        Assert.Equal(
            970, attackerBefore.Ammunition.SmallArms);

        Assert.Equal(
            100, defenderBefore.Personnel.Available);
    }

    [Fact]
    public void StalePerception_RejectsProtectedEstimation()
    {
        var s = CreateScenario();
        FireAndEstimate(s);

        s.Perception.Apply(
            2,
            Array.Empty<TacticalObservationEvent>());

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.EstimateProtectedLastPhase(
                s.Geometry,
                s.Perception,
                HitProfile,
                DefenseProfile));
    }

    [Fact]
    public void InvalidDefensiveInputs_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalDefenseProfile(
                1, 0.6, 0.3, 0.1, 0.5, double.NaN));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalDefenseProfile(
                1, 0.6, 0.3, 0.1, 1.5, 0.7));

        var s = CreateScenario();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            s.Battle.DefensiveState.Set(
                s.Target,
                new TacticalDefensiveDisposition(
                    (TacticalStance)999,
                    TacticalCover.None)));
    }
}
