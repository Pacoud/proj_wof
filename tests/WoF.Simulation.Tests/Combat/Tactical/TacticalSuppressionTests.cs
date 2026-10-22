
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalSuppressionTests
{
    private static readonly TacticalSuppressionProfile Profile =
        new(
            dangerMarginMeters: 10,
            pressurePerThreateningRound: 2,
            recoveryPerHour: 10,
            maxFireReductionFraction: 0.8);

    private static readonly TacticalHitProfile Hit =
        new(2, 1, 0.6);

    private static readonly TacticalDefenseProfile Defense =
        new(1, 1, 1, 0, 0, 0);

    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalPerceptionState Perception,
        DiplomacySystem Diplomacy,
        CompanyDeployment Shooter,
        CompanyDeployment Target,
        Division Attacker,
        Division Defender);

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

    private static Scenario CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var attacker = MakeDivision(france, province);
        var defender = MakeDivision(germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { attacker, defender },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var field = new TacticalBattlefield();

        var sector = new BattleSector(
            1, "Plain", TerrainType.Plains);

        field.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, field);

        var shooter = battle.DeployCompany(
            attacker.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var target = battle.DeployCompany(
            defender.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(
            sector,
            centerXMeters: 50,
            centerYMeters: 0,
            radiusMeters: 500);

        geometry.PlaceCompany(
            shooter,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 20);

        geometry.PlaceCompany(
            target,
            new TacticalPoint(100, 0, 100),
            facingDegrees: 270,
            frontageMeters: 20);

        battle.PersonnelExposure.Set(
            shooter,
            new TacticalPersonnelExposure(
                exposed: 20,
                sheltered: 80,
                reserve: 0,
                exposedDepthMeters: 20));

        battle.PersonnelExposure.Set(
            target,
            new TacticalPersonnelExposure(
                exposed: 50,
                sheltered: 50,
                reserve: 0,
                exposedDepthMeters: 20));

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
            battle,
            geometry,
            perception,
            diplomacy,
            shooter,
            target,
            attacker,
            defender);
    }

    private static void FireWithoutCasualties(Scenario s)
    {
        var phase = s.Battle.DirectFire.Execute(
            new[]
            {
                new TacticalFireOrder(
                    s.Shooter,
                    s.Target,
                    new TacticalDirectFireProfile(250),
                    WeaponsCommitted: 10,
                    RoundsPerWeapon: 3)
            },
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            hour: 1);

        Assert.Single(phase.Events);

        s.Battle.DirectFire.ResolvePersonnelLastPhase(
            Hit,
            Defense,
            seed: 1);

        s.Battle.DirectFire.ResolvePersonnelEffectsLastPhase(
            new TacticalPersonnelEffectProfile(
                killedProbability: 0,
                woundedUnavailableProbability: 0),
            seed: 2);

        s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase();
    }

    [Fact]
    public void GunfireCausesSuppressionWithoutCasualties()
    {
        var s = CreateScenario();

        FireWithoutCasualties(s);

        var result = s.Battle.DirectFire
            .ResolveSuppressionLastPhase(Hit, Profile);

        var report = Assert.Single(result.Targets);

        Assert.Same(s.Target, report.Target);
        Assert.True(report.ExpectedThreateningRounds > 0);
        Assert.True(report.PressureAfter > 0);

        // Aucun mort ou blesse n'a ete applique.
        Assert.Equal(
            100,
            s.Defender.GetCurrentResourceTotals()
                .Personnel.Available);

        // Seul le vrai tir a consomme les cartouches.
        Assert.Equal(
            970,
            s.Attacker.GetCurrentResourceTotals()
                .Ammunition.SmallArms);
    }

    [Fact]
    public void DifferentShootersAccumulateOnOneTarget()
    {
        var s = CreateScenario();

        var threats = new[]
        {
            new TacticalSuppressionThreat(
                s.Target, 10, 1, 10),

            new TacticalSuppressionThreat(
                s.Target, 20, 1, 20)
        };

        var phase = s.Battle.Suppression.ApplyHour(
            1, threats, Profile);

        var report = Assert.Single(phase.Targets);

        Assert.Equal(30, report.ExpectedThreateningRounds);
        Assert.Equal(60, report.PressureAfter);
    }

    [Fact]
    public void SuppressionRecoversWithoutIncomingFire()
    {
        var s = CreateScenario();

        s.Battle.Suppression.ApplyHour(
            1,
            new[]
            {
                new TacticalSuppressionThreat(
                    s.Target, 30, 1, 30)
            },
            Profile);

        Assert.Equal(
            60,
            s.Battle.Suppression.GetProjectedPressure(
                s.Target, 1, Profile));

        var empty = s.Battle.Suppression.ApplyHour(
            2,
            Array.Empty<TacticalSuppressionThreat>(),
            Profile);

        Assert.Empty(empty.Targets);

        Assert.Equal(
            50,
            s.Battle.Suppression.GetProjectedPressure(
                s.Target, 2, Profile));
    }

    [Fact]
    public void SuppressedCompanyUsesFewerWeapons()
    {
        var s = CreateScenario();

        s.Battle.Suppression.ApplyHour(
            1,
            new[]
            {
                new TacticalSuppressionThreat(
                    s.Target, 30, 1, 30)
            },
            Profile);

        int weapons = s.Battle.Suppression.GetEffectiveWeapons(
            s.Target,
            availableWeapons: 10,
            hour: 2,
            Profile);

        // Pression 60 - recuperation 10 = 50.
        // 10 * (1 - 0.8 * 0.5) = 6.
        Assert.Equal(6, weapons);

        // Les armes n'ont pas disparu physiquement.
        Assert.Equal(
            100,
            s.Target.Company.Composition!
                .InfantryEquipment.Current);
    }

    [Fact]
    public void SameSuppressionPhaseCannotBeAppliedTwice()
    {
        var s = CreateScenario();

        FireWithoutCasualties(s);

        s.Battle.DirectFire.ResolveSuppressionLastPhase(
            Hit, Profile);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.ResolveSuppressionLastPhase(
                Hit, Profile));

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.Suppression.ApplyHour(
                1,
                Array.Empty<TacticalSuppressionThreat>(),
                Profile));
    }

    [Fact]
    public void ForeignDeploymentIsRejected()
    {
        var first = CreateScenario();
        var second = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            first.Battle.Suppression.GetProjectedPressure(
                second.Target,
                1,
                Profile));
    }

    [Fact]
    public void InvalidSuppressionParametersAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalSuppressionProfile(
                -1, 2, 10, 0.8));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalSuppressionProfile(
                10, double.NaN, 10, 0.8));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalSuppressionProfile(
                10, 2, 10, 1.2));
    }
}
