
using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using WoF.Simulation.Military.Combat.Losses;


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
        bool enableSuppression = false,
        bool enableCohesion = false,
        bool enableBattalionAssessment = false,
        bool enableBattalionReactions = false,
        TacticalPhaseTimingRecorder? timingRecorder = null,
        double cohesionRecoveryPerHour = 0)
    {
        var engine = new SimulationEngine(cohesionRecoveryPerHour);

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
                    : null,

                cohesion: enableCohesion
                    ? new TacticalCohesionProfile(
                        lossPerCasualtyPercent: 1.5,
                        sustainedSuppressionThreshold: 40,
                        lossPerExcessPressurePoint: 0.1,
                        recoveryPerQuietHour: 3,
                        recoverySuppressionCeiling: 20,
                        maxFireReductionFraction: 0.5)
                    : null,

                    battalionAssessment: enableBattalionAssessment
                    ? new TacticalBattalionAssessmentProfile(
                        frontLineWeight: 3,
                        supportWeight: 2,
                        reserveWeight: 1,
                        priorityFrontMultiplier: 2,
                        holdThreshold: 60,
                        riskThreshold: 30)
                    : null,

                    battalionReactions: enableBattalionReactions
                    ? new TacticalBattalionReactionProfile(
                        criticalConfirmationHours: 2,
                        recoveryConfirmationHours: 2,
                        recoveryMargin: 10)
                    : null
                    );

        
    

        var orchestrator = new TacticalHourlyCombatOrchestrator(
            battle,
            geometry,
            perception,
            signatures,
            engine.Diplomacy,
            settings,
            timingRecorder);

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
        Assert.Null(s.Orchestrator.LastCohesionPhase);

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




    [Fact]
    public void SustainedTacticalFireDegradesCompanyCohesionAcrossTicks()
    {
        var s = CreateScenario(
            killedProbability: 0,
            enableSuppression: true,
            enableCohesion: true);

        // Heure 2 : premiere salve.
        s.Engine.Tick();

        Assert.Equal(2, s.Engine.Clock.CurrentHour);

        Assert.Equal(
            100,
            s.Battle.Cohesion.Get(s.German));

        Assert.Equal(
            100,
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available);

        var firstPhase = Assert.IsType<TacticalCohesionPhaseResult>(
            s.Orchestrator.LastCohesionPhase);
        Assert.Equal(2, firstPhase.Hour);
        var firstReports = firstPhase.Companies.ToArray();

        // Heure 3 : feu continu sur la meme compagnie.
        // La suppression preexistante provoque
        // maintenant une perte de cohesion.
        s.Engine.Tick();

        Assert.Equal(3, s.Engine.Clock.CurrentHour);

        var secondPhase = Assert.IsType<TacticalCohesionPhaseResult>(
            s.Orchestrator.LastCohesionPhase);
        Assert.Equal(3, secondPhase.Hour);
        Assert.NotSame(firstPhase, secondPhase);
        Assert.Equal(firstReports, firstPhase.Companies);

        double germanCohesion =
            s.Battle.Cohesion.Get(s.German);

        Assert.InRange(germanCohesion, 0, 99.999);

        // Le defenseur est desorganise, mais il
        // n'a perdu aucun homme dans ce scenario.
        Assert.Equal(
            100,
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available);

        // La compagnie francaise n'a pas subi de feu.
        Assert.Equal(
            100,
            s.Battle.Cohesion.Get(s.French));

        // La baisse de cohesion reduit les armes
        // qui pourront etre engagees ensuite.
        var profile = new TacticalCohesionProfile(
            lossPerCasualtyPercent: 1.5,
            sustainedSuppressionThreshold: 40,
            lossPerExcessPressurePoint: 0.1,
            recoveryPerQuietHour: 3,
            recoverySuppressionCeiling: 20,
            maxFireReductionFraction: 0.5);

        int effectiveWeapons =
            s.Battle.Cohesion.GetEffectiveWeapons(
                s.German,
                availableWeapons: 10,
                profile);

        Assert.True(effectiveWeapons < 10);

        // Les armes physiques n'ont pas disparu.
        Assert.Equal(
            100,
            s.German.Company.Composition!
                .InfantryEquipment.Current);

        // Une seule compagnie tire dans le scenario :
        // 30 cartouches par heure.
        Assert.Equal(
            940,
            s.FrenchDivision.GetCurrentResourceTotals()
                .Ammunition.SmallArms);
    }


    [Fact]
    public void HourlyTickProducesBattalionAssessment()
    {
        var s = CreateScenario(
            enableSuppression: true,
            enableCohesion: true,
            enableBattalionAssessment: true);

        var battalion = s.German.Company.ParentBattalion!;

        s.Battle.BattalionAssessments.SetPrioritySector(
            battalion,
            s.German.Sector);

        // Le tick produit d'abord les consequences
        // aux compagnies puis les bilans de bataillons.
        s.Engine.Tick();

        Assert.Equal(2, s.Engine.Clock.CurrentHour);

        var reports = s.Orchestrator.LastBattalionAssessments;

        // Un bataillon francais, un bataillon allemand.
        Assert.Equal(2, reports.Count);

        var german = Assert.Single(reports, r =>
            ReferenceEquals(r.Battalion, battalion));

        int available =
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available;

        Assert.Equal(available, german.AvailablePersonnel);

        Assert.Equal(
            s.Battle.Cohesion.Get(s.German),
            german.Cohesion,
            6);

        Assert.Equal(
            german.Cohesion * available / 100.0,
            german.CombatReadiness,
            6);

        Assert.Equal(
            german.CombatReadiness,
            german.PrioritySectorReadiness!.Value,
            6);

        Assert.NotEqual(
            TacticalBattalionMissionStatus.Unassigned,
            german.MissionStatus);

        // L'evaluation ne cree aucune perte supplementaire.
        Assert.Equal(
            available,
            s.German.Company.Composition!.Manpower.Current);
    }

    [Fact]
    public void ConsecutiveTicksRefreshBattalionAssessment()
    {
        var s = CreateScenario(
            enableSuppression: true,
            enableCohesion: true,
            enableBattalionAssessment: true);

        var battalion = s.German.Company.ParentBattalion!;

        s.Battle.BattalionAssessments.SetPrioritySector(
            battalion,
            s.German.Sector);

        s.Engine.Tick();

        var first = Assert.Single(
            s.Orchestrator.LastBattalionAssessments,
            r => ReferenceEquals(r.Battalion, battalion));

        s.Engine.Tick();

        var second = Assert.Single(
            s.Orchestrator.LastBattalionAssessments,
            r => ReferenceEquals(r.Battalion, battalion));

        Assert.Equal(3, s.Engine.Clock.CurrentHour);

        // Les effectifs et la cohesion reels sont
        // relus apres chaque heure de combat.
        Assert.True(
            second.AvailablePersonnel <= first.AvailablePersonnel);

        Assert.Equal(
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available,
            second.AvailablePersonnel);

        Assert.Equal(
            s.Battle.Cohesion.Get(s.German),
            second.Cohesion,
            6);

        Assert.True(
            second.CombatReadiness <= first.CombatReadiness);

        // Le bilan de l'heure precedente reste une
        // photographie, et n'est pas modifie en place.
        Assert.Equal(
            first.AvailablePersonnel,
            first.AvailablePersonnel);
    }




    [Fact]
    public void ConsecutiveTicksConfirmRuptureAndRequestWithdrawal()
    {
        var s = CreateScenario(
            killedProbability: 0,
            enableSuppression: true,
            enableCohesion: true,
            enableBattalionAssessment: true,
            enableBattalionReactions: true);

        var battalion = s.German.Company.ParentBattalion!;
        var initialSector = s.German.Sector;

        s.Battle.BattalionAssessments.SetPrioritySector(
            battalion,
            s.German.Sector);

        // Preparer une compagnie fortement affaiblie
        // AVANT le premier tick de ce test.
        var pool = s.German.Company.Composition!.Manpower;

        pool.ApplyCasualties(new PersonnelCasualtyReport(
            KilledInAction: 80,
            WoundedInAction: 0,
            MissingOrCaptured: 0));

        // Mettre en accord l'exposition avec
        // les vingt survivants.
        s.Battle.PersonnelExposure.Set(
            s.German,
            new TacticalPersonnelExposure(
                exposed: 20,
                sheltered: 0,
                reserve: 0,
                exposedDepthMeters: 1));

        // Heure 2 : incapacite critique observee.
        s.Engine.Tick();

        var first = s.Orchestrator.LastBattalionReactionPhase!;

        var firstReport = Assert.Single(
            first.Battalions,
            r => ReferenceEquals(r.Battalion, battalion));

        Assert.Equal(
            TacticalBattalionFrontState.Threatened,
            firstReport.State);

        Assert.Equal(1, firstReport.ConsecutiveCriticalHours);

        Assert.Equal(
            TacticalBattalionReactionType.Reorganize,
            Assert.Single(
                first.Requests,
                r => ReferenceEquals(
                    r.Battalion, battalion)).Type);

        // Heure 3 : seconde confirmation.
        s.Engine.Tick();

        var second = s.Orchestrator.LastBattalionReactionPhase!;

        var secondReport = Assert.Single(
            second.Battalions,
            r => ReferenceEquals(r.Battalion, battalion));

        Assert.Equal(
            TacticalBattalionFrontState.RuptureConfirmed,
            secondReport.State);

        Assert.Equal(2, secondReport.ConsecutiveCriticalHours);

        // Aucun reserviste dans ce bataillon :
        // le commandement recoit une demande de repli.
        var request = Assert.Single(
            second.Requests,
            r => ReferenceEquals(r.Battalion, battalion));

        Assert.Equal(
            TacticalBattalionReactionType.RequestWithdrawal,
            request.Type);

        // Mais la bataille n'a pas physiquement
        // deplace ou rompu toute la division.
        Assert.Same(initialSector, s.German.Sector);
        Assert.False(s.GermanDivision.IsBroken);

        // Les pertes preparees en amont ne sont
        // pas appliquees une seconde fois.
        Assert.Equal(20, pool.Current);

        Assert.Equal(
            20,
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available);
    }




    [Fact]
    public void InstrumentedTickRecordsExecutedPhases()
    {
        var recorder = new TacticalPhaseTimingRecorder();

        var s = CreateScenario(
            enableSuppression: true,
            enableCohesion: true,
            enableBattalionAssessment: true,
            enableBattalionReactions: true,
            timingRecorder: recorder);

        s.Engine.Tick();

        var measurements = recorder.Snapshot();

        var expectedPhases = new[]
        {
            TacticalBenchmarkPhase.Validation,
            TacticalBenchmarkPhase.MobileParticipantPreparation,
            TacticalBenchmarkPhase.Detection,
            TacticalBenchmarkPhase.FireOrderPlanning,
            TacticalBenchmarkPhase.FireExecution,
            TacticalBenchmarkPhase.PersonnelContacts,
            TacticalBenchmarkPhase.PersonnelEffects,
            TacticalBenchmarkPhase.CasualtyApplication,
            TacticalBenchmarkPhase.ExposureReconciliation,
            TacticalBenchmarkPhase.Suppression,
            TacticalBenchmarkPhase.Cohesion,
            TacticalBenchmarkPhase.BattalionAssessment,
            TacticalBenchmarkPhase.BattalionReactions
        };

        Assert.Equal(
            expectedPhases.Length,
            measurements.Count);

        foreach (var phase in expectedPhases)
        {
            var measurement = Assert.Single(
                measurements,
                m => m.Phase == phase);

            Assert.Equal(1, measurement.Calls);

            Assert.True(double.IsFinite(
                measurement.Milliseconds));

            Assert.True(measurement.Milliseconds >= 0);

            Assert.True(measurement.AllocatedBytes >= 0);
        }
    }

    [Fact]
    public void InstrumentationDoesNotChangeMilitaryResults()
    {
        var recorder = new TacticalPhaseTimingRecorder();

        var normal = CreateScenario(
            enableSuppression: true,
            enableCohesion: true,
            enableBattalionAssessment: true);

        var measured = CreateScenario(
            enableSuppression: true,
            enableCohesion: true,
            enableBattalionAssessment: true,
            timingRecorder: recorder);

        normal.Engine.Tick();
        measured.Engine.Tick();

        Assert.Equal(
            normal.Orchestrator.LastResult,
            measured.Orchestrator.LastResult);

        Assert.Equal(
            normal.French.Company.Composition!.Manpower.Current,
            measured.French.Company.Composition!.Manpower.Current);

        Assert.Equal(
            normal.German.Company.Composition!.Manpower.Current,
            measured.German.Company.Composition!.Manpower.Current);

        Assert.Equal(
            normal.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms),
            measured.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));

        Assert.Equal(
            normal.Battle.Cohesion.Get(normal.French),
            measured.Battle.Cohesion.Get(measured.French));

        Assert.Equal(
            normal.Battle.DirectFire.LastPhase!.Events.Count,
            measured.Battle.DirectFire.LastPhase!.Events.Count);

        Assert.NotEmpty(recorder.Snapshot());
    }


    [Fact]
    public void FailedHourKeepsLastCompletedCohesionPhase()
    {
        var s = CreateScenario(
            enableSuppression: true,
            enableCohesion: true);
        s.Engine.Tick();
        var previous = Assert.IsType<TacticalCohesionPhaseResult>(
            s.Orchestrator.LastCohesionPhase);

        // Preparer la suppression de l'heure suivante pour provoquer
        // un rejet apres le tir et l'application des pertes.
        long hour = previous.Hour + 1;
        s.Battle.Suppression.ApplyHour(
            hour, Array.Empty<TacticalSuppressionThreat>(),
            new TacticalSuppressionProfile(10, 2, 10, 0.8));

        Assert.Throws<InvalidOperationException>(() =>
            s.Orchestrator.Run(hour));

        Assert.Equal(hour, s.Battle.DirectFire.LastCasualtyPhase!.Hour);
        Assert.Equal(previous.Hour, s.Orchestrator.LastResult!.Value.Hour);
        Assert.Same(previous, s.Orchestrator.LastCohesionPhase);
    }

    [Fact]
    public void CompanyCohesionSurvivesTwoDistinctEngineEngagements()
    {
        var s = CreateScenario(enableSuppression: true, enableCohesion: true);
        var company = s.German.Company;
        int initialManpower = company.Composition!.Manpower.Current;
        double divisionOrganization = s.GermanDivision.Organization;
        var suppressionProfile = new TacticalSuppressionProfile(10, 2, 10, 0.8);
        var cohesionProfile = new TacticalCohesionProfile(1.5, 40, 0.1, 3, 20, 0.5);

        s.Engine.Tick();

        var firstEngagement = s.Battle.Engagement;
        var firstPhase = Assert.IsType<TacticalCohesionPhaseResult>(
            s.Orchestrator.LastCohesionPhase);
        var firstReports = firstPhase.Companies.ToArray();
        var firstCompanyReport = Assert.Single(firstPhase.Companies,
            report => ReferenceEquals(report.Company, s.German));
        var firstCasualty = Assert.Single(
            s.Battle.DirectFire.LastCasualtyPhase!.Companies,
            report => ReferenceEquals(report.Target, s.German));
        Assert.True(firstCasualty.Casualties.Total > 0);
        Assert.Equal(initialManpower - firstCasualty.Casualties.Total,
            company.Composition.Manpower.Current);
        Assert.True(company.Cohesion < 100);
        Assert.Equal(company.Cohesion, firstCompanyReport.CohesionAfter);
        Assert.True(s.Battle.Suppression.GetProjectedPressure(
            s.German, firstPhase.Hour, suppressionProfile) > 0);

        double inheritedCohesion = company.Cohesion;
        int inheritedManpower = company.Composition.Manpower.Current;
        var inheritedResources = s.GermanDivision.GetCurrentResourceTotals();

        // Provoquer la cloture par les regles operationnelles existantes.
        s.FrenchDivision.LoseOrganization(s.FrenchDivision.Organization);
        Assert.True(s.FrenchDivision.IsBroken);
        s.Engine.Tick();

        Assert.False(firstEngagement.IsActive);
        Assert.Equal(s.Engine.Clock.CurrentHour, firstEngagement.EndHour);
        Assert.Same(s.GermanDivision.Country, firstEngagement.WinnerCountry);
        Assert.Same(firstEngagement, Assert.Single(s.Engine.Engagements.EndedThisTick));
        Assert.Equal(firstEngagement.Id,
            Assert.Single(s.Engine.Engagements.History.Reports).EngagementId);
        Assert.Empty(s.Engine.Engagements.ActiveEngagements);
        Assert.Null(s.GermanDivision.CurrentEngagement);
        Assert.Equal(inheritedCohesion, company.Cohesion);
        Assert.Equal(inheritedResources, s.GermanDivision.GetCurrentResourceTotals());
        Assert.Same(firstPhase, s.Orchestrator.LastCohesionPhase);
        Assert.Throws<InvalidOperationException>(() =>
            s.Engine.Engagements.AttachTacticalBattle(s.Battle));

        var province = s.GermanDivision.CurrentProvince!;
        var reinforcement = MakeDivision(s.FrenchDivision.Country!, province);
        var newFrenchCompany = reinforcement.Battalions[0].Companies[0];
        Assert.Equal(100, newFrenchCompany.Cohesion);
        s.Engine.AddDivision(reinforcement);

        // Le nouveau combat est cree en fin de tick, avant tout tir.
        s.Engine.Tick();

        var secondEngagement = Assert.Single(s.Engine.Engagements.ActiveEngagements);
        Assert.NotSame(firstEngagement, secondEngagement);
        Assert.NotEqual(firstEngagement.Id, secondEngagement.Id);
        Assert.Same(secondEngagement, s.GermanDivision.CurrentEngagement);
        Assert.Same(secondEngagement, reinforcement.CurrentEngagement);
        Assert.DoesNotContain(s.FrenchDivision, secondEngagement.Participants);
        Assert.Equal(inheritedResources, s.GermanDivision.GetCurrentResourceTotals());
        Assert.Equal(inheritedCohesion, company.Cohesion);
        Assert.Same(firstPhase, s.Orchestrator.LastCohesionPhase);

        var field = new TacticalBattlefield();
        var sector = new BattleSector(1, "Second battle", TerrainType.Plains);
        field.AddSector(sector);
        var battle = new TacticalBattle(secondEngagement, field);
        var french = battle.DeployCompany(
            newFrenchCompany, sector, CompanyDeploymentRole.FrontLine);
        var german = battle.DeployCompany(
            company, sector, CompanyDeploymentRole.FrontLine);

        Assert.NotSame(s.German, german);
        Assert.Same(company, german.Company);
        Assert.Same(s.GermanDivision.Battalions[0], company.ParentBattalion);
        Assert.Equal(inheritedManpower, german.Company.Composition!.Manpower.Current);
        Assert.Equal(inheritedCohesion, battle.Cohesion.Get(german));
        Assert.Throws<InvalidOperationException>(() => battle.Cohesion.Get(s.German));
        Assert.Throws<InvalidOperationException>(() => s.Battle.Cohesion.Get(german));
        Assert.NotSame(s.Battle.Suppression, battle.Suppression);
        Assert.Equal(0, battle.Suppression.GetProjectedPressure(
            german, secondEngagement.StartHour, suppressionProfile));
        Assert.Null(battle.DirectFire.LastPhase);
        Assert.Null(battle.DirectFire.LastCasualtyPhase);
        Assert.Null(battle.DirectFire.LastSuppressionPhase);

        var geometry = new TacticalGeometryMap(battle);
        geometry.DefineSector(sector, 50, 0, 500);
        geometry.PlaceCompany(french, new TacticalPoint(0, 0, 100), 90, 20);
        geometry.PlaceCompany(german, new TacticalPoint(100, 0, 100), 270, 1);
        battle.PersonnelExposure.Set(french, new TacticalPersonnelExposure(10, 90, 0, 20));
        battle.PersonnelExposure.Set(german,
            new TacticalPersonnelExposure(inheritedManpower, 0, 0, 1));
        var signatures = new TacticalSignatureFrame(battle);
        signatures.Set(german, new TacticalUnitSignature(
            TacticalActivity.Stationary, TacticalConcealment.None));
        var settings = new TacticalHourlyCombatSettings(
            detection: new TacticalDetectionProfile(500, 0, 360, 1),
            fire: new TacticalDirectFireProfile(250),
            hit: new TacticalHitProfile(0.001, 0, 10),
            defense: new TacticalDefenseProfile(1, 1, 1, 0, 0, 0),
            effects: new TacticalPersonnelEffectProfile(1, 0),
            maxWeaponsPerCompany: 10,
            roundsPerWeapon: 3,
            baseSeed: 12345,
            suppression: suppressionProfile,
            cohesion: cohesionProfile);
        var orchestrator = new TacticalHourlyCombatOrchestrator(
            battle, geometry, new TacticalPerceptionState(battle),
            signatures, s.Engine.Diplomacy, settings);
        Assert.Null(orchestrator.LastResult);
        Assert.Null(orchestrator.LastCohesionPhase);
        s.Engine.Engagements.AttachTacticalBattle(battle);
        s.Engine.Engagements.RegisterTacticalOrchestrator(orchestrator);

        s.Engine.Tick();

        var secondPhase = Assert.IsType<TacticalCohesionPhaseResult>(
            orchestrator.LastCohesionPhase);
        var secondCompanyReport = Assert.Single(secondPhase.Companies,
            report => ReferenceEquals(report.Company, german));
        var secondCasualty = Assert.Single(battle.DirectFire.LastCasualtyPhase!.Companies,
            report => ReferenceEquals(report.Target, german));
        Assert.Equal(s.Engine.Clock.CurrentHour, secondPhase.Hour);
        Assert.True(secondPhase.Hour > firstPhase.Hour);
        Assert.True(secondCasualty.Casualties.Total > 0);
        Assert.Equal(inheritedCohesion, secondCompanyReport.CohesionBefore);
        Assert.Equal(secondCompanyReport.CohesionAfter, company.Cohesion);
        Assert.Equal(0, secondCompanyReport.LossFromSustainedFire);
        Assert.Equal(inheritedManpower, secondCasualty.AvailableBefore);
        Assert.Equal(inheritedManpower - secondCasualty.Casualties.Total,
            company.Composition.Manpower.Current);
        Assert.Equal(company.Composition.Manpower.Current,
            company.ParentBattalion!.GetResourceTotals().Personnel.Available);
        Assert.Equal(company.Composition.Manpower.Current,
            s.GermanDivision.GetCurrentResourceTotals().Personnel.Available);
        Assert.Equal(divisionOrganization, s.GermanDivision.Organization);
        Assert.False(s.GermanDivision.IsBroken);
        Assert.Equal(1, orchestrator.LastResult!.Value.SalvosFired);
        Assert.Equal(30, orchestrator.LastResult.Value.RoundsExpended);
        Assert.Equal(0, Assert.Single(battle.DirectFire.LastSuppressionPhase!.Targets,
            report => ReferenceEquals(report.Target, german)).PressureBefore);

        double afterCohesion = company.Cohesion;
        int afterManpower = company.Composition.Manpower.Current;
        Assert.Throws<InvalidOperationException>(() => orchestrator.Run(secondPhase.Hour));
        Assert.Throws<InvalidOperationException>(() =>
            battle.Cohesion.ApplyHour(
                secondPhase.Hour, battle.DirectFire.LastCasualtyPhase!,
                battle.DirectFire.LastSuppressionPhase!, suppressionProfile, cohesionProfile));
        Assert.Equal(afterCohesion, company.Cohesion);
        Assert.Equal(afterManpower, company.Composition.Manpower.Current);
        Assert.Same(secondPhase, orchestrator.LastCohesionPhase);
        Assert.Same(firstPhase, s.Orchestrator.LastCohesionPhase);
        Assert.Equal(firstReports, firstPhase.Companies);
        Assert.Equal(inheritedCohesion, firstCompanyReport.CohesionAfter);
        Assert.Single(s.Engine.Engagements.History.Reports);
    }

    [Fact]
    public void FirstSalvoPublishesCohesionLossWithoutDoubleCounting()
    {
        var s = CreateScenario(
            killedProbability: 1,
            enableSuppression: true,
            enableCohesion: true);

        Assert.Null(s.Orchestrator.LastCohesionPhase);

        int initialManpower =
            s.German.Company.Composition!.Manpower.Current;

        double initialDivisionOrganization =
            s.GermanDivision.Organization;

        // Executer la chaine tactique complete.
        s.Engine.Tick();

        var phase = Assert.IsType<TacticalCohesionPhaseResult>(
            s.Orchestrator.LastCohesionPhase);

        Assert.Equal(2, phase.Hour);

        // Recuperer les pertes reellement appliquees.
        var casualty = Assert.Single(
            s.Battle.DirectFire.LastCasualtyPhase!.Companies,
            report => ReferenceEquals(
                report.Target, s.German));

        Assert.True(casualty.Casualties.Total > 0);

        var german = Assert.Single(
            phase.Companies,
            report => ReferenceEquals(
                report.Company, s.German));

        var french = Assert.Single(
            phase.Companies,
            report => ReferenceEquals(
                report.Company, s.French));

        // Formule du profil configure dans CreateScenario :
        // pourcentage de pertes * 1.5.
        double expectedLoss = Math.Min(
            100.0,
            100.0 * casualty.Casualties.Total
                / casualty.AvailableBefore * 1.5);

        Assert.Equal(100.0, german.CohesionBefore, 6);

        Assert.Equal(
            expectedLoss,
            german.LossFromCasualties,
            6);

        // Premiere salve : aucune pression preexistante.
        Assert.Equal(
            0.0,
            german.LossFromSustainedFire,
            6);

        Assert.Equal(0.0, german.Recovered, 6);

        Assert.Equal(
            100.0 - expectedLoss,
            german.CohesionAfter,
            6);

        Assert.Equal(
            german.CohesionAfter,
            s.Battle.Cohesion.Get(s.German),
            6);

        // La compagnie non touchee conserve sa cohesion.
        Assert.Equal(100.0, french.CohesionAfter, 6);
        Assert.Equal(0.0, french.LossFromCasualties, 6);
        Assert.Equal(
            100.0,
            s.Battle.Cohesion.Get(s.French),
            6);

        // Les pertes physiques ne sont appliquees
        // qu'une seule fois et propagees par aggregation.
        int expectedManpower =
            initialManpower - casualty.Casualties.Total;

        Assert.Equal(
            expectedManpower,
            s.German.Company.Composition!.Manpower.Current);

        Assert.Equal(
            expectedManpower,
            s.German.Company.ParentBattalion!
                .GetResourceTotals().Personnel.Available);

        Assert.Equal(
            expectedManpower,
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available);

        // La cohesion tactique ne modifie pas directement
        // l'organisation operationnelle de la division.
        Assert.Equal(
            initialDivisionOrganization,
            s.GermanDivision.Organization);
    }


    [Fact]
    public void TacticalClosureDoesNotRecoverCohesionTwice()
    {
        var s = CreateScenario(
            registerOrchestrator: false,
            killedProbability: 1,
            enableSuppression: true,
            enableCohesion: true,
            cohesionRecoveryPerHour: 3);

        // Rendre le territoire sur pour le
        // survivant, afin que l'exclusion de
        // l'heure de cloture soit vraiment testee.
        s.GermanDivision.CurrentProvince!
            .ChangeController(s.GermanDivision.Country);

        var engagement = s.Battle.Engagement;

        Assert.Equal(1, s.Engine.Clock.CurrentHour);

        // Execution manuelle de la phase tactique
        // pour l'heure 2, avant le tick operationnel.
        s.Orchestrator.Run(2);

        var phase = Assert.IsType<TacticalCohesionPhaseResult>(
            s.Orchestrator.LastCohesionPhase);

        var germanReport = Assert.Single(
            phase.Companies,
            r => ReferenceEquals(r.Company, s.German));

        Assert.True(germanReport.LossFromCasualties > 0);

        double afterCombat = germanReport.CohesionAfter;

        Assert.Equal(
            afterCombat,
            s.German.Company.Cohesion,
            6);

        // Provoquer la fin de la bataille
        // sans ajouter de nouvelles regles
        // de rupture ou de victoire.
        s.FrenchDivision.LoseOrganization(
            s.FrenchDivision.Organization);

        s.Engine.Tick();

        Assert.Equal(2, s.Engine.Clock.CurrentHour);
        Assert.False(engagement.IsActive);

        Assert.Contains(
            s.Engine.Engagements.EndedThisTick,
            e => ReferenceEquals(e, engagement));

        // Le service hors combat ne doit pas
        // ajouter 3 points a une cohesion deja
        // traitee pour cette heure.
        Assert.Equal(
            afterCombat,
            s.German.Company.Cohesion,
            6);

        Assert.Same(
            phase,
            s.Orchestrator.LastCohesionPhase);
    }


    [Fact]
    public void CompanyRecoversBetweenDistinctTacticalEngagements()
    {
        const double recoveryPerHour = 3;
        var s = CreateScenario(
            enableSuppression: true,
            enableCohesion: true,
            cohesionRecoveryPerHour: recoveryPerHour);
        var company = s.German.Company;
        int initialManpower = company.Composition!.Manpower.Current;
        double divisionOrganization = s.GermanDivision.Organization;
        var suppressionProfile = new TacticalSuppressionProfile(10, 2, 10, 0.8);
        var cohesionProfile = new TacticalCohesionProfile(1.5, 40, 0.1, 3, 20, 0.5);

        s.Engine.Tick();

        var firstEngagement = s.Battle.Engagement;
        var firstPhase = Assert.IsType<TacticalCohesionPhaseResult>(
            s.Orchestrator.LastCohesionPhase);
        Assert.Equal(2, firstPhase.Hour);
        var firstReports = firstPhase.Companies.ToArray();
        var firstCompanyReport = Assert.Single(firstPhase.Companies,
            report => ReferenceEquals(report.Company, s.German));
        var firstCasualty = Assert.Single(
            s.Battle.DirectFire.LastCasualtyPhase!.Companies,
            report => ReferenceEquals(report.Target, s.German));
        Assert.True(firstCasualty.Casualties.Total > 0);
        Assert.Equal(initialManpower - firstCasualty.Casualties.Total,
            company.Composition.Manpower.Current);
        Assert.True(company.Cohesion < 100);
        Assert.Equal(company.Cohesion, firstCompanyReport.CohesionAfter);
        Assert.True(s.Battle.Suppression.GetProjectedPressure(
            s.German, firstPhase.Hour, suppressionProfile) > 0);

        double inheritedCohesion = company.Cohesion;
        int inheritedManpower = company.Composition.Manpower.Current;
        var inheritedResources = s.GermanDivision.GetCurrentResourceTotals();

        // Provoquer la cloture par les regles operationnelles existantes.
        s.FrenchDivision.LoseOrganization(s.FrenchDivision.Organization);
        Assert.True(s.FrenchDivision.IsBroken);
        s.Engine.Tick();

        long closureHour = s.Engine.Clock.CurrentHour;
        Assert.Equal(firstPhase.Hour + 1, closureHour);
        Assert.False(firstEngagement.IsActive);
        Assert.Equal(closureHour, firstEngagement.EndHour);
        Assert.Same(s.GermanDivision.Country, firstEngagement.WinnerCountry);
        Assert.Same(firstEngagement, Assert.Single(s.Engine.Engagements.EndedThisTick));
        Assert.Equal(firstEngagement.Id,
            Assert.Single(s.Engine.Engagements.History.Reports).EngagementId);
        Assert.Empty(s.Engine.Engagements.ActiveEngagements);
        Assert.Null(s.GermanDivision.CurrentEngagement);
        Assert.Equal(inheritedCohesion, company.Cohesion);
        Assert.Equal(inheritedResources, s.GermanDivision.GetCurrentResourceTotals());
        Assert.Same(firstPhase, s.Orchestrator.LastCohesionPhase);
        Assert.Throws<InvalidOperationException>(() =>
            s.Engine.Engagements.AttachTacticalBattle(s.Battle));

        var province = Assert.IsType<Province>(s.GermanDivision.CurrentProvince);
        Assert.Same(s.GermanDivision.Country, province.Controller);

        double cohesionAtClosure = company.Cohesion;
        double expectedCohesion = Math.Min(100, cohesionAtClosure + recoveryPerHour);
        s.Engine.Tick();

        Assert.Equal(closureHour + 1, s.Engine.Clock.CurrentHour);
        Assert.Empty(s.Engine.Engagements.ActiveEngagements);
        Assert.Null(s.GermanDivision.CurrentEngagement);
        Assert.True(company.Cohesion > cohesionAtClosure);
        Assert.Equal(expectedCohesion, company.Cohesion);
        Assert.Equal(inheritedResources, s.GermanDivision.GetCurrentResourceTotals());
        Assert.Equal(inheritedManpower, company.Composition.Manpower.Current);

        var reinforcement = MakeDivision(s.FrenchDivision.Country!, province);
        var newFrenchCompany = reinforcement.Battalions[0].Companies[0];
        Assert.Equal(100, newFrenchCompany.Cohesion);
        s.Engine.AddDivision(reinforcement);

        // Le nouveau combat est cree en fin de tick, avant tout tir.
        s.Engine.Tick();

        var secondEngagement = Assert.Single(s.Engine.Engagements.ActiveEngagements);
        Assert.NotSame(firstEngagement, secondEngagement);
        Assert.NotEqual(firstEngagement.Id, secondEngagement.Id);
        Assert.Equal(closureHour + 2, secondEngagement.StartHour);
        Assert.Same(secondEngagement, s.GermanDivision.CurrentEngagement);
        Assert.Same(secondEngagement, reinforcement.CurrentEngagement);
        Assert.DoesNotContain(s.FrenchDivision, secondEngagement.Participants);
        Assert.Equal(inheritedResources, s.GermanDivision.GetCurrentResourceTotals());
        Assert.Equal(expectedCohesion, company.Cohesion);
        Assert.Same(firstPhase, s.Orchestrator.LastCohesionPhase);

        var field = new TacticalBattlefield();
        var sector = new BattleSector(1, "Second battle", TerrainType.Plains);
        field.AddSector(sector);
        var battle = new TacticalBattle(secondEngagement, field);
        var french = battle.DeployCompany(
            newFrenchCompany, sector, CompanyDeploymentRole.FrontLine);
        var german = battle.DeployCompany(
            company, sector, CompanyDeploymentRole.FrontLine);

        Assert.NotSame(s.German, german);
        Assert.Same(company, german.Company);
        Assert.Same(s.GermanDivision.Battalions[0], company.ParentBattalion);
        Assert.Equal(inheritedManpower, german.Company.Composition!.Manpower.Current);
        Assert.Equal(expectedCohesion, battle.Cohesion.Get(german));
        Assert.Throws<InvalidOperationException>(() => battle.Cohesion.Get(s.German));
        Assert.Throws<InvalidOperationException>(() => s.Battle.Cohesion.Get(german));
        Assert.NotSame(s.Battle.Suppression, battle.Suppression);
        Assert.Equal(0, battle.Suppression.GetProjectedPressure(
            german, secondEngagement.StartHour, suppressionProfile));
        Assert.Null(battle.DirectFire.LastPhase);
        Assert.Null(battle.DirectFire.LastCasualtyPhase);
        Assert.Null(battle.DirectFire.LastSuppressionPhase);

        var geometry = new TacticalGeometryMap(battle);
        geometry.DefineSector(sector, 50, 0, 500);
        geometry.PlaceCompany(french, new TacticalPoint(0, 0, 100), 90, 20);
        geometry.PlaceCompany(german, new TacticalPoint(100, 0, 100), 270, 1);
        battle.PersonnelExposure.Set(french, new TacticalPersonnelExposure(10, 90, 0, 20));
        battle.PersonnelExposure.Set(german,
            new TacticalPersonnelExposure(inheritedManpower, 0, 0, 1));
        var signatures = new TacticalSignatureFrame(battle);
        signatures.Set(german, new TacticalUnitSignature(
            TacticalActivity.Stationary, TacticalConcealment.None));
        var settings = new TacticalHourlyCombatSettings(
            detection: new TacticalDetectionProfile(500, 0, 360, 1),
            fire: new TacticalDirectFireProfile(250),
            hit: new TacticalHitProfile(0.001, 0, 10),
            defense: new TacticalDefenseProfile(1, 1, 1, 0, 0, 0),
            effects: new TacticalPersonnelEffectProfile(1, 0),
            maxWeaponsPerCompany: 10,
            roundsPerWeapon: 3,
            baseSeed: 12345,
            suppression: suppressionProfile,
            cohesion: cohesionProfile);
        var orchestrator = new TacticalHourlyCombatOrchestrator(
            battle, geometry, new TacticalPerceptionState(battle),
            signatures, s.Engine.Diplomacy, settings);
        Assert.Null(orchestrator.LastResult);
        Assert.Null(orchestrator.LastCohesionPhase);
        s.Engine.Engagements.AttachTacticalBattle(battle);
        s.Engine.Engagements.RegisterTacticalOrchestrator(orchestrator);

        s.Engine.Tick();

        var secondPhase = Assert.IsType<TacticalCohesionPhaseResult>(
            orchestrator.LastCohesionPhase);
        var secondCompanyReport = Assert.Single(secondPhase.Companies,
            report => ReferenceEquals(report.Company, german));
        var secondCasualty = Assert.Single(battle.DirectFire.LastCasualtyPhase!.Companies,
            report => ReferenceEquals(report.Target, german));
        Assert.Equal(s.Engine.Clock.CurrentHour, secondPhase.Hour);
        Assert.Equal(secondEngagement.StartHour + 1, secondPhase.Hour);
        Assert.True(secondCasualty.Casualties.Total > 0);
        Assert.Equal(expectedCohesion, secondCompanyReport.CohesionBefore);
        Assert.Equal(secondCompanyReport.CohesionAfter, company.Cohesion);
        Assert.Equal(0, secondCompanyReport.LossFromSustainedFire);
        Assert.Equal(inheritedManpower, secondCasualty.AvailableBefore);
        Assert.Equal(inheritedManpower - secondCasualty.Casualties.Total,
            company.Composition.Manpower.Current);
        Assert.Equal(company.Composition.Manpower.Current,
            company.ParentBattalion!.GetResourceTotals().Personnel.Available);
        Assert.Equal(company.Composition.Manpower.Current,
            s.GermanDivision.GetCurrentResourceTotals().Personnel.Available);
        Assert.Equal(divisionOrganization, s.GermanDivision.Organization);
        Assert.False(s.GermanDivision.IsBroken);
        Assert.Equal(1, orchestrator.LastResult!.Value.SalvosFired);
        Assert.Equal(30, orchestrator.LastResult.Value.RoundsExpended);
        Assert.Equal(0, Assert.Single(battle.DirectFire.LastSuppressionPhase!.Targets,
            report => ReferenceEquals(report.Target, german)).PressureBefore);

        double afterCohesion = company.Cohesion;
        int afterManpower = company.Composition.Manpower.Current;
        Assert.Throws<InvalidOperationException>(() => orchestrator.Run(secondPhase.Hour));
        Assert.Throws<InvalidOperationException>(() =>
            battle.Cohesion.ApplyHour(
                secondPhase.Hour, battle.DirectFire.LastCasualtyPhase!,
                battle.DirectFire.LastSuppressionPhase!, suppressionProfile, cohesionProfile));
        Assert.Equal(afterCohesion, company.Cohesion);
        Assert.Equal(afterManpower, company.Composition.Manpower.Current);
        Assert.Same(secondPhase, orchestrator.LastCohesionPhase);
        Assert.Same(firstPhase, s.Orchestrator.LastCohesionPhase);
        Assert.Equal(firstReports, firstPhase.Companies);
        Assert.Equal(inheritedCohesion, firstCompanyReport.CohesionAfter);
        Assert.Single(s.Engine.Engagements.History.Reports);
    }


}
