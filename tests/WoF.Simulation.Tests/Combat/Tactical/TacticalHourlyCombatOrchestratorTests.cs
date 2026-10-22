
using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalHourlyCombatOrchestratorTests
{
    private sealed record Scenario(
        SimulationEngine Engine,
        TacticalBattle Battle,
        TacticalHourlyCombatOrchestrator Orchestrator,
        TacticalSignatureFrame Signatures,
        CompanyDeployment French,
        CompanyDeployment German,
        Division FrenchDivision,
        Division GermanDivision);

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
        bool targetHasSignature = true,
        double visualRange = 500,
        int frenchExposed = 10,
        bool placeFrench = true,
        bool registerOrchestrator = true,
        double killedProbability = 1,
        bool enableSuppression = false)
    {
        var engine = new SimulationEngine();

        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var french = MakeDivision(france, province);
        var german = MakeDivision(germany, province);

        engine.AddDivision(french);
        engine.AddDivision(german);
        engine.DeclareWar(france, germany);

        // Creation operationnelle de la bataille.
        engine.Tick();

        Assert.Equal(1, engine.Clock.CurrentHour);

        var engagement = Assert.Single(
            engine.Engagements.ActiveEngagements);

        var battlefield = new TacticalBattlefield();
        var sector = new BattleSector(
            1, "Main", TerrainType.Plains);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var frenchDeployment = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var germanDeployment = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(
            sector,
            centerXMeters: 50,
            centerYMeters: 0,
            radiusMeters: 500);

        if (placeFrench)
        {
            geometry.PlaceCompany(
                frenchDeployment,
                new TacticalPoint(0, 0, 100),
                facingDegrees: 90,
                frontageMeters: 20);
        }

        geometry.PlaceCompany(
            germanDeployment,
            new TacticalPoint(100, 0, 100),
            facingDegrees: 270,
            frontageMeters: 1);

        battle.PersonnelExposure.Set(
            frenchDeployment,
            new TacticalPersonnelExposure(
                exposed: frenchExposed,
                sheltered: 100 - frenchExposed,
                reserve: 0,
                exposedDepthMeters: 20));

        battle.PersonnelExposure.Set(
            germanDeployment,
            new TacticalPersonnelExposure(
                exposed: 100,
                sheltered: 0,
                reserve: 0,
                exposedDepthMeters: 1));

        var perception = new TacticalPerceptionState(battle);
        var signatures = new TacticalSignatureFrame(battle);

        // Seule la cible allemande emet une
        // signature observable. Les Allemands
        // ne detectent donc pas automatiquement
        // les Francais dans ce scenario.
        if (targetHasSignature)
        {
            signatures.Set(
                germanDeployment,
                new TacticalUnitSignature(
                    TacticalActivity.Stationary,
                    TacticalConcealment.None));
        }

        var settings = new TacticalHourlyCombatSettings(
            detection: new TacticalDetectionProfile(
                maxVisualRangeMeters: visualRange,
                maxAcousticRangeMeters: 0,
                fieldOfViewDegrees: 360,
                localizationCellMeters: 1),
            fire: new TacticalDirectFireProfile(250),
            hit: new TacticalHitProfile(
                minimumDispersionMeters: 0.001,
                dispersionAt100Meters: 0,
                effectiveTargetRadiusMeters: 10),
            defense: new TacticalDefenseProfile(
                1, 1, 1, 0, 0, 0),

                effects: new TacticalPersonnelEffectProfile(
                killedProbability,
                woundedUnavailableProbability: 0),
                maxWeaponsPerCompany: 10,
                roundsPerWeapon: 3,
                baseSeed: 12345,
                suppression: enableSuppression
                ? new TacticalSuppressionProfile(
                    dangerMarginMeters: 10,
                    pressurePerThreateningRound: 2,
                    recoveryPerHour: 10,
                    maxFireReductionFraction: 0.8)
                    : null);

        
    

        var orchestrator = new TacticalHourlyCombatOrchestrator(
            battle,
            geometry,
            perception,
            signatures,
            engine.Diplomacy,
            settings);

        engine.Engagements.AttachTacticalBattle(battle);

        if (registerOrchestrator)
        {
            engine.Engagements.RegisterTacticalOrchestrator(
                orchestrator);
        }

        return new Scenario(
            engine,
            battle,
            orchestrator,
            signatures,
            frenchDeployment,
            germanDeployment,
            french,
            german);
    }

    [Fact]
    public void Tick_AutomaticallyExecutesCompleteTacticalPipeline()
    {
        var s = CreateScenario();

        s.Engine.Tick();

        Assert.Equal(2, s.Engine.Clock.CurrentHour);

        var result = s.Orchestrator.LastResult!.Value;

        Assert.Equal(2, result.Hour);
        Assert.Equal(1, result.OrdersIssued);
        Assert.Equal(1, result.SalvosFired);
        Assert.Equal(30, result.RoundsExpended);

        Assert.Equal(
            2, s.Battle.DirectFire.LastPhase!.Hour);

        Assert.Equal(
            2, s.Battle.DirectFire.LastPersonnelPhase!.Hour);

        Assert.Equal(
            2, s.Battle.DirectFire
                .LastPersonnelEffectsPhase!.Hour);

        Assert.Equal(
            2, s.Battle.DirectFire.LastCasualtyPhase!.Hour);
    }

    [Fact]
    public void Casualties_PropagateWithoutLegacyDoubleCounting()
    {
        var s = CreateScenario();

        int initialGerman = s.GermanDivision
            .GetCurrentResourceTotals().Personnel.Available;

        double germanOrganization =
            s.GermanDivision.Organization;

        s.Engine.Tick();

        var result = s.Orchestrator.LastResult!.Value;

        Assert.True(result.Killed > 0);
        Assert.Equal(0, result.Wounded);

        int available = s.GermanDivision
            .GetCurrentResourceTotals().Personnel.Available;

        Assert.Equal(initialGerman - result.Killed, available);

        Assert.Equal(
            available,
            s.German.Company.Composition!.Manpower.Current);

        Assert.Equal(
            available,
            s.GermanDivision.Battalions[0]
                .GetResourceTotals().Personnel.Available);

        // Le moteur operationnel n'applique pas
        // une seconde perte d'organisation ou d'hommes.
        Assert.Equal(
            germanOrganization,
            s.GermanDivision.Organization);

        Assert.Equal(
            970,
            s.FrenchDivision.GetCurrentResourceTotals()
                .Ammunition.SmallArms);
    }

    [Fact]
    public void NoTargetSignature_ProducesCompletedEmptyPhase()
    {
        var s = CreateScenario(
            targetHasSignature: false);

        s.Engine.Tick();

        var result = s.Orchestrator.LastResult!.Value;

        Assert.Equal(0, result.OrdersIssued);
        Assert.Equal(0, result.SalvosFired);
        Assert.Equal(0, result.RoundsExpended);
        Assert.Equal(0, result.Killed);
        Assert.Equal(0, result.Wounded);

        Assert.Empty(
            s.Battle.DirectFire.LastCasualtyPhase!.Companies);

        Assert.Equal(
            100,
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available);
    }

    [Fact]
    public void TargetOutsideDetectionRange_IsNotFiredUpon()
    {
        var s = CreateScenario(
            visualRange: 10);

        s.Engine.Tick();

        Assert.Equal(
            0,
            s.Orchestrator.LastResult!.Value.SalvosFired);

        Assert.Equal(
            1000,
            s.FrenchDivision.GetCurrentResourceTotals()
                .Ammunition.SmallArms);
    }

    [Fact]
    public void NoExposedShooters_ProduceNoFireOrders()
    {
        var s = CreateScenario(
            frenchExposed: 0);

        s.Engine.Tick();

        Assert.Equal(
            0,
            s.Orchestrator.LastResult!.Value.SalvosFired);

        Assert.Equal(
            100,
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available);
    }

    [Fact]
    public void ConsecutiveTicks_UseSurvivingPopulation()
    {
        var s = CreateScenario();

        s.Engine.Tick();

        int afterFirst =
            s.German.Company.Composition!.Manpower.Current;

        Assert.InRange(afterFirst, 1, 99);

        // La repartition a ete automatiquement
        // synchronisee avec les hommes survivants.
        var exposure = s.Battle.PersonnelExposure.Get(
            s.German);

        Assert.Equal(afterFirst, exposure.Total);
        Assert.Equal(afterFirst, exposure.Exposed);

        s.Engine.Tick();

        int afterSecond =
            s.German.Company.Composition!.Manpower.Current;

        Assert.Equal(3, s.Engine.Clock.CurrentHour);
        Assert.InRange(afterSecond, 0, afterFirst);

        Assert.Equal(
            afterSecond,
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available);

        Assert.Equal(
            940,
            s.FrenchDivision.GetCurrentResourceTotals()
                .Ammunition.SmallArms);
    }

    [Fact]
    public void MissingPosition_StopsTickBeforeChangingResources()
    {
        var s = CreateScenario(placeFrench: false);

        long hourBefore = s.Engine.Clock.CurrentHour;

        var frenchBefore =
            s.FrenchDivision.GetCurrentResourceTotals();

        var germanBefore =
            s.GermanDivision.GetCurrentResourceTotals();

        Assert.Throws<InvalidOperationException>(() =>
            s.Engine.Tick());

        Assert.Equal(
            hourBefore,
            s.Engine.Clock.CurrentHour);

        Assert.Equal(
            frenchBefore,
            s.FrenchDivision.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.GermanDivision.GetCurrentResourceTotals());

        Assert.Null(s.Battle.DirectFire.LastPhase);
    }

    [Fact]
    public void SameHour_CannotBeOrchestratedTwice()
    {
        var s = CreateScenario();

        s.Engine.Tick();

        Assert.Throws<InvalidOperationException>(() =>
            s.Orchestrator.Run(2));
    }

    [Fact]
    public void ForeignOrDuplicateOrchestrator_IsRejected()
    {
        var owner = CreateScenario();
        var foreign = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            owner.Engine.Engagements.RegisterTacticalOrchestrator(
                foreign.Orchestrator));

        Assert.Throws<InvalidOperationException>(() =>
            owner.Engine.Engagements.RegisterTacticalOrchestrator(
                owner.Orchestrator));
    }

    [Fact]
    public void TacticalBattleWithoutOrchestrator_RemainsManual()
    {
        var s = CreateScenario(
            registerOrchestrator: false);

        Assert.Throws<InvalidOperationException>(() =>
            s.Engine.Tick());

        Assert.Equal(1, s.Engine.Clock.CurrentHour);
        Assert.Null(s.Battle.DirectFire.LastPhase);
    }

    [Fact]
    public void ZeroLethality_ConsumesRoundsButNotPersonnel()
    {
        var s = CreateScenario(
            killedProbability: 0);

        s.Engine.Tick();

        Assert.Equal(
            30,
            s.Orchestrator.LastResult!.Value.RoundsExpended);

        Assert.Equal(
            0,
            s.Orchestrator.LastResult!.Value.Killed);

        Assert.Equal(
            100,
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available);

        Assert.Equal(
            970,
            s.FrenchDivision.GetCurrentResourceTotals()
                .Ammunition.SmallArms);
    }



    [Fact]
    public void SuppressionReducesFireInFollowingTick()
    {
        var s = CreateScenario(
            killedProbability: 0,
            enableSuppression: true);

        // Les deux camps deviennent observables.
        // Ils pourront donc tous les deux tirer.
        s.Signatures.Set(
            s.French,
            new TacticalUnitSignature(
                TacticalActivity.Stationary,
                TacticalConcealment.None));

        s.Engine.Tick();

        var first = s.Orchestrator.LastResult!.Value;

        Assert.Equal(2, first.SalvosFired);
        Assert.Equal(60, first.RoundsExpended);

        // Les deux compagnies ont ete prises sous le feu.
        Assert.True(
            s.Battle.Suppression.GetProjectedPressure(
                s.French, 3,
                new TacticalSuppressionProfile(10, 2, 10, 0.8)) > 0);

        s.Engine.Tick();

        var second = s.Orchestrator.LastResult!.Value;

        Assert.Equal(3, second.Hour);

        Assert.True(
            second.RoundsExpended < first.RoundsExpended);

        // Aucune perte humaine : la baisse provient
        // uniquement de la suppression.
        Assert.Equal(
            100,
            s.FrenchDivision.GetCurrentResourceTotals()
                .Personnel.Available);

        Assert.Equal(
            100,
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available);
    }

}
