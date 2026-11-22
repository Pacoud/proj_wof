
using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalReactionExecutionTests
{
    private sealed record Scenario(
        SimulationEngine Engine,
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalHourlyCombatOrchestrator Orchestrator,
        TacticalReactionExecutionState Movements,
        Battalion Battalion,
        CompanyDeployment Front,
        CompanyDeployment Reserve,
        CompanyDeployment Enemy,
        BattleSector FrontSector,
        BattleSector ReserveSector,
        BattleSector RearSector,
        TacticalPerceptionState Perception,
        TacticalSignatureFrame Signatures);


    private static TacticalBattalionCommandSectorReport Sector9n(
        int id,
        TacticalBattalionSectorStatus status,
        bool priority = false,
        int localized = 0,
        int suspected = 0,
        int stale = 0)
    {
        int available = status switch
        {
            TacticalBattalionSectorStatus.Uncovered => 0,
            TacticalBattalionSectorStatus.UnableToHold => 20,
            TacticalBattalionSectorStatus.AtRisk => 40,
            TacticalBattalionSectorStatus.Capable => 80,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status))
        };

        return new TacticalBattalionCommandSectorReport(
            SectorId: id,
            SectorName: $"Sector {id}",
            IsPrioritySector: priority,
            DefenseStatus: status,
            StationaryFrontLineCompanies:
                status == TacticalBattalionSectorStatus.Uncovered
                    ? 0 : 1,
            AuthorizedFrontLinePersonnel:
                status == TacticalBattalionSectorStatus.Uncovered
                    ? 0 : 100,
            AvailableFrontLinePersonnel: available,
            FrontLineReadiness: available,
            CurrentLocalizedReports: localized,
            CurrentSuspectedReports: suspected,
            StaleReports: stale);
    }

    private static (
        Scenario Scenario,
        TacticalCommandSituationReader Reader)
        Create9nScenario(
            params TacticalBattalionCommandSectorReport[] sectors)
    {
        Scenario? linked = null;

        var s = CreateScenario(
            commandSituationCaptureOverride:
                (battalion, _, _) =>
                    new TacticalBattalionCommandSituationSnapshot(
                        BattalionId: battalion.Id,
                        BattalionName: battalion.Name,
                        FriendlyDivisionName:
                            battalion.ParentDivision!.Name,
                        Hour:
                            linked!.Orchestrator.LastResult!.Value.Hour,
                        DeploymentRevision:
                            linked.Battle.DeploymentRevision,
                        Sectors:
                            Array.AsReadOnly(sectors.ToArray()),
                        OutsideLocalizedReports: 0,
                        OutsideSuspectedReports: 0,
                        OutsideStaleReports: 0));

        linked = s;

        var key = s.Orchestrator.RegisterCommandSituation(
            s.Battalion,
            new[]
            {
                s.FrontSector,
                s.ReserveSector
            });

        return (
            s,
            s.Orchestrator.BindCommandSituationReader(key));
    }


    private static Division MakeDivision(
        Country country,
        Province province,
        int companyCount,
        bool splitLastCompany = false)
    {
        var division = new Division(
            $"{country.Name} Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: companyCount * 100,
                infantryEquipment: companyCount * 100,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Infantry Battalion", BattalionType.Infantry);

        var secondBattalion = splitLastCompany
            ? new Battalion(2, "Second Battalion", BattalionType.Infantry)
            : null;
        for (int i = 1; i <= companyCount; i++)
        {
            var parent = i == companyCount && secondBattalion is not null
                ? secondBattalion
                : battalion;
            parent.AddCompany(new Company(
                i, $"Company {i}", CompanyType.Infantry,
                new CompanyComposition(
                    manpower: 100,
                    infantryEquipment: 100,
                    ammunition: new AmmunitionTotals(
                        smallArms: 1000))));
        }

        division.AddBattalion(battalion);
        if (secondBattalion is not null)
            division.AddBattalion(secondBattalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario(
        bool hasReserve = true,
        bool enemyInRear = false,
        bool connectReserve = true,
        double transitContactDistanceMeters = 0,
        bool useSimultaneousContacts = false,
        bool includeEnemyReserve = false,
        bool includeSecondEnemyBattalion = false,
        bool enableMobileCombat = false,
        double movementSpeed = 3000,
        double killedProbability = 0,
        int extraEnemyCompanies = 0,
        TacticalPhaseTimingRecorder? timingRecorder = null,
        bool enableAutomaticReinforcements = false,
        Func<
        Battalion,
        IReadOnlyList<BattleSector>,
        long,
        TacticalBattalionCommandSituationSnapshot>?
            commandSituationCaptureOverride = null,
        double riskThreshold = 30)
    {
        var engine = new SimulationEngine();

        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var friendly = MakeDivision(france, province, 2);
        int enemyCompanyCount = 1 + (includeEnemyReserve ? 1 : 0)
            + (includeSecondEnemyBattalion ? 1 : 0) + extraEnemyCompanies;
        var enemy = MakeDivision(germany, province, enemyCompanyCount,
            splitLastCompany: includeSecondEnemyBattalion);

        engine.AddDivision(friendly);
        engine.AddDivision(enemy);
        engine.DeclareWar(france, germany);
        engine.Tick();

        var engagement = Assert.Single(
            engine.Engagements.ActiveEngagements);

        var field = new TacticalBattlefield();

        var frontSector = new BattleSector(
            1, "Front", TerrainType.Plains);

        var reserveSector = new BattleSector(
            2, "Reserve", TerrainType.Plains);

        var rearSector = new BattleSector(
            3, "Rear", TerrainType.Plains);

        field.AddSector(frontSector);
        field.AddSector(reserveSector);
        field.AddSector(rearSector);

        if (connectReserve)
            field.Connect(reserveSector, frontSector, 4000);

        field.Connect(frontSector, rearSector, 2000);

        var battle = new TacticalBattle(engagement, field);

        var battalion = friendly.Battalions[0];

        var front = battle.DeployCompany(
            battalion.Companies[0],
            frontSector,
            CompanyDeploymentRole.FrontLine);

        var reserve = battle.DeployCompany(
            battalion.Companies[1],
            reserveSector,
            hasReserve
                ? CompanyDeploymentRole.Reserve
                : CompanyDeploymentRole.Support);

        var enemyDeployment = battle.DeployCompany(
            enemy.Battalions[0].Companies[0],
            enemyInRear ? rearSector : frontSector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(
            frontSector, 0, 0, 100);

        geometry.DefineSector(
            reserveSector, -4000, 0, 100);

        geometry.DefineSector(
            rearSector, 2000, 0, 100);

        geometry.PlaceCompany(
            front,
            new TacticalPoint(0, 0, 0),
            facingDegrees: 90,
            frontageMeters: 20);

        geometry.PlaceCompany(
            reserve,
            new TacticalPoint(-4000, 0, 0),
            facingDegrees: 90,
            frontageMeters: 20);

        geometry.PlaceCompany(
            enemyDeployment,
            enemyInRear
                ? new TacticalPoint(2000, 0, 0)
                : new TacticalPoint(20, 0, 0),
            facingDegrees: 270,
            frontageMeters: 20);

        // Les pertes initiales ne proviennent pas
        // des nouveaux mouvements.
        front.Company.Composition!.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 80,
                WoundedInAction: 0,
                MissingOrCaptured: 0));

        battle.PersonnelExposure.Set(
            front,
            new TacticalPersonnelExposure(
                exposed: 20, sheltered: 0,
                reserve: 0, exposedDepthMeters: 20));

        battle.PersonnelExposure.Set(
            reserve,
            new TacticalPersonnelExposure(
                exposed: 0, sheltered: 0,
                reserve: 100, exposedDepthMeters: 20));

        battle.PersonnelExposure.Set(
            enemyDeployment,
            new TacticalPersonnelExposure(
                exposed: 100, sheltered: 0,
                reserve: 0, exposedDepthMeters: 20));

        battle.BattalionAssessments.SetPrioritySector(
            battalion, frontSector);

        var settings = new TacticalHourlyCombatSettings(
            detection: new TacticalDetectionProfile(
                maxVisualRangeMeters: 500,
                maxAcousticRangeMeters: 0,
                fieldOfViewDegrees: 360,
                localizationCellMeters: 25),
            fire: new TacticalDirectFireProfile(250,
                fireArcDegrees: enableMobileCombat ? 360 : 120),
            hit: killedProbability > 0
                ? new TacticalHitProfile(0.001, 0, 10)
                : new TacticalHitProfile(2, 1, 0.5),
            defense: new TacticalDefenseProfile(
                1, 1, 1, 0, 0, 0),
            effects: new TacticalPersonnelEffectProfile(killedProbability, 0),
            maxWeaponsPerCompany: 10,
            roundsPerWeapon: 3,
            baseSeed: 12345,
            suppression: new TacticalSuppressionProfile(
                10, 2, 10, 0.8),
            cohesion: new TacticalCohesionProfile(
                1.5, 40, 0.1, 3, 20, 0.5),
            battalionAssessment:
                new TacticalBattalionAssessmentProfile(
                    3, 2, 1, 2, 60, riskThreshold),
            battalionReactions:
                new TacticalBattalionReactionProfile(
                    2, 2, 10),
            reactionMovement:
                new TacticalReactionMovementProfile(movementSpeed,
                frontLineExposureFraction: 0.25,
                transitContactDistanceMeters: transitContactDistanceMeters,
                useSimultaneousContacts: useSimultaneousContacts),
            enableMobileCombat: enableMobileCombat,
            enableAutomaticReinforcements:
                enableAutomaticReinforcements);

        var perception = new TacticalPerceptionState(battle);
        var signatures = new TacticalSignatureFrame(battle);
        var orchestrator = new TacticalHourlyCombatOrchestrator(
            battle,
            geometry,
            perception,
            signatures,
            engine.Diplomacy,
            settings,
            timingRecorder,
            commandSituationCaptureOverride);

        engine.Engagements.AttachTacticalBattle(battle);

        engine.Engagements.RegisterTacticalOrchestrator(
            orchestrator);

        return new Scenario(
            engine,
            battle,
            geometry,
            orchestrator,
            orchestrator.ReactionExecution!,
            battalion,
            front,
            reserve,
            enemyDeployment,
            frontSector,
            reserveSector,
            rearSector,
            perception,
            signatures);
    }

    private static TacticalBattalionReactionRequest
        ReachConfirmedRupture(Scenario s)
    {
        s.Engine.Tick(); // Heure 2 : situation menacee.
        s.Engine.Tick(); // Heure 3 : rupture confirmee.

        Assert.Equal(3, s.Engine.Clock.CurrentHour);

        return Assert.Single(
            s.Orchestrator.LastBattalionReactionPhase!.Requests,
            r => ReferenceEquals(r.Battalion, s.Battalion));
    }


    private static TacticalBattalionCommandSituationSnapshot
        Capture9l(Scenario s, params BattleSector[] sectors)
    {
        return new TacticalBattalionCommandSituationSystem(
            s.Orchestrator).Capture(
                s.Battalion,
                sectors,
                maxAgeHours: 2);
    }


    [Fact]
    public void ReinforcementTakesTimeAndUpdatesDeployment()
    {
        var s = CreateScenario();

        var request = ReachConfirmedRupture(s);

        Assert.Equal(
            TacticalBattalionReactionType.Reinforce,
            request.Type);

        var resourcesBefore = s.Reserve.Company.Composition!
            .Manpower.Current;

        var decision = s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Reserve);

        Assert.NotNull(decision.Movement);
        Assert.Equal(5, decision.Movement.ArrivalHour);

        // Acceptation en heure 3 : rien ne bouge.
        Assert.Same(s.ReserveSector, s.Reserve.Sector);
        Assert.Equal(
            CompanyDeploymentRole.Reserve,
            s.Reserve.Role);

        s.Engine.Tick(); // Heure 4 : encore en transit.

        Assert.True(s.Movements.IsMoving(s.Reserve));
        Assert.Same(s.ReserveSector, s.Reserve.Sector);

        s.Engine.Tick(); // Heure 5 : arrivee.

        Assert.False(s.Movements.IsMoving(s.Reserve));
        Assert.Same(s.FrontSector, s.Reserve.Sector);

        Assert.Equal(
            CompanyDeploymentRole.FrontLine,
            s.Reserve.Role);

        var position = s.Geometry.GetPosition(s.Reserve)!;

        Assert.Equal(0, position.Point.XMeters);
        Assert.Equal(0, position.Point.YMeters);

        Assert.Equal(
            resourcesBefore,
            s.Reserve.Company.Composition!.Manpower.Current);

        var completion = Assert.Single(
            s.Orchestrator.LastMovementPhase!.Completed);

        Assert.Equal(
            TacticalMovementOutcome.Arrived,
            completion.Outcome);



        var exposure = s.Battle.PersonnelExposure.Get(s.Reserve);

        Assert.Equal(100, exposure.Total);
        Assert.Equal(25, exposure.Exposed);
        Assert.Equal(75, exposure.Reserve);

    }

    [Fact]
    public void WithdrawalMovesToSafeAdjacentSector()
    {
        var s = CreateScenario(hasReserve: false);

        var request = ReachConfirmedRupture(s);

        Assert.Equal(
            TacticalBattalionReactionType.RequestWithdrawal,
            request.Type);

        var decision = s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Front,
            destination: s.RearSector);

        Assert.Equal(4, decision.Movement!.ArrivalHour);

        s.Engine.Tick();

        Assert.Same(s.RearSector, s.Front.Sector);

        Assert.Equal(
            CompanyDeploymentRole.Reserve,
            s.Front.Role);

        var exposure = s.Battle.PersonnelExposure.Get(s.Front);
        Assert.Equal(20, exposure.Total);
        Assert.Equal(0, exposure.Exposed);
        Assert.Equal(20, exposure.Reserve);

        Assert.Equal(
            20,
            s.Front.Company.Composition!.Manpower.Current);
    }

    [Fact]
    public void EnemyOccupiedWithdrawalSectorIsRejected()
    {
        var s = CreateScenario(
            hasReserve: false,
            enemyInRear: true);

        var request = ReachConfirmedRupture(s);

        Assert.Throws<InvalidOperationException>(() =>
            s.Movements.Decide(
                request,
                TacticalReactionDecision.Approve,
                company: s.Front,
                destination: s.RearSector));

        Assert.Same(s.FrontSector, s.Front.Sector);
        Assert.Empty(s.Movements.PendingMovements);
    }

    [Fact]
    public void UnconnectedReinforcementCannotBeApproved()
    {
        var s = CreateScenario(connectReserve: false);

        var request = ReachConfirmedRupture(s);

        Assert.Throws<InvalidOperationException>(() =>
            s.Movements.Decide(
                request,
                TacticalReactionDecision.Approve,
                company: s.Reserve));

        // Un ordre invalide ne doit pas etre marque
        // comme accepte et peut encore etre refuse.
        var rejected = s.Movements.Decide(
            request,
            TacticalReactionDecision.Reject);

        Assert.Null(rejected.Movement);
        Assert.Empty(s.Movements.PendingMovements);
    }

    [Fact]
    public void RejectedOrderDoesNotMoveTheCompany()
    {
        var s = CreateScenario();

        var request = ReachConfirmedRupture(s);

        s.Movements.Decide(
            request,
            TacticalReactionDecision.Reject);

        s.Engine.Tick();
        s.Engine.Tick();

        Assert.Same(s.ReserveSector, s.Reserve.Sector);
        Assert.Empty(s.Movements.PendingMovements);
    }

    [Fact]
    public void ARequestCannotBeDecidedTwice()
    {
        var s = CreateScenario();

        var request = ReachConfirmedRupture(s);

        s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Reserve);

        Assert.Throws<InvalidOperationException>(() =>
            s.Movements.Decide(
                request,
                TacticalReactionDecision.Approve,
                company: s.Reserve));
    }

    [Fact]
    public void ForgedRequestIsRejected()
    {
        var s = CreateScenario();

        var request = ReachConfirmedRupture(s);

        // Les donnees sont identiques mais l'objet
        // n'est pas la demande emise par le moteur.
        var forged = request with { Hour = request.Hour };

        Assert.Throws<InvalidOperationException>(() =>
            s.Movements.Decide(
                forged,
                TacticalReactionDecision.Approve,
                company: s.Reserve));
    }

    [Fact]
    public void SpatialIndexBecomesStaleAfterArrival()
    {
        var s = CreateScenario();

        var request = ReachConfirmedRupture(s);

        var oldIndex = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        Assert.Contains(
            s.Reserve,
            oldIndex.GetAssigned(s.ReserveSector));

        s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Reserve);

        s.Engine.Tick();
        s.Engine.Tick();

        Assert.Throws<InvalidOperationException>(() =>
            oldIndex.GetAssigned(s.ReserveSector));

        var fresh = new TacticalSectorPresenceIndex(
            s.Battle, s.Geometry);

        Assert.Contains(
            s.Reserve,
            fresh.GetAssigned(s.FrontSector));
    }

    [Fact]
    public void AcceptedReorganizationDoesNotInventMovements()
    {
        var s = CreateScenario();

        // Une menace des la premiere heure produit
        // une demande Reorganize.
        s.Engine.Tick();

        var request = Assert.Single(
            s.Orchestrator.LastBattalionReactionPhase!.Requests,
            r => ReferenceEquals(r.Battalion, s.Battalion));

        Assert.Equal(
            TacticalBattalionReactionType.Reorganize,
            request.Type);

        var result = s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve);

        Assert.Null(result.Movement);
        Assert.Empty(s.Movements.PendingMovements);
    }



    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithdrawalIsBlockedIfEnemyOccupiesDestinationBeforeArrival(
        bool useSimultaneousContacts)
    {
        var s = CreateScenario(
            hasReserve: false,
            transitContactDistanceMeters: useSimultaneousContacts ? 100 : 0,
            useSimultaneousContacts: useSimultaneousContacts);

        var request = ReachConfirmedRupture(s);

        s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Front,
            destination: s.RearSector);

        // Une unite ennemie rejoint entre-temps le
        // secteur dans lequel le bataillon voulait
        // se replier. On simule directement ce changement
        // pour tester la revalidation a l'arrivee.
        s.Geometry.RelocateCompany(
            s.Enemy, s.RearSector,
            s.Geometry.GetSectorCenterPoint(s.RearSector));

        s.Enemy.ApplyMovementArrival(
            s.RearSector,
            CompanyDeploymentRole.FrontLine);

        // Heure 4 : la destination n'est plus sure.
        s.Engine.Tick();

        var completion = Assert.Single(
            s.Orchestrator.LastMovementPhase!.Completed);

        Assert.Equal(
            TacticalMovementOutcome.BlockedByEnemy,
            completion.Outcome);

        Assert.Same(s.FrontSector, s.Front.Sector);

        Assert.Equal(
            CompanyDeploymentRole.FrontLine,
            s.Front.Role);

        Assert.Empty(s.Movements.PendingMovements);
    }



    [Fact]
    public void CancelledWithdrawal_StopsAtTheEdgeOfTheLastReachedSector()
    {
        // 1 000 m/h : après une heure, la compagnie est au milieu
        // de la liaison de 2 000 m entre le front et l'arrière.
        var s = CreateScenario(hasReserve: false, movementSpeed: 1000);
        var request = ReachConfirmedRupture(s);

        s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Front,
            destination: s.RearSector);

        s.Engine.Tick(); // Heure 4 : à 1 000 m du front.

        Assert.True(s.Front.IsInTransit);
        Assert.Equal(1000, s.Geometry.GetPosition(s.Front)!.Point.XMeters, 6);

        // L'arrière est occupé avant l'arrivée : le repli est annulé.
        Relocate(s, s.Enemy, s.RearSector);

        s.Engine.Tick(); // Heure 5.

        var completion = Assert.Single(s.Orchestrator.LastMovementPhase!.Completed);
        Assert.Equal(TacticalMovementOutcome.BlockedByEnemy, completion.Outcome);

        // La compagnie recule seulement jusqu'au bord du secteur du front
        // (rayon 100 m), au lieu de revenir à son centre.
        var point = s.Geometry.GetPosition(s.Front)!.Point;

        Assert.Same(s.FrontSector, s.Front.Sector);
        Assert.False(s.Front.IsInTransit);
        Assert.Equal(100, point.XMeters, 6);
        Assert.Equal(0, point.YMeters);
        Assert.True(s.Geometry.GetSectorGeometry(s.FrontSector)!.Contains(point));
    }

    [Fact]
    public void Movement_ArrivesAtTheDestinationPointOfItsOrder()
    {
        var s = CreateScenario();
        var request = ReachConfirmedRupture(s);

        // Hors de la géométrie du front (rayon 100 m) : refusé.
        Assert.Throws<InvalidOperationException>(() => s.Movements.Decide(
            request, TacticalReactionDecision.Approve,
            company: s.Reserve, destinationPoint: new TacticalPoint(150, 0, 0)));

        // Pas à l'altitude du secteur (0 m) : refusé.
        Assert.Throws<InvalidOperationException>(() => s.Movements.Decide(
            request, TacticalReactionDecision.Approve,
            company: s.Reserve, destinationPoint: new TacticalPoint(30, 40, 5)));

        // Un refus n'accepte aucun point.
        Assert.Throws<ArgumentException>(() => s.Movements.Decide(
            request, TacticalReactionDecision.Reject,
            destinationPoint: new TacticalPoint(30, 40, 0)));

        Assert.False(s.Reserve.IsInTransit);

        var decision = s.Movements.Decide(
            request, TacticalReactionDecision.Approve,
            company: s.Reserve, destinationPoint: new TacticalPoint(30, 40, 0));

        Assert.Equal(new TacticalPoint(30, 40, 0), decision.Movement!.DestinationPoint);

        s.Engine.Tick(); // Heure 4 : en route.
        s.Engine.Tick(); // Heure 5 : arrivée.

        Assert.Same(s.FrontSector, s.Reserve.Sector);
        Assert.False(s.Reserve.IsInTransit);
        Assert.Equal(new TacticalPoint(30, 40, 0), s.Geometry.GetPosition(s.Reserve)!.Point);
    }

    [Fact]
    public void DirectReinforcementProgressesAlongItsConnection()
    {
        var s = CreateScenario();
        var request = ReachConfirmedRupture(s);

        var decision = s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Reserve);

        Assert.Equal(2, decision.Movement!.Route.Count);
        Assert.Equal(4000, decision.Movement.TotalDistanceMeters);

        s.Engine.Tick(); // Heure 4 : 3000 / 4000 metres.

        Assert.True(s.Reserve.IsInTransit);
        Assert.Same(s.ReserveSector, s.Reserve.Sector);

        var movement = Assert.Single(
            s.Orchestrator.LastMovementPhase!.Active,
            m => ReferenceEquals(m.Order.Company, s.Reserve));

        Assert.Equal(3000, movement.DistanceTravelledMeters, 6);
        Assert.Equal(0.75, movement.ProgressFraction, 6);

        var position = s.Geometry.GetPosition(s.Reserve)!;

        // Reserve = -4000 m, front = 0 m.
        // 75 % du trajet => -1000 m.
        Assert.Equal(-1000, position.Point.XMeters, 6);
        Assert.Empty(
            s.Orchestrator.LastMovementPhase.Completed);

        s.Engine.Tick(); // Heure 5 : arrivee.

        Assert.False(s.Reserve.IsInTransit);
        Assert.Same(s.FrontSector, s.Reserve.Sector);
        Assert.Equal(
            CompanyDeploymentRole.FrontLine,
            s.Reserve.Role);

        Assert.Single(
            s.Orchestrator.LastMovementPhase!.Completed,
            c => ReferenceEquals(c.Order.Company, s.Reserve)
                && c.Outcome == TacticalMovementOutcome.Arrived);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiSectorWithdrawalTraversesIntermediateWaypoints(
        bool useSimultaneousContacts)
    {
        var s = CreateScenario(
            hasReserve: false,
            transitContactDistanceMeters: useSimultaneousContacts ? 100 : 0,
            useSimultaneousContacts: useSimultaneousContacts);

        // Ajouter un quatrieme secteur derriere Rear.
        var farRear = new BattleSector(
            4, "Far Rear", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(farRear);
        s.Battle.Battlefield.Connect(
            s.RearSector, farRear, 5000);

        s.Geometry.DefineSector(
            farRear, 7000, 0, 100);

        var request = ReachConfirmedRupture(s);

        var decision = s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Front,
            destination: farRear);

        var order = decision.Movement!;

        Assert.Equal(
            new[]
            {
                s.FrontSector,
                s.RearSector,
                farRear
            },
            order.Route);

        Assert.Equal(7000, order.TotalDistanceMeters, 6);
        Assert.Equal(6, order.ArrivalHour);

        if (useSimultaneousContacts)
        {
            // Tester la progression sur un trajet sans contact.
            s.Geometry.RelocateCompany(s.Enemy, s.ReserveSector,
                s.Geometry.GetSectorCenterPoint(s.ReserveSector));
            s.Enemy.ApplyMovementArrival(s.ReserveSector, CompanyDeploymentRole.FrontLine);
        }

        // Heure 4 : 2000 m jusqu'a Rear
        // puis 1000 m sur Rear -> Far Rear.
        s.Engine.Tick();

        Assert.True(s.Front.IsInTransit);
        Assert.Same(s.RearSector, s.Front.Sector);
        Assert.Equal(
            3000,
            s.Geometry.GetPosition(s.Front)!.Point.XMeters,
            6);

        Assert.Equal(
            CompanyDeploymentRole.FrontLine,
            s.Front.Role);

        var first = Assert.Single(
            s.Orchestrator.LastMovementPhase!.Active,
            m => ReferenceEquals(m.Order.Company, s.Front));

        Assert.Equal(3000, first.DistanceTravelledMeters, 6);
        Assert.Equal(3000.0 / 7000, first.ProgressFraction, 6);

        // Heure 5 : encore 3000 m.
        s.Engine.Tick();

        Assert.Same(s.RearSector, s.Front.Sector);
        Assert.Equal(
            6000,
            s.Geometry.GetPosition(s.Front)!.Point.XMeters,
            6);

        // Heure 6 : derniers 1000 m.
        s.Engine.Tick();

        Assert.False(s.Front.IsInTransit);
        Assert.Same(farRear, s.Front.Sector);

        Assert.Equal(
            CompanyDeploymentRole.Reserve,
            s.Front.Role);

        Assert.Equal(
            7000,
            s.Geometry.GetPosition(s.Front)!.Point.XMeters,
            6);

        Assert.Single(
            s.Orchestrator.LastMovementPhase!.Completed,
            c => c.Outcome == TacticalMovementOutcome.Arrived
                && ReferenceEquals(c.Order.Company, s.Front));

        // Les pertes initiales restent les seules.
        Assert.Equal(
            20,
            s.Front.Company.Composition!.Manpower.Current);
    }

    [Fact]
    public void TransitContactPausesWithoutCausingCasualties()
    {
        var s = CreateScenario(
            hasReserve: false,
            transitContactDistanceMeters: 100);

        var request = ReachConfirmedRupture(s);

        s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Front,
            destination: s.RearSector);

        int before =
            s.Front.Company.Composition!.Manpower.Current;

        // L'ennemi se trouve a 20 m du point de depart.
        s.Engine.Tick();

        var phase = s.Orchestrator.LastMovementPhase!;

        var contact = Assert.Single(
            phase.Contacts,
            c => ReferenceEquals(c.MovingCompany, s.Front));

        Assert.Same(s.Enemy, contact.EnemyCompany);

        var progress = Assert.Single(
            phase.Active,
            m => ReferenceEquals(m.Order.Company, s.Front));

        Assert.True(progress.PausedByContact);
        Assert.Equal(0, progress.DistanceTravelledMeters);

        Assert.True(s.Movements.IsMoving(s.Front));

        Assert.Equal(
            before,
            s.Front.Company.Composition!.Manpower.Current);

        // Le contact n'a pas invente de perte humaine.
        Assert.Empty(phase.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovementResumesAfterTransitContactDisappears(bool useSimultaneousContacts)
    {
        var s = CreateScenario(
            hasReserve: false,
            transitContactDistanceMeters: 100,
            useSimultaneousContacts: useSimultaneousContacts);

        var request = ReachConfirmedRupture(s);

        s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Front,
            destination: s.RearSector);

        s.Engine.Tick(); // Contact, mouvement suspendu.

        Assert.True(s.Movements.IsMoving(s.Front));

        // Eloigner l'ennemi du corridor Front -> Rear.
        s.Geometry.RelocateCompany(
            s.Enemy, s.ReserveSector,
            s.Geometry.GetSectorCenterPoint(s.ReserveSector));

        s.Enemy.ApplyMovementArrival(
            s.ReserveSector,
            CompanyDeploymentRole.FrontLine);

        s.Engine.Tick(); // Le corridor est de nouveau libre.

        Assert.False(s.Movements.IsMoving(s.Front));
        Assert.Same(s.RearSector, s.Front.Sector);

        Assert.Single(
            s.Orchestrator.LastMovementPhase!.Completed,
            c => ReferenceEquals(c.Order.Company, s.Front)
                && c.Outcome == TacticalMovementOutcome.Arrived);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimultaneousContactsIncludeTimeSpentAtDestination(
        bool reverseApprovalOrder)
    {
        var s = CreateScenario(
            hasReserve: false,
            enemyInRear: true,
            transitContactDistanceMeters: 100,
            useSimultaneousContacts: true);

        var destination = new BattleSector(4, "Meeting point", TerrainType.Plains);
        s.Battle.Battlefield.AddSector(destination);
        s.Battle.Battlefield.Connect(s.FrontSector, destination, 1000);
        s.Battle.Battlefield.Connect(s.RearSector, destination, 2000);
        s.Geometry.DefineSector(destination, 1000, 2000, 100);

        WeakenFront(s, s.Enemy);
        var enemyBattalion = s.Enemy.Company.ParentBattalion!;

        var firstRequest = ReachConfirmedRupture(s);
        var secondRequest = Assert.Single(
            s.Orchestrator.LastBattalionReactionPhase!.Requests,
            request => ReferenceEquals(request.Battalion, enemyBattalion));
        var orders = new[] { (firstRequest, s.Front), (secondRequest, s.Enemy) };
        if (reverseApprovalOrder)
            Array.Reverse(orders);

        foreach (var (request, company) in orders)
        {
            s.Movements.Decide(request, TacticalReactionDecision.Approve,
                company: company, destination: destination);
        }

        var frontStart = s.Geometry.GetPosition(s.Front)!.Point;
        var enemyStart = s.Geometry.GetPosition(s.Enemy)!.Point;
        s.Engine.Tick();

        var phase = s.Orchestrator.LastMovementPhase!;
        var encounter = Assert.Single(phase.PredictedEncounters!);
        Assert.Same(s.Front, encounter.First);
        Assert.Same(s.Enemy, encounter.Second);
        Assert.Equal(2.0 / 3, encounter.TimeFraction, 6);
        Assert.Equal(2, phase.SimultaneousResolution!.ResolutionPasses);
        Assert.True(phase.SimultaneousResolution.ExactPairChecks > 0);
        Assert.Empty(phase.Contacts);
        Assert.Empty(phase.Completed);
        Assert.Equal(2, phase.Active.Count);
        Assert.All(phase.Active, move =>
        {
            Assert.True(move.PausedByContact);
            Assert.Equal(0, move.DistanceTravelledMeters);
        });
        Assert.Same(s.FrontSector, s.Front.Sector);
        Assert.Same(s.RearSector, s.Enemy.Sector);
        Assert.Equal(frontStart, s.Geometry.GetPosition(s.Front)!.Point);
        Assert.Equal(enemyStart, s.Geometry.GetPosition(s.Enemy)!.Point);
    }

    private static void WeakenFront(Scenario s, CompanyDeployment company)
    {
        var battalion = company.Company.ParentBattalion!;
        s.Battle.BattalionAssessments.SetPrioritySector(battalion, company.Sector);
        company.Company.Composition!.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(80, 0, 0));
        s.Battle.PersonnelExposure.Set(company, new TacticalPersonnelExposure(
            exposed: 20, sheltered: 0, reserve: 0, exposedDepthMeters: 20));
    }

    private static BattleSector AddSector(
        Scenario s, int id, double x, double y)
    {
        var sector = new BattleSector(id, $"Sector {id}", TerrainType.Plains);
        s.Battle.Battlefield.AddSector(sector);
        s.Geometry.DefineSector(sector, x, y, 100);
        return sector;
    }

    private static void Relocate(Scenario s, CompanyDeployment company, BattleSector sector)
    {
        s.Geometry.RelocateCompany(company, sector,
            s.Geometry.GetSectorCenterPoint(sector));
        company.ApplyMovementArrival(sector, company.Role);
    }

    private static void ApproveMovement(
        Scenario s, CompanyDeployment company, BattleSector? destination = null)
    {
        var request = Assert.Single(
            s.Orchestrator.LastBattalionReactionPhase!.Requests,
            r => ReferenceEquals(r.Battalion, company.Company.ParentBattalion));
        s.Movements.Decide(request, TacticalReactionDecision.Approve,
            company: company, destination: destination);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OppositeOrdersOnSameConnectionPauseBothCompanies(bool reverseApprovalOrder)
    {
        var s = CreateScenario(
            enemyInRear: true, transitContactDistanceMeters: 100,
            useSimultaneousContacts: true, includeEnemyReserve: true);

        // Deux renforts rejoignent leurs fronts respectifs par la même liaison.
        Relocate(s, s.Reserve, s.RearSector);
        var enemyReserve = s.Battle.DeployCompany(
            s.Enemy.Company.ParentBattalion!.Companies[1],
            s.FrontSector, CompanyDeploymentRole.Reserve);
        s.Geometry.PlaceCompany(enemyReserve, new TacticalPoint(0, 0, 0), 270, 20);
        WeakenFront(s, s.Enemy);
        ReachConfirmedRupture(s);

        var first = reverseApprovalOrder ? enemyReserve : s.Reserve;
        var second = reverseApprovalOrder ? s.Reserve : enemyReserve;
        ApproveMovement(s, first);
        ApproveMovement(s, second);
        var firstPosition = s.Geometry.GetPosition(first);
        var secondPosition = s.Geometry.GetPosition(second);
        int firstPersonnel = first.Company.Composition!.Manpower.Current;
        int secondPersonnel = second.Company.Composition!.Manpower.Current;

        s.Engine.Tick();

        var phase = s.Orchestrator.LastMovementPhase!;
        var encounter = Assert.Single(phase.PredictedEncounters!);
        Assert.Same(s.Reserve, encounter.First);
        Assert.Same(enemyReserve, encounter.Second);
        Assert.Equal(1.0 / 3, encounter.TimeFraction, 6);
        Assert.Equal(2, phase.SimultaneousResolution!.PausedCompanies.Count);
        Assert.All(phase.Active, progress => Assert.True(progress.PausedByContact));
        Assert.Empty(phase.Completed);
        Assert.Same(firstPosition, s.Geometry.GetPosition(first));
        Assert.Same(secondPosition, s.Geometry.GetPosition(second));
        Assert.Equal(firstPersonnel, first.Company.Composition!.Manpower.Current);
        Assert.Equal(secondPersonnel, second.Company.Composition!.Manpower.Current);
    }

    private static CompanyDeployment DeployOpposingReserve(Scenario s)
    {
        Relocate(s, s.Reserve, s.RearSector);
        var opponent = s.Battle.DeployCompany(
            s.Enemy.Company.ParentBattalion!.Companies[1],
            s.FrontSector, CompanyDeploymentRole.Reserve);
        s.Geometry.PlaceCompany(opponent, new TacticalPoint(0, 0, 0), 90, 20);
        WeakenFront(s, s.Enemy);
        return opponent;
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(10, true)]
    public void MobileContactPreparesExposureAndSignature(
        int reserveBefore, bool withConcealment)
    {
        var s = CreateScenario(
            enemyInRear: true,
            transitContactDistanceMeters: 200,
            useSimultaneousContacts: true,
            includeEnemyReserve: true,
            enableMobileCombat: true);

        var enemyReserve = DeployOpposingReserve(s);
        s.Battle.PersonnelExposure.Set(s.Reserve, new TacticalPersonnelExposure(
            exposed: 0, sheltered: 100 - reserveBefore, reserve: reserveBefore,
            exposedDepthMeters: 30));
        if (withConcealment)
        {
            s.Signatures.Set(s.Reserve, new TacticalUnitSignature(
                TacticalActivity.Stationary, TacticalConcealment.Strong));
        }

        ReachConfirmedRupture(s);
        ApproveMovement(s, s.Reserve);
        ApproveMovement(s, enemyReserve);
        s.Engine.Tick();

        long hour = s.Engine.Clock.CurrentHour;
        var contact = Assert.Single(s.Battle.MobileEngagements.GetContacts(hour));
        Assert.Same(s.Reserve, contact.First);
        Assert.Same(enemyReserve, contact.Second);
        Assert.Equal(0.3, contact.ContactTimeFraction, 6);
        var exposure = s.Battle.PersonnelExposure.Get(s.Reserve);
        Assert.Equal(25, exposure.Exposed);
        Assert.Equal(Math.Max(reserveBefore - 25, 0), exposure.Reserve);
        Assert.Equal(75 - exposure.Reserve, exposure.Sheltered);
        Assert.Equal(30, exposure.ExposedDepthMeters);
        Assert.Equal(100, exposure.Total);
        Assert.Equal(100, s.Reserve.Company.Composition!.Manpower.Current);
        Assert.Equal(CompanyDeploymentRole.Reserve, s.Reserve.Role);

        Assert.True(s.Perception.HasCurrentLocalization(s.Reserve, enemyReserve, hour));
        // A 200 m, la signature Moving reste sous le seuil avec dissimulation forte.
        Assert.Equal(!withConcealment,
            s.Perception.HasCurrentLocalization(enemyReserve, s.Reserve, hour));
        if (withConcealment)
        {
            Assert.True(s.Signatures.TryGet(s.Reserve, out var original));
            Assert.Equal(TacticalActivity.Stationary, original!.Activity);
            Assert.Equal(TacticalConcealment.Strong, original.Concealment);
        }
        else
        {
            Assert.False(s.Signatures.TryGet(s.Reserve, out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OppositeMobileOrdersStopAtFirstContactAndFireOncePerCompany(
        bool reverseApprovalOrder)
    {
        var s = CreateScenario(
            enemyInRear: true, transitContactDistanceMeters: 200,
            useSimultaneousContacts: true, includeEnemyReserve: true,
            enableMobileCombat: true);
        var opponent = DeployOpposingReserve(s);
        ReachConfirmedRupture(s);
        ApproveMovement(s, reverseApprovalOrder ? opponent : s.Reserve);
        ApproveMovement(s, reverseApprovalOrder ? s.Reserve : opponent);

        s.Engine.Tick();

        var result = s.Orchestrator.LastMovementPhase!;
        var contact = Assert.Single(result.ConfirmedMobileContacts!);
        Assert.Equal(0.3, contact.ContactTimeFraction, 6);
        Assert.Equal(1.0 / 3, Assert.Single(result.PredictedEncounters!).TimeFraction, 6);
        Assert.Equal(1100, contact.FirstPosition.XMeters, 6);
        Assert.Equal(900, contact.SecondPosition.XMeters, 6);
        Assert.Empty(result.Completed);
        Assert.Equal(2, result.Active.Count);
        var fire = s.Battle.DirectFire.LastPhase!;
        Assert.Equal(4, fire.Hour);
        Assert.Equal(2, fire.Events.Count);
        foreach (var company in new[] { s.Reserve, opponent })
        {
            var target = ReferenceEquals(company, s.Reserve) ? opponent : s.Reserve;
            var salvo = Assert.Single(fire.Events, e => ReferenceEquals(e.Shooter, company));
            Assert.Equal(7, salvo.WeaponsCommitted);
            Assert.Equal(21, salvo.RoundsExpended);
            Assert.Equal(979, company.Company.Composition!.Ammunition.GetAvailable(
                AmmunitionType.SmallArms));
            Assert.Equal(CompanyDeploymentRole.Reserve, company.Role);
            Assert.True(company.IsInTransit);
            Assert.Equal(900, Assert.Single(result.Active,
                m => ReferenceEquals(m.Order.Company, company)).DistanceTravelledMeters, 6);

            var report = Assert.Single(s.Perception.GetReports(company));
            Assert.Equal(s.Geometry.GetPosition(target)!.Point, report.LastKnownPosition);
        }

        Assert.Equal(contact.FirstPosition, s.Geometry.GetPosition(s.Reserve)!.Point);
        Assert.Equal(contact.SecondPosition, s.Geometry.GetPosition(opponent)!.Point);
        Assert.Equal(new TacticalMobileCombatSummary(4, 1, 2, 2),
            s.Orchestrator.LastMobileCombatSummary!.Value);
    }

    [Theory]
    [InlineData(true, 3000, 0.3)]
    [InlineData(false, 1000, 0.9)]
    public void VisibilityAndRemainingTimeLimitMobileFire(bool obstacle, double speed, double time)
    {
        var s = CreateScenario(
            enemyInRear: true, transitContactDistanceMeters: 200,
            useSimultaneousContacts: true, includeEnemyReserve: true,
            enableMobileCombat: true, movementSpeed: speed);
        var opponent = DeployOpposingReserve(s);
        ReachConfirmedRupture(s);
        ApproveMovement(s, s.Reserve);
        ApproveMovement(s, opponent);
        if (obstacle)
        {
            s.Geometry.AddLineObstacle(new TacticalLineObstacle(
                1, "Crête", TacticalLineObstacleKind.Ridge,
                1000, -30, 1000, 30, topElevationMeters: 120));
        }

        s.Engine.Tick();

        var contact = Assert.Single(s.Battle.MobileEngagements.GetContacts(4));
        Assert.Equal(time, contact.ContactTimeFraction, 6);
        Assert.Equal(!obstacle, s.Perception.HasCurrentLocalization(s.Reserve, opponent, 4));
        Assert.Equal(!obstacle, s.Perception.HasCurrentLocalization(opponent, s.Reserve, 4));
        Assert.Equal(new TacticalMobileCombatSummary(
            4, 1, obstacle ? 0 : 2, obstacle ? 0 : 2),
            s.Orchestrator.LastMobileCombatSummary!.Value);
        Assert.All(s.Battle.DirectFire.LastPhase!.Events, salvo =>
        {
            Assert.Equal(1, salvo.WeaponsCommitted);
            Assert.Equal(3, salvo.RoundsExpended);
        });
    }

    [Fact]
    public void CompanyWithoutAmmunitionCannotFireDuringConfirmedMobileContact()
    {
        var s = CreateScenario(
            enemyInRear: true, transitContactDistanceMeters: 200,
            useSimultaneousContacts: true, includeEnemyReserve: true,
            enableMobileCombat: true);
        var opponent = DeployOpposingReserve(s);
        var ammunition = s.Reserve.Company.Composition!.Ammunition;
        Assert.True(ammunition.TryConsume(AmmunitionType.SmallArms,
            ammunition.GetAvailable(AmmunitionType.SmallArms)));
        ReachConfirmedRupture(s);
        ApproveMovement(s, s.Reserve);
        ApproveMovement(s, opponent);

        s.Engine.Tick();

        Assert.True(s.Perception.HasCurrentLocalization(s.Reserve, opponent, 4));
        Assert.Equal(new TacticalMobileCombatSummary(4, 1, 1, 1),
            s.Orchestrator.LastMobileCombatSummary!.Value);
        Assert.Same(opponent, Assert.Single(s.Battle.DirectFire.LastPhase!.Events).Shooter);
        Assert.Equal(0, ammunition.GetAvailable(AmmunitionType.SmallArms));
    }

    [Fact]
    public void UnavailableCompanyDoesNotFightOrAcquireNewCasualties()
    {
        var s = CreateScenario(
            enemyInRear: true, transitContactDistanceMeters: 200,
            useSimultaneousContacts: true, includeEnemyReserve: true,
            enableMobileCombat: true);
        var opponent = DeployOpposingReserve(s);
        ReachConfirmedRupture(s);
        ApproveMovement(s, s.Reserve);
        ApproveMovement(s, opponent);
        var manpower = s.Reserve.Company.Composition!.Manpower;
        manpower.ApplyCasualties(new PersonnelCasualtyReport(100, 0, 0));
        s.Battle.PersonnelExposure.Set(s.Reserve, new TacticalPersonnelExposure(0, 0, 0, 20));

        s.Engine.Tick();

        var completion = Assert.Single(s.Orchestrator.LastMovementPhase!.Completed,
            c => ReferenceEquals(c.Order.Company, s.Reserve));
        Assert.Equal(TacticalMovementOutcome.UnitUnavailable, completion.Outcome);
        Assert.Empty(s.Battle.MobileEngagements.GetContacts(4));
        Assert.Empty(s.Battle.DirectFire.LastPhase!.Events);
        Assert.Empty(s.Battle.DirectFire.LastCasualtyPhase!.Companies);
        Assert.Equal(0, manpower.Current);
    }

    [Fact]
    public void RepeatedMobileContactAppliesOnlyTheCurrentHoursLosses()
    {
        var s = CreateScenario(
            enemyInRear: true, transitContactDistanceMeters: 200,
            useSimultaneousContacts: true, includeEnemyReserve: true,
            enableMobileCombat: true, killedProbability: 1);
        var opponent = DeployOpposingReserve(s);
        ReachConfirmedRupture(s);
        ApproveMovement(s, s.Reserve);
        ApproveMovement(s, opponent);
        var companies = new[] { s.Reserve, opponent };

        for (long hour = 4; hour <= 5; hour++)
        {
            var before = companies.Select(company =>
            {
                var battalion = company.Company.ParentBattalion!;
                return (
                    Company: company.Company.Composition!.Manpower.Current,
                    Battalion: battalion.GetResourceTotals().Personnel.Available,
                    Division: battalion.ParentDivision!.GetCurrentResourceTotals().Personnel.Available);
            }).ToArray();

            s.Engine.Tick();

            var fire = s.Battle.DirectFire.LastPhase!;
            Assert.Equal(hour, fire.Hour);
            Assert.Equal(2, fire.Events.Count);
            var casualties = s.Battle.DirectFire.LastCasualtyPhase!;
            Assert.Equal(hour, casualties.Hour);
            Assert.Equal(2, casualties.Companies.Count);
            var suppression = s.Battle.DirectFire.LastSuppressionPhase!;
            Assert.Equal(hour, suppression.Hour);
            var contact = Assert.Single(s.Battle.MobileEngagements.GetContacts(hour));
            Assert.Equal(hour == 4 ? 0.3 : 0, contact.ContactTimeFraction, 6);
            Assert.Empty(s.Battle.MobileEngagements.GetContacts(hour - 1));

            for (int i = 0; i < companies.Length; i++)
            {
                var company = companies[i];
                var applied = Assert.Single(casualties.Companies,
                    report => ReferenceEquals(report.Target, company));
                int lost = applied.Casualties.Total;
                Assert.True(lost > 0);
                int after = company.Company.Composition!.Manpower.Current;
                Assert.Equal(before[i].Company, applied.AvailableBefore);
                Assert.Equal(before[i].Company - lost, after);
                Assert.Equal(after, applied.AvailableAfter);
                Assert.Equal(after, s.Battle.PersonnelExposure.Get(company).Total);
                var battalion = company.Company.ParentBattalion!;
                Assert.Equal(before[i].Battalion - lost,
                    battalion.GetResourceTotals().Personnel.Available);
                Assert.Equal(before[i].Division - lost,
                    battalion.ParentDivision!.GetCurrentResourceTotals().Personnel.Available);
                var pressure = Assert.Single(suppression.Targets,
                    report => ReferenceEquals(report.Target, company));
                Assert.True(pressure.PressureAdded > 0);
                Assert.True(s.Battle.Cohesion.Get(company) < 100);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MobileStopCanCreateAContactWithAThirdCompany(bool crossesStop)
    {
        var s = CreateScenario(
            enemyInRear: true, transitContactDistanceMeters: 200,
            useSimultaneousContacts: true, includeEnemyReserve: true,
            includeSecondEnemyBattalion: true, enableMobileCombat: true);
        var opponent = DeployOpposingReserve(s);
        double x = crossesStop ? 1100 : -2000;
        var origin = AddSector(s, 4, x, -2000);
        var destination = AddSector(s, 5, x, 1000);
        s.Battle.Battlefield.Connect(origin, destination, 3000);
        var battalion = s.Enemy.Company.ParentBattalion!.ParentDivision!.Battalions[1];
        var third = s.Battle.DeployCompany(
            battalion.Companies[0], origin, CompanyDeploymentRole.FrontLine);
        s.Geometry.PlaceCompany(third, new TacticalPoint(x, -2000, 0), 0, 20);
        WeakenFront(s, third);
        ReachConfirmedRupture(s);
        // Donner un identifiant de piste plus ancien au contact qui surviendra en second.
        s.Perception.Apply(3, new[]
        {
            new TacticalObservationEvent(s.Reserve, third, s.Geometry.GetPosition(third)!.Point, origin.Id)
        });

        ApproveMovement(s, s.Reserve);
        ApproveMovement(s, opponent);
        ApproveMovement(s, third, destination);

        s.Engine.Tick();

        var phase = s.Orchestrator.LastMovementPhase!;
        Assert.Single(phase.PredictedEncounters!);
        if (crossesStop)
        {
            Assert.Equal(3, phase.SimultaneousResolution!.ResolutionPasses);
            Assert.Contains(third, phase.SimultaneousResolution.PausedCompanies);
            var contact = Assert.Single(phase.ConfirmedMobileContacts!,
                c => ReferenceEquals(c.First, s.Reserve) && ReferenceEquals(c.Second, third));
            Assert.Equal(0.6, contact.ContactTimeFraction, 6);
            Assert.Equal(-200, contact.SecondPosition.YMeters, 6);
            int? laterTrack = s.Perception.GetCurrentLocalizedTrackId(s.Reserve, third, 4);
            int? earlierTrack = s.Perception.GetCurrentLocalizedTrackId(s.Reserve, opponent, 4);
            Assert.NotNull(laterTrack);
            Assert.NotNull(earlierTrack);
            Assert.True(laterTrack < earlierTrack);

            var reserveSalvo = Assert.Single(s.Battle.DirectFire.LastPhase!.Events,
                salvo => ReferenceEquals(salvo.Shooter, s.Reserve));
            Assert.Equal(earlierTrack.Value, reserveSalvo.TargetTrackId);
            Assert.Equal(3, s.Battle.DirectFire.LastPhase.Events.Count);
            Assert.All(new[] { s.Reserve, opponent, third }, company =>
                Assert.Single(s.Battle.DirectFire.LastPhase.Events,
                    salvo => ReferenceEquals(salvo.Shooter, company)));
        }
        else
        {
            Assert.Single(phase.ConfirmedMobileContacts!);
            Assert.Same(third, Assert.Single(phase.Completed).Order.Company);
            Assert.Same(destination, third.Sector);
        }
    }

    [Fact]
    public void DistantDeploymentsDoNotIncreaseExactMobileContactChecks()
    {
        Assert.Equal(MeasureMobileContactChecks(0), MeasureMobileContactChecks(128));
    }

    private static (long ExactChecks, long Candidates) MeasureMobileContactChecks(int distantCount)
    {
        var s = CreateScenario(
            enemyInRear: true, transitContactDistanceMeters: 200,
            useSimultaneousContacts: true, includeEnemyReserve: true,
            enableMobileCombat: true, extraEnemyCompanies: distantCount);
        var opponent = DeployOpposingReserve(s);
        var battalion = opponent.Company.ParentBattalion!;
        for (int i = 0; i < distantCount; i++)
        {
            double x = 10000 + 1000 * i;
            var sector = AddSector(s, 4 + i, x, 0);
            var deployment = s.Battle.DeployCompany(
                battalion.Companies[2 + i], sector, CompanyDeploymentRole.Reserve);
            s.Geometry.PlaceCompany(deployment, new TacticalPoint(x, 0, 0), 0, 20);
        }

        // Les réserves éloignées ne changent pas la paire qui reçoit les ordres.
        ReachConfirmedRupture(s);
        ApproveMovement(s, s.Reserve);
        ApproveMovement(s, opponent);
        s.Engine.Tick();

        var phase = s.Orchestrator.LastMovementPhase!;
        Assert.Single(phase.ConfirmedMobileContacts!);
        var resolution = phase.SimultaneousResolution!;
        Assert.True(resolution.ExactPairChecks > 0);
        return (resolution.ExactPairChecks, resolution.BroadPhaseCandidates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdersCrossingAtDifferentTimesContinue(bool enableMobileCombat)
    {
        var s = CreateScenario(
            hasReserve: false, transitContactDistanceMeters: 100,
            useSimultaneousContacts: true, enableMobileCombat: enableMobileCombat);
        var enemyOrigin = AddSector(s, 4, 1000, -3000);
        var enemyDestination = AddSector(s, 5, 1000, 1000);
        s.Battle.Battlefield.Connect(enemyOrigin, enemyDestination, 4000);
        Relocate(s, s.Enemy, enemyOrigin);
        WeakenFront(s, s.Enemy);
        ReachConfirmedRupture(s);
        ApproveMovement(s, s.Front, s.RearSector);
        ApproveMovement(s, s.Enemy, enemyDestination);

        s.Engine.Tick();

        var phase = s.Orchestrator.LastMovementPhase!;
        Assert.Empty(phase.PredictedEncounters!);
        Assert.Empty(phase.SimultaneousResolution!.PausedCompanies);
        if (enableMobileCombat)
        {
            Assert.Empty(phase.ConfirmedMobileContacts!);
            Assert.Equal(new TacticalMobileCombatSummary(4, 0, 0, 0),
                s.Orchestrator.LastMobileCombatSummary!.Value);
        }
        else
        {
            Assert.Null(s.Orchestrator.LastMobileCombatSummary);
        }
        Assert.Same(s.Front, Assert.Single(phase.Completed).Order.Company);
        Assert.Same(s.RearSector, s.Front.Sector);
        var continuing = Assert.Single(phase.Active);
        Assert.Same(s.Enemy, continuing.Order.Company);
        Assert.Equal(3000, continuing.DistanceTravelledMeters, 6);
        Assert.Equal(new TacticalPoint(1000, 0, 0), continuing.Position);

        s.Engine.Tick();
        Assert.Same(enemyDestination, s.Enemy.Sector);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThirdMovementRespondsOnlyToItsOwnEncounter(bool crossesPausedPosition)
    {
        var s = CreateScenario(
            hasReserve: false, enemyInRear: true, transitContactDistanceMeters: 100,
            useSimultaneousContacts: true, includeSecondEnemyBattalion: true);
        var commonDestination = AddSector(s, 4, 1000, 2000);
        s.Battle.Battlefield.Connect(s.FrontSector, commonDestination, 1000);
        s.Battle.Battlefield.Connect(s.RearSector, commonDestination, 2000);

        double x = crossesPausedPosition ? 0 : -2000;
        var origin = AddSector(s, 5, x, -2000);
        var destination = AddSector(s, 6, x, 1000);
        var waypoint = AddSector(s, 7, x, -1000);
        s.Battle.Battlefield.Connect(origin, waypoint, 1000);
        s.Battle.Battlefield.Connect(waypoint, destination, 2000);
        var thirdBattalion = s.Enemy.Company.ParentBattalion!.ParentDivision!.Battalions[1];
        var third = s.Battle.DeployCompany(
            thirdBattalion.Companies[0], origin, CompanyDeploymentRole.FrontLine);
        var start = new TacticalPoint(x, -2000, 0);
        s.Geometry.PlaceCompany(third, start, 90, 20);
        WeakenFront(s, s.Enemy);
        WeakenFront(s, third);
        ReachConfirmedRupture(s);
        ApproveMovement(s, s.Front, commonDestination);
        ApproveMovement(s, s.Enemy, commonDestination);
        ApproveMovement(s, third, destination);

        s.Engine.Tick();

        var phase = s.Orchestrator.LastMovementPhase!;
        var resolution = phase.SimultaneousResolution!;
        Assert.Contains(s.Front, resolution.PausedCompanies);
        Assert.Contains(s.Enemy, resolution.PausedCompanies);
        Assert.Equal(crossesPausedPosition ? 3 : 2, resolution.ResolutionPasses);
        if (crossesPausedPosition)
        {
            Assert.Contains(third, resolution.PausedCompanies);
            var encounter = Assert.Single(phase.PredictedEncounters!,
                e => ReferenceEquals(e.First, s.Front) && ReferenceEquals(e.Second, third));
            Assert.Equal(2.0 / 3, encounter.TimeFraction, 6);
            Assert.Empty(phase.Completed);
            Assert.Same(origin, third.Sector);
            Assert.Equal(start, s.Geometry.GetPosition(third)!.Point);
        }
        else
        {
            Assert.DoesNotContain(third, resolution.PausedCompanies);
            Assert.Single(phase.PredictedEncounters!);
            var completion = Assert.Single(phase.Completed);
            Assert.Same(third, completion.Order.Company);
            Assert.Equal(TacticalMovementOutcome.Arrived, completion.Outcome);
            Assert.Same(destination, third.Sector);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidPendingMovementLeavesOtherDeploymentsUntouched(bool enableMobileCombat)
    {
        var s = CreateScenario(
            hasReserve: false, enemyInRear: true, transitContactDistanceMeters: 100,
            useSimultaneousContacts: true, enableMobileCombat: enableMobileCombat);
        var firstDestination = AddSector(s, 4, 0, 1000);
        var secondDestination = AddSector(s, 5, 2000, 1000);
        s.Battle.Battlefield.Connect(s.FrontSector, firstDestination, 1000);
        s.Battle.Battlefield.Connect(s.RearSector, secondDestination, 1000);
        WeakenFront(s, s.Enemy);
        ReachConfirmedRupture(s);
        ApproveMovement(s, s.Front, firstDestination);
        ApproveMovement(s, s.Enemy, secondDestination);

        var positions = s.Geometry.CompanyPositions.ToArray();
        var orders = s.Movements.PendingMovements.ToArray();
        var previousPhase = s.Movements.LastMovementPhase;
        long geometryRevision = s.Geometry.Revision;
        s.Enemy.SetInTransit(false);
        long deploymentRevision = s.Battle.DeploymentRevision;

        Assert.Throws<InvalidOperationException>(() => s.Movements.AdvanceHour(4));

        Assert.Equal(positions, s.Geometry.CompanyPositions.ToArray());
        Assert.Equal(orders, s.Movements.PendingMovements.ToArray());
        Assert.Same(previousPhase, s.Movements.LastMovementPhase);
        Assert.Equal(deploymentRevision, s.Battle.DeploymentRevision);
        Assert.Equal(geometryRevision, s.Geometry.Revision);
        Assert.Same(s.FrontSector, s.Front.Sector);
        Assert.Same(s.RearSector, s.Enemy.Sector);

        s.Enemy.SetInTransit(true);
        Assert.Equal(2, s.Movements.AdvanceHour(4).Completed.Count);
    }

    [Fact]
    public void EnemyOccupationBlocksLaterWaypoint()
    {
        var s = CreateScenario(hasReserve: false);

        var farRear = new BattleSector(
            4, "Far Rear", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(farRear);
        s.Battle.Battlefield.Connect(
            s.RearSector, farRear, 5000);

        s.Geometry.DefineSector(farRear, 7000, 0, 100);

        var request = ReachConfirmedRupture(s);

        s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Front,
            destination: farRear);

        s.Engine.Tick(); // Atteint Rear, puis commence le transit.

        Assert.Same(s.RearSector, s.Front.Sector);

        // L'ennemi occupe maintenant le secteur final.
        s.Geometry.RelocateCompany(s.Enemy, farRear,
            s.Geometry.GetSectorCenterPoint(farRear));

        s.Enemy.ApplyMovementArrival(
            farRear,
            CompanyDeploymentRole.FrontLine);

        s.Engine.Tick();

        Assert.False(s.Front.IsInTransit);

        // Annulation au dernier secteur sur de la route.
        Assert.Same(s.RearSector, s.Front.Sector);

        Assert.Single(
            s.Orchestrator.LastMovementPhase!.Completed,
            c => ReferenceEquals(c.Order.Company, s.Front)
                && c.Outcome == TacticalMovementOutcome.BlockedByEnemy);
    }



    [Fact]
    public void SpatialIndexReturnsNearbyCompaniesOnly()
    {
        var s = CreateScenario();

        var index = new TacticalCompanySpatialIndex(
            s.Battle, s.Geometry, cellSizeMeters: 250);

        var candidates = index.QueryNearSegment(
            new TacticalPoint(-100, 0, 0),
            new TacticalPoint(100, 0, 0),
            radiusMeters: 50);

        Assert.Contains(s.Front, candidates);
        Assert.Contains(s.Enemy, candidates);

        // La reserve se trouve a -4000 metres.
        Assert.DoesNotContain(s.Reserve, candidates);

        Assert.Equal(3, index.Statistics.IndexedCompanies);
        Assert.Equal(1, index.Statistics.Queries);
    }

    [Fact]
    public void SpatialIndexSupportsNegativeCoordinates()
    {
        var s = CreateScenario();

        var index = new TacticalCompanySpatialIndex(
            s.Battle, s.Geometry, cellSizeMeters: 200);

        var candidates = index.QueryNearSegment(
            new TacticalPoint(-4500, -50, 0),
            new TacticalPoint(-3500, 50, 0),
            radiusMeters: 50);

        Assert.Contains(s.Reserve, candidates);
        Assert.DoesNotContain(s.Front, candidates);
        Assert.DoesNotContain(s.Enemy, candidates);
    }

    [Fact]
    public void SpatialIndexDeduplicatesCandidatesAcrossCells()
    {
        var s = CreateScenario();

        var index = new TacticalCompanySpatialIndex(
            s.Battle, s.Geometry, cellSizeMeters: 100);

        var candidates = index.QueryNearSegment(
            new TacticalPoint(-4000, 0, 0),
            new TacticalPoint(2000, 0, 0),
            radiusMeters: 50);

        Assert.Equal(3, candidates.Count);

        // Une compagnie n'apparait jamais deux fois,
        // meme si la recherche traverse de nombreuses
        // cellules de voisinage.
        Assert.Single(
            candidates,
            d => ReferenceEquals(d, s.Enemy));

        Assert.Equal(
            s.Battle.Deployments.ToArray(),
            candidates.ToArray());
    }

    [Fact]
    public void GeometryChangeInvalidatesIndexUntilRefreshed()
    {
        var s = CreateScenario();

        var index = new TacticalCompanySpatialIndex(
            s.Battle, s.Geometry, cellSizeMeters: 200);

        // Changer la position et l'affectation d'une
        // compagnie en utilisant les APIs existantes.
        s.Geometry.RelocateCompany(
            s.Enemy, s.RearSector,
            s.Geometry.GetSectorCenterPoint(s.RearSector));

        s.Enemy.ApplyMovementArrival(
            s.RearSector,
            CompanyDeploymentRole.FrontLine);

        // Une ancienne photographie de positions
        // ne doit pas etre exploitee silencieusement.
        Assert.Throws<InvalidOperationException>(() =>
            index.QueryNearSegment(
                new TacticalPoint(1900, 0, 0),
                new TacticalPoint(2100, 0, 0),
                radiusMeters: 25));

        index.RefreshPosition(s.Enemy);

        var nearRear = index.QueryNearSegment(
            new TacticalPoint(1900, 0, 0),
            new TacticalPoint(2100, 0, 0),
            radiusMeters: 25);

        Assert.Contains(s.Enemy, nearRear);

        var nearFront = index.QueryNearSegment(
            new TacticalPoint(-100, 0, 0),
            new TacticalPoint(100, 0, 0),
            radiusMeters: 25);

        Assert.DoesNotContain(s.Enemy, nearFront);
        Assert.Contains(s.Front, nearFront);
    }

    [Fact]
    public void SpatialIndexRejectsInvalidConfiguration()
    {
        var s = CreateScenario();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalCompanySpatialIndex(
                s.Battle, s.Geometry, cellSizeMeters: 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalCompanySpatialIndex(
                s.Battle, s.Geometry, cellSizeMeters: double.NaN));

        var index = new TacticalCompanySpatialIndex(
            s.Battle, s.Geometry, cellSizeMeters: 200);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            index.QueryNearSegment(
                new TacticalPoint(0, 0, 0),
                new TacticalPoint(100, 0, 0),
                radiusMeters: -1));
    }

    [Fact]
    public void TransitContactUsesSpatialIndexWithoutChangingCombatRules()
    {
        var s = CreateScenario(
            hasReserve: false,
            transitContactDistanceMeters: 100);

        var request = ReachConfirmedRupture(s);

        s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Front,
            destination: s.RearSector);

        int manpowerBefore =
            s.Front.Company.Composition!.Manpower.Current;

        s.Engine.Tick();

        // La rencontre deja attendue par C2.4d.7
        // doit toujours exister.
        Assert.Single(
            s.Orchestrator.LastMovementPhase!.Contacts,
            c => ReferenceEquals(c.MovingCompany, s.Front)
                && ReferenceEquals(c.EnemyCompany, s.Enemy));

        Assert.True(s.Front.IsInTransit);

        var stats = s.Movements.LastSpatialIndexStatistics;

        Assert.NotNull(stats);
        Assert.Equal(3, stats.Value.IndexedCompanies);
        Assert.True(stats.Value.Queries > 0);

        // Aucune nouvelle victime n'est produite
        // par l'indexation spatiale.
        Assert.Equal(
            manpowerBefore,
            s.Front.Company.Composition!.Manpower.Current);
    }



    [Fact]
    public void OpposingMovementsMeetAtSameTime()
    {
        var s = CreateScenario();

        var segments = new[]
        {
            new TacticalTimedTrajectorySegment(
                s.Front,
                new TacticalPoint(0, 0, 0),
                new TacticalPoint(100, 0, 0),
                0, 1, IsMoving: true),

            new TacticalTimedTrajectorySegment(
                s.Enemy,
                new TacticalPoint(100, 0, 0),
                new TacticalPoint(0, 0, 0),
                0, 1, IsMoving: true)
        };

        var result = TacticalSimultaneousContactDetector.Detect(
            s.Battle, s.Engine.Diplomacy, segments,
            hour: 4, contactRadiusMeters: 5);

        var encounter = Assert.Single(result.Encounters);

        Assert.Same(s.Front, encounter.First);
        Assert.Same(s.Enemy, encounter.Second);

        Assert.Equal(0.5, encounter.TimeFraction, 6);
        Assert.Equal(0, encounter.DistanceMeters, 6);
        Assert.Equal(50, encounter.EstimatedPosition.XMeters, 6);
    }

    [Fact]
    public void CrossingSamePointAtDifferentTimesIsNotContact()
    {
        var s = CreateScenario();

        var segments = new[]
        {
            // Traverse (50, 0) a t = 0.5.
            new TacticalTimedTrajectorySegment(
                s.Front,
                new TacticalPoint(0, 0, 0),
                new TacticalPoint(100, 0, 0),
                0, 1, IsMoving: true),

            // Traverse (50, 0) a t = 0.75.
            new TacticalTimedTrajectorySegment(
                s.Enemy,
                new TacticalPoint(50, -150, 0),
                new TacticalPoint(50, 50, 0),
                0, 1, IsMoving: true)
        };

        var result = TacticalSimultaneousContactDetector.Detect(
            s.Battle, s.Engine.Diplomacy, segments,
            hour: 4, contactRadiusMeters: 5);

        Assert.Empty(result.Encounters);
    }

    [Fact]
    public void MovingCompanyCanMeetStationaryEnemy()
    {
        var s = CreateScenario();

        var segments = new[]
        {
            new TacticalTimedTrajectorySegment(
                s.Front,
                new TacticalPoint(0, 0, 0),
                new TacticalPoint(100, 0, 0),
                0, 1, IsMoving: true),

            new TacticalTimedTrajectorySegment(
                s.Enemy,
                new TacticalPoint(50, 0, 0),
                new TacticalPoint(50, 0, 0),
                0, 1, IsMoving: false)
        };

        var result = TacticalSimultaneousContactDetector.Detect(
            s.Battle, s.Engine.Diplomacy, segments,
            hour: 4, contactRadiusMeters: 5);

        var encounter = Assert.Single(result.Encounters);

        Assert.Equal(0.5, encounter.TimeFraction, 6);
        Assert.Equal(0, encounter.DistanceMeters, 6);
    }

    [Fact]
    public void FriendlyCompaniesNeverCreateEnemyEncounter()
    {
        var s = CreateScenario();

        var segments = new[]
        {
            new TacticalTimedTrajectorySegment(
                s.Front,
                new TacticalPoint(0, 0, 0),
                new TacticalPoint(100, 0, 0),
                0, 1, IsMoving: true),

            new TacticalTimedTrajectorySegment(
                s.Reserve,
                new TacticalPoint(100, 0, 0),
                new TacticalPoint(0, 0, 0),
                0, 1, IsMoving: true)
        };

        var result = TacticalSimultaneousContactDetector.Detect(
            s.Battle, s.Engine.Diplomacy, segments,
            hour: 4, contactRadiusMeters: 5);

        Assert.Empty(result.Encounters);
    }

    [Fact]
    public void SegmentedTrajectoryProducesSingleEncounterPerPair()
    {
        var s = CreateScenario();

        var segments = new[]
        {
            new TacticalTimedTrajectorySegment(
                s.Front,
                new TacticalPoint(0, 0, 0),
                new TacticalPoint(50, 0, 0),
                0, 0.5, IsMoving: true),

            new TacticalTimedTrajectorySegment(
                s.Front,
                new TacticalPoint(50, 0, 0),
                new TacticalPoint(100, 0, 0),
                0.5, 1, IsMoving: true),

            new TacticalTimedTrajectorySegment(
                s.Enemy,
                new TacticalPoint(50, 0, 0),
                new TacticalPoint(50, 0, 0),
                0, 1, IsMoving: false)
        };

        var result = TacticalSimultaneousContactDetector.Detect(
            s.Battle, s.Engine.Diplomacy, segments,
            hour: 4, contactRadiusMeters: 0);

        var encounter = Assert.Single(result.Encounters);

        Assert.Equal(0.5, encounter.TimeFraction, 6);
    }

    [Fact]
    public void ContactDetectionDoesNotDependOnSegmentOrder()
    {
        var s = CreateScenario();

        var first = new TacticalTimedTrajectorySegment(
            s.Front,
            new TacticalPoint(0, 0, 0),
            new TacticalPoint(100, 0, 0),
            0, 1, IsMoving: true);

        var second = new TacticalTimedTrajectorySegment(
            s.Enemy,
            new TacticalPoint(100, 0, 0),
            new TacticalPoint(0, 0, 0),
            0, 1, IsMoving: true);

        var forward = TacticalSimultaneousContactDetector.Detect(
            s.Battle,
            s.Engine.Diplomacy,
            new[] { first, second },
            4, 5);

        var reverse = TacticalSimultaneousContactDetector.Detect(
            s.Battle,
            s.Engine.Diplomacy,
            new[] { second, first },
            4, 5);

        Assert.Equal(forward.Encounters, reverse.Encounters);
        Assert.Equal(
            forward.Statistics.ExactPairChecks,
            reverse.Statistics.ExactPairChecks);
    }

    [Fact]
    public void SimultaneousResolverPausesBothOpposingMovers()
    {
        var s = CreateScenario();

        IReadOnlyList<TacticalTimedTrajectorySegment> Build(
            IReadOnlySet<CompanyDeployment> paused)
        {
            TacticalTimedTrajectorySegment Make(
                CompanyDeployment company,
                double fromX,
                double toX)
            {
                bool stopped = paused.Contains(company);

                return new TacticalTimedTrajectorySegment(
                    company,
                    new TacticalPoint(fromX, 0, 0),
                    new TacticalPoint(
                        stopped ? fromX : toX, 0, 0),
                    0, 1,
                    IsMoving: !stopped);
            }

            return new[]
            {
                Make(s.Front, 0, 100),
                Make(s.Enemy, 100, 0)
            };
        }

        var result = TacticalSimultaneousMovementResolver.Resolve(
            s.Battle,
            s.Engine.Diplomacy,
            new[] { s.Front, s.Enemy },
            Build,
            hour: 4,
            contactRadiusMeters: 5);

        Assert.Contains(s.Front, result.PausedCompanies);
        Assert.Contains(s.Enemy, result.PausedCompanies);

        Assert.Equal(2, result.PausedCompanies.Count);
        Assert.Equal(2, result.ResolutionPasses);

        Assert.Single(result.PredictedEncounters);
    }

    [Fact]
    public void InvalidTrajectoryIsRejected()
    {
        var s = CreateScenario();

        var invalid = new[]
        {
            new TacticalTimedTrajectorySegment(
                s.Front,
                new TacticalPoint(0, 0, 0),
                new TacticalPoint(100, 0, 0),
                StartTime: 0.8,
                EndTime: 0.2,
                IsMoving: true)
        };

        Assert.Throws<InvalidOperationException>(() =>
            TacticalSimultaneousContactDetector.Detect(
                s.Battle,
                s.Engine.Diplomacy,
                invalid,
                hour: 4,
                contactRadiusMeters: 10));
    }




    [Fact]
    public void FirstMobileContactOccursBeforePositionsOverlap()
    {
        var s = CreateScenario();

        var first = new TacticalTimedTrajectorySegment(
            s.Front,
            new TacticalPoint(0, 0, 0),
            new TacticalPoint(100, 0, 0),
            0, 1,
            IsMoving: true);

        var second = new TacticalTimedTrajectorySegment(
            s.Enemy,
            new TacticalPoint(200, 0, 0),
            new TacticalPoint(100, 0, 0),
            0, 1,
            IsMoving: true);

        var result = TacticalFirstMobileContactSolver.Find(
            first,
            second,
            radiusMeters: 20);

        Assert.NotNull(result);

        Assert.Equal(
            0.9, result.Value.TimeFraction, 6);

        Assert.Equal(
            90, result.Value.FirstPosition.XMeters, 6);

        Assert.Equal(
            110, result.Value.SecondPosition.XMeters, 6);

        Assert.Equal(
            20, result.Value.DistanceMeters, 6);
    }

    [Fact]
    public void MobileContactDoesNotExistAtDifferentPassageTimes()
    {
        var s = CreateScenario();

        var first = new TacticalTimedTrajectorySegment(
            s.Front,
            new TacticalPoint(0, 0, 0),
            new TacticalPoint(100, 0, 0),
            0, 1,
            IsMoving: true);

        var second = new TacticalTimedTrajectorySegment(
            s.Enemy,
            new TacticalPoint(50, -150, 0),
            new TacticalPoint(50, 50, 0),
            0, 1,
            IsMoving: true);

        var result = TacticalFirstMobileContactSolver.Find(
            first,
            second,
            radiusMeters: 5);

        Assert.Null(result);
    }

    [Fact]
    public void StationaryEnemyCanBeReachedByMovingCompany()
    {
        var s = CreateScenario();

        var moving = new TacticalTimedTrajectorySegment(
            s.Front,
            new TacticalPoint(0, 0, 0),
            new TacticalPoint(100, 0, 0),
            0, 1,
            IsMoving: true);

        var stationary = new TacticalTimedTrajectorySegment(
            s.Enemy,
            new TacticalPoint(50, 0, 0),
            new TacticalPoint(50, 0, 0),
            0, 1,
            IsMoving: false);

        var result = TacticalFirstMobileContactSolver.Find(
            moving,
            stationary,
            radiusMeters: 10);

        Assert.NotNull(result);

        Assert.Equal(
            0.4, result.Value.TimeFraction, 6);

        Assert.Equal(
            40, result.Value.FirstPosition.XMeters, 6);

        Assert.Equal(
            50, result.Value.SecondPosition.XMeters, 6);
    }

    [Fact]
    public void InvalidMobileContactRadiusIsRejected()
    {
        var s = CreateScenario();

        var moving = new TacticalTimedTrajectorySegment(
            s.Front,
            new TacticalPoint(0, 0, 0),
            new TacticalPoint(100, 0, 0),
            0, 1,
            IsMoving: true);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TacticalFirstMobileContactSolver.Find(
                moving,
                moving,
                radiusMeters: 0));
    }




    [Fact]
    public void MobileCombatRecordsMovementSubphases()
    {
        var recorder = new TacticalPhaseTimingRecorder();

        var s = CreateScenario(
            enemyInRear: true,
            transitContactDistanceMeters: 200,
            useSimultaneousContacts: true,
            includeEnemyReserve: true,
            enableMobileCombat: true,
            timingRecorder: recorder);

        var opponent = DeployOpposingReserve(s);

        ReachConfirmedRupture(s);

        ApproveMovement(s, s.Reserve);
        ApproveMovement(s, opponent);

        // Ne mesurer que l'heure du contact.
        recorder.Reset();

        s.Engine.Tick();

        var measurements = recorder.Snapshot();
        var movement = s.Orchestrator.LastMovementPhase!;
        int passes = movement.SimultaneousResolution!.ResolutionPasses;
        Assert.True(passes > 1);

        var phases = new[]
        {
            TacticalBenchmarkPhase.MovementPreparation,
            TacticalBenchmarkPhase.MovementContactDetection,
            TacticalBenchmarkPhase.MovementStabilization,
            TacticalBenchmarkPhase.MovementPositionValidation,
            TacticalBenchmarkPhase.MovementApplication,
            TacticalBenchmarkPhase.MovementConfirmation
        };

        foreach (var phase in phases)
        {
            var measurement = Assert.Single(
                measurements,
                m => m.Phase == phase);

            int expectedCalls = phase is TacticalBenchmarkPhase.MovementContactDetection
                or TacticalBenchmarkPhase.MovementStabilization
                ? passes
                : 1;
            Assert.Equal(expectedCalls, measurement.Calls);
            Assert.True(double.IsFinite(measurement.Milliseconds));
            Assert.True(measurement.Milliseconds >= 0);
            Assert.True(measurement.AllocatedBytes >= 0);
        }

        Assert.Single(movement.ConfirmedMobileContacts!);
    }




    [Fact]
    public void MovementInstrumentationDoesNotChangeCombat()
    {
        var recorder = new TacticalPhaseTimingRecorder();

        var normal = CreateScenario(
            enemyInRear: true,
            transitContactDistanceMeters: 200,
            useSimultaneousContacts: true,
            includeEnemyReserve: true,
            enableMobileCombat: true,
            killedProbability: 1);

        var measured = CreateScenario(
            enemyInRear: true,
            transitContactDistanceMeters: 200,
            useSimultaneousContacts: true,
            includeEnemyReserve: true,
            enableMobileCombat: true,
            killedProbability: 1,
            timingRecorder: recorder);

        var normalOpponent = DeployOpposingReserve(normal);
        var measuredOpponent = DeployOpposingReserve(measured);

        ReachConfirmedRupture(normal);
        ReachConfirmedRupture(measured);

        ApproveMovement(normal, normal.Reserve);
        ApproveMovement(normal, normalOpponent);

        ApproveMovement(measured, measured.Reserve);
        ApproveMovement(measured, measuredOpponent);

        recorder.Reset();

        normal.Engine.Tick();
        measured.Engine.Tick();

        Assert.Equal(
            normal.Orchestrator.LastResult,
            measured.Orchestrator.LastResult);

        Assert.Equal(
            normal.Orchestrator.LastMobileCombatSummary,
            measured.Orchestrator.LastMobileCombatSummary);

        var a = Assert.Single(
            normal.Orchestrator.LastMovementPhase!
                .ConfirmedMobileContacts!);

        var b = Assert.Single(
            measured.Orchestrator.LastMovementPhase!
                .ConfirmedMobileContacts!);

        Assert.Equal(a with { First = measured.Reserve, Second = measuredOpponent }, b);
        Assert.Same(normal.Reserve, a.First);
        Assert.Same(normalOpponent, a.Second);

        var normalFire = normal.Battle.DirectFire.LastPhase!;
        var measuredFire = measured.Battle.DirectFire.LastPhase!;
        Assert.Equal(normalFire.Outcomes.ToArray(), measuredFire.Outcomes.ToArray());
        Assert.Equal(2, normalFire.Events.Count);
        Assert.Equal(2, measuredFire.Events.Count);

        var normalLosses = normal.Battle.DirectFire.LastCasualtyPhase!;
        var measuredLosses = measured.Battle.DirectFire.LastCasualtyPhase!;
        Assert.Equal(2, normalLosses.Companies.Count);
        Assert.Equal(2, measuredLosses.Companies.Count);

        foreach (var (expected, actual) in new[]
        {
            (normal.Reserve, measured.Reserve),
            (normalOpponent, measuredOpponent)
        })
        {
            Assert.Equal(normal.Geometry.GetPosition(expected)!.Point,
                measured.Geometry.GetPosition(actual)!.Point);
            Assert.Equal(expected.Sector.Id, actual.Sector.Id);
            Assert.Equal(expected.IsInTransit, actual.IsInTransit);

            var expectedComposition = expected.Company.Composition!;
            var actualComposition = actual.Company.Composition!;
            Assert.Equal(expectedComposition.Manpower.Current, actualComposition.Manpower.Current);
            Assert.Equal(expectedComposition.Ammunition.GetAvailable(AmmunitionType.SmallArms),
                actualComposition.Ammunition.GetAvailable(AmmunitionType.SmallArms));

            var expectedLoss = Assert.Single(normalLosses.Companies,
                loss => ReferenceEquals(loss.Target, expected));
            var actualLoss = Assert.Single(measuredLosses.Companies,
                loss => ReferenceEquals(loss.Target, actual));
            Assert.True(expectedLoss.Casualties.Total > 0);
            Assert.Equal(expectedLoss with { Target = actual }, actualLoss);

            var expectedSalvo = Assert.Single(normalFire.Events,
                salvo => ReferenceEquals(salvo.Shooter, expected));
            var actualSalvo = Assert.Single(measuredFire.Events,
                salvo => ReferenceEquals(salvo.Shooter, actual));
            Assert.True(expectedSalvo.RoundsExpended > 0);
            Assert.Equal(expectedSalvo with { Shooter = actual }, actualSalvo);
        }

        Assert.Equal(
            normal.Battle.Cohesion.Get(normal.Reserve),
            measured.Battle.Cohesion.Get(measured.Reserve));

        Assert.NotEmpty(recorder.Snapshot());
    }




    private static TacticalBattalionReinforcementCommandSystem
        CreateCommander(Scenario s)
    {
        return new TacticalBattalionReinforcementCommandSystem(
            s.Orchestrator);
    }

    [Fact]
    public void CommanderApprovesReinforcementAndCompanyArrives()
    {
        var s = CreateScenario();

        var request = ReachConfirmedRupture(s);

        Assert.Equal(
            TacticalBattalionReactionType.Reinforce,
            request.Type);

        var commander = CreateCommander(s);

        int manpowerBefore =
            s.Reserve.Company.Composition!.Manpower.Current;

        double cohesionBefore = s.Reserve.Company.Cohesion;

        var decision = commander.TryApprove(request);

        Assert.NotNull(decision);
        Assert.Equal(
            TacticalReactionDecision.Approve,
            decision!.Decision);

        var order = Assert.IsType<TacticalSectorMovementOrder>(
            decision.Movement);

        Assert.Same(s.Reserve, order.Company);
        Assert.Same(s.FrontSector, order.Destination);

        // L'ordre est emis a H3.
        // Aucun deplacement instantane.
        Assert.Equal(3, s.Engine.Clock.CurrentHour);
        Assert.True(s.Reserve.IsInTransit);
        Assert.Same(s.ReserveSector, s.Reserve.Sector);

        s.Engine.Tick(); // H4 : progression.

        Assert.True(s.Movements.IsMoving(s.Reserve));

        s.Engine.Tick(); // H5 : arrivee.

        Assert.False(s.Movements.IsMoving(s.Reserve));
        Assert.False(s.Reserve.IsInTransit);
        Assert.Same(s.FrontSector, s.Reserve.Sector);

        Assert.Equal(
            CompanyDeploymentRole.FrontLine,
            s.Reserve.Role);

        Assert.Equal(
            manpowerBefore,
            s.Reserve.Company.Composition.Manpower.Current);

        // La selection du renfort ne cree pas
        // de perte ou de degradation artificielle.
        // Les effets d'un combat ulterieur restent
        // geres par les mecanismes existants.
    }

    [Fact]
    public void DefaultScenarioDoesNotApproveRequestsAutomatically()
    {
        var s = CreateScenario();

        var request = ReachConfirmedRupture(s);

        Assert.Equal(
            TacticalBattalionReactionType.Reinforce,
            request.Type);

        // Aucun commandant automatique n'a ete invoque.
        Assert.False(s.Reserve.IsInTransit);
        Assert.False(s.Movements.IsMoving(s.Reserve));
        Assert.Same(s.ReserveSector, s.Reserve.Sector);
    }

    [Fact]
    public void CommanderCannotApproveUnreachableReserve()
    {
        var s = CreateScenario(connectReserve: false);

        var request = ReachConfirmedRupture(s);
        var commander = CreateCommander(s);

        var result = commander.TryApprove(request);

        Assert.Null(result);
        Assert.False(s.Reserve.IsInTransit);
        Assert.False(s.Movements.IsMoving(s.Reserve));
        Assert.Empty(s.Movements.PendingMovements);
    }

    [Fact]
    public void CommanderDoesNotAutomaticallyApproveWithdrawal()
    {
        var s = CreateScenario(hasReserve: false);

        var request = ReachConfirmedRupture(s);

        Assert.Equal(
            TacticalBattalionReactionType.RequestWithdrawal,
            request.Type);

        var commander = CreateCommander(s);

        Assert.Null(commander.TryApprove(request));

        Assert.False(s.Movements.IsMoving(s.Front));
        Assert.False(s.Front.IsInTransit);
        Assert.Same(s.FrontSector, s.Front.Sector);
    }

    [Fact]
    public void CommanderCannotApproveSameRequestTwice()
    {
        var s = CreateScenario();

        var request = ReachConfirmedRupture(s);
        var commander = CreateCommander(s);

        Assert.NotNull(commander.TryApprove(request));

        Assert.Throws<InvalidOperationException>(() =>
            commander.TryApprove(request));

        Assert.Single(s.Movements.PendingMovements);
    }

    [Fact]
    public void CommanderUsesOrchestratorMovementService()
    {
        var s = CreateScenario();
        var request = ReachConfirmedRupture(s);

        var commander =
            new TacticalBattalionReinforcementCommandSystem(
                s.Orchestrator);

        double cohesionBefore = s.Reserve.Company.Cohesion;

        var decision = commander.TryApprove(request);

        Assert.NotNull(decision);
        Assert.Same(
            s.Reserve,
            decision!.Movement!.Company);

        Assert.True(
            s.Orchestrator.ReactionExecution!
                .IsMoving(s.Reserve));

        Assert.Equal(
            cohesionBefore,
            s.Reserve.Company.Cohesion,
            6);
    }

    [Fact]
    public void CommanderUsesValidDetourWhenShortcutLacksGeometry()
    {
        var s = CreateScenario();
        var request = ReachConfirmedRupture(s);

        // La liaison directe reserve -> front
        // mesure 4000 metres et est geometree.
        // Un raccourci de 200 metres traverse
        // un secteur sans geometrie.
        var shortcut = new BattleSector(
            4, "Incomplete shortcut", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(shortcut);

        s.Battle.Battlefield.Connect(
            s.ReserveSector, shortcut, 100);

        s.Battle.Battlefield.Connect(
            shortcut, s.FrontSector, 100);

        var commander =
            new TacticalBattalionReinforcementCommandSystem(
                s.Orchestrator);

        var decision = commander.TryApprove(request);

        Assert.NotNull(decision);

        var movement = Assert.IsType<TacticalSectorMovementOrder>(
            decision!.Movement);

        Assert.Equal(4000, movement.TotalDistanceMeters);

        Assert.Equal(
            new[] { s.ReserveSector, s.FrontSector },
            movement.Route);

        Assert.DoesNotContain(shortcut, movement.Route);
    }

    [Fact]
    public void CommanderRejectsRouteWithOnlyUnmappedConnection()
    {
        var s = CreateScenario(connectReserve: false);
        var request = ReachConfirmedRupture(s);

        var shortcut = new BattleSector(
            4, "Unmapped", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(shortcut);

        s.Battle.Battlefield.Connect(
            s.ReserveSector, shortcut, 100);

        s.Battle.Battlefield.Connect(
            shortcut, s.FrontSector, 100);

        var commander =
            new TacticalBattalionReinforcementCommandSystem(
                s.Orchestrator);

        Assert.Null(commander.TryApprove(request));
        Assert.False(s.Reserve.IsInTransit);
        Assert.Empty(s.Movements.PendingMovements);
    }




    [Fact]
    public void AutomaticReinforcementStartsAfterH3AndArrivesAtH5()
    {
        var s = CreateScenario(
            enableAutomaticReinforcements: true);

        // H2 : secteur menace, pas de renfort encore.
        s.Engine.Tick();

        Assert.Equal(2, s.Engine.Clock.CurrentHour);

        var h2 = Assert.IsType<
            TacticalAutomaticReinforcementPhaseResult>(
                s.Orchestrator.LastAutomaticReinforcementPhase);

        Assert.Empty(h2.Decisions);
        Assert.False(s.Reserve.IsInTransit);

        // H3 : rupture confirmee.
        s.Engine.Tick();

        Assert.Equal(3, s.Engine.Clock.CurrentHour);

        var h3 = Assert.IsType<
            TacticalAutomaticReinforcementPhaseResult>(
                s.Orchestrator.LastAutomaticReinforcementPhase);

        var decision = Assert.Single(h3.Decisions);

        Assert.Equal(
            TacticalAutomaticReinforcementStatus.Approved,
            decision.Status);

        Assert.Same(s.Reserve, decision.Movement!.Company);

        // L'ordre est approuve, mais la compagnie
        // n'a pas encore quitte son secteur de depart.
        Assert.True(s.Reserve.IsInTransit);
        Assert.Same(s.ReserveSector, s.Reserve.Sector);

        Assert.Equal(
            3, s.Orchestrator.LastResult!.Value.Hour);

        // H4 : progression.
        s.Engine.Tick();

        Assert.True(s.Movements.IsMoving(s.Reserve));
        Assert.Same(s.ReserveSector, s.Reserve.Sector);

        // H5 : arrivee.
        s.Engine.Tick();

        Assert.False(s.Movements.IsMoving(s.Reserve));
        Assert.False(s.Reserve.IsInTransit);
        Assert.Same(s.FrontSector, s.Reserve.Sector);

        Assert.Equal(
            CompanyDeploymentRole.FrontLine,
            s.Reserve.Role);
    }

    [Fact]
    public void DisabledAutomaticCommandKeepsRequestsManual()
    {
        var s = CreateScenario();

        var request = ReachConfirmedRupture(s);

        Assert.Equal(
            TacticalBattalionReactionType.Reinforce,
            request.Type);

        Assert.Null(
            s.Orchestrator.LastAutomaticReinforcementPhase);

        Assert.False(s.Reserve.IsInTransit);
        Assert.False(s.Movements.IsMoving(s.Reserve));

        Assert.Empty(s.Movements.PendingMovements);
    }

    [Fact]
    public void AutomaticCommandDoesNotApproveWithdrawal()
    {
        var s = CreateScenario(
            hasReserve: false,
            enableAutomaticReinforcements: true);

        var request = ReachConfirmedRupture(s);

        Assert.Equal(
            TacticalBattalionReactionType.RequestWithdrawal,
            request.Type);

        var phase = Assert.IsType<
            TacticalAutomaticReinforcementPhaseResult>(
                s.Orchestrator.LastAutomaticReinforcementPhase);

        Assert.Empty(phase.Decisions);

        Assert.False(s.Movements.IsMoving(s.Front));
        Assert.False(s.Front.IsInTransit);
    }

    [Fact]
    public void UnreachableReserveDoesNotInterruptCompletedHour()
    {
        var s = CreateScenario(
            connectReserve: false,
            enableAutomaticReinforcements: true);

        ReachConfirmedRupture(s);

        var phase = Assert.IsType<
            TacticalAutomaticReinforcementPhaseResult>(
                s.Orchestrator.LastAutomaticReinforcementPhase);

        var decision = Assert.Single(phase.Decisions);

        Assert.Equal(
            TacticalAutomaticReinforcementStatus.NoEligibleReserve,
            decision.Status);

        Assert.Null(decision.Movement);

        Assert.Equal(
            3, s.Orchestrator.LastResult!.Value.Hour);

        Assert.False(s.Reserve.IsInTransit);
        Assert.Empty(s.Movements.PendingMovements);

        // L'heure suivante reste executable.
        s.Engine.Tick();

        Assert.Equal(4, s.Engine.Clock.CurrentHour);
    }

    [Fact]
    public void AutomaticApprovalDoesNotChangeResolvedCombatResults()
    {
        var manual = CreateScenario();

        var automatic = CreateScenario(
            enableAutomaticReinforcements: true);

        ReachConfirmedRupture(manual);
        ReachConfirmedRupture(automatic);

        // Les deux batailles ont execute les memes
        // combats. Seule l'approbation change.
        Assert.Equal(
            manual.Orchestrator.LastResult,
            automatic.Orchestrator.LastResult);

        Assert.Equal(
            manual.Battalion.ParentDivision!
                .GetCurrentResourceTotals(),
            automatic.Battalion.ParentDivision!
                .GetCurrentResourceTotals());

        Assert.Equal(
            manual.Reserve.Company.Cohesion,
            automatic.Reserve.Company.Cohesion,
            6);

        Assert.False(manual.Reserve.IsInTransit);
        Assert.True(automatic.Reserve.IsInTransit);
    }



    [Fact]
    public void BrokenOppositionIsNotConsideredCombatCapable()
    {
        var s = CreateScenario(
            enableAutomaticReinforcements: true);

        Assert.True(
            s.Orchestrator.HasCombatCapableOpposition());

        var enemyDivision =
            s.Enemy.Company.ParentBattalion!
                .ParentDivision!;

        enemyDivision.LoseOrganization(
            enemyDivision.Organization);

        Assert.True(enemyDivision.IsBroken);

        Assert.False(
            s.Orchestrator.HasCombatCapableOpposition());

        // Le moteur doit cloturer l'engagement sans
        // generer de nouvel ordre de renforcement.
        s.Engine.Tick();

        Assert.False(s.Battle.Engagement.IsActive);
        Assert.Empty(s.Movements.PendingMovements);
    }



    [Fact]
    public void CommandPictureJoinsOwnDefenseWithObservedContacts()
    {
        var s = CreateScenario();

        s.Signatures.Set(
            s.Enemy,
            new TacticalUnitSignature(
                TacticalActivity.Stationary,
                TacticalConcealment.None));


        s.Engine.Tick(); // H2

        var picture = Capture9l(
            s,
            s.FrontSector,
            s.ReserveSector);

        Assert.Equal(2, picture.Hour);
        Assert.Equal(2, picture.Sectors.Count);

        var front = picture.Sectors[0];
        var reserve = picture.Sectors[1];

        Assert.Equal(s.FrontSector.Id, front.SectorId);
        Assert.True(front.IsPrioritySector);

        Assert.Equal(
            TacticalBattalionSectorStatus.UnableToHold,
            front.DefenseStatus);

        Assert.Equal(20, front.AvailableFrontLinePersonnel);

        Assert.True(front.CurrentLocalizedReports > 0);

        Assert.Equal(s.ReserveSector.Id, reserve.SectorId);

        Assert.Equal(
            TacticalBattalionSectorStatus.Uncovered,
            reserve.DefenseStatus);

        Assert.Equal(0, reserve.AvailableFrontLinePersonnel);
    }

    [Fact]
    public void SectorWithoutKnownEnemyIsNotMarkedSafe()
    {
        var s = CreateScenario(enemyInRear: true);

        s.Engine.Tick();

        var picture = Capture9l(
            s,
            s.FrontSector,
            s.ReserveSector);

        var reserve = picture.Sectors[1];

        Assert.Equal(
            TacticalBattalionSectorStatus.Uncovered,
            reserve.DefenseStatus);

        Assert.Equal(0, reserve.CurrentLocalizedReports);
        Assert.Equal(0, reserve.CurrentSuspectedReports);

        // Aucune propriete IsSafe : zero rapport
        // signifie absence de connaissance, pas
        // absence physique d'ennemis.
    }

    [Fact]
    public void ContactsOutsideResponsibilityArePreserved()
    {
        var s = CreateScenario();

        s.Signatures.Set(
            s.Enemy,
            new TacticalUnitSignature(
                TacticalActivity.Stationary,
                TacticalConcealment.None));

        s.Engine.Tick();

        // Le front n'est pas dans la selection.
        var picture = Capture9l(s, s.ReserveSector);

        Assert.Single(picture.Sectors);

        Assert.True(picture.OutsideLocalizedReports > 0);

        Assert.Equal(
            0,
            picture.Sectors[0].CurrentLocalizedReports);
    }

    [Fact]
    public void CommandPictureContainsNoLiveMilitaryReferences()
    {
        var s = CreateScenario();

        s.Engine.Tick();

        var picture = Capture9l(s, s.FrontSector);

        Assert.Single(picture.Sectors);

        var safeSnapshotTypes = new[]
        {
            typeof(int),
            typeof(long),
            typeof(string),
            typeof(IReadOnlyList<
                TacticalBattalionCommandSectorReport>)
        };

        foreach (var property in
            typeof(TacticalBattalionCommandSituationSnapshot)
                .GetProperties())
        {
            Assert.Contains(
                property.PropertyType,
                safeSnapshotTypes);
        }

        var safeSectorTypes = new[]
        {
            typeof(int),
            typeof(string),
            typeof(bool),
            typeof(double),
            typeof(TacticalBattalionSectorStatus)
        };

        foreach (var property in
            typeof(TacticalBattalionCommandSectorReport)
                .GetProperties())
        {
            Assert.Contains(
                property.PropertyType,
                safeSectorTypes);
        }
    }

    [Fact]
    public void CommandPictureRemainsFrozenAfterNextHour()
    {
        var s = CreateScenario();

        s.Engine.Tick(); // H2

        var h2 = Capture9l(s, s.FrontSector);

        int originalPersonnel =
            h2.Sectors[0].AvailableFrontLinePersonnel;

        s.Engine.Tick(); // H3

        Assert.Equal(2, h2.Hour);

        Assert.Equal(
            originalPersonnel,
            h2.Sectors[0].AvailableFrontLinePersonnel);

        var h3 = Capture9l(s, s.FrontSector);

        Assert.Equal(3, h3.Hour);

        Assert.NotSame(h2, h3);
    }

    [Fact]
    public void CommandPictureRejectsAfterAutomaticApproval()
    {
        var s = CreateScenario(
            enableAutomaticReinforcements: true);

        s.Engine.Tick(); // H2
        s.Engine.Tick(); // H3

        Assert.True(s.Reserve.IsInTransit);

        Assert.NotEqual(
            s.Battle.DirectFire.LastSuppressionPhase!
                .DeploymentRevision,
            s.Battle.DeploymentRevision);

        Assert.Throws<InvalidOperationException>(() =>
            Capture9l(s, s.FrontSector));
    }

    [Fact]
    public void CommandPictureRejectsInvalidPhaseOrResponsibility()
    {
        var s = CreateScenario();

        // Pas encore d'heure tactique terminee.
        Assert.Throws<InvalidOperationException>(() =>
            Capture9l(s, s.FrontSector));

        s.Engine.Tick();

        Assert.Throws<ArgumentException>(() =>
            Capture9l(
                s,
                s.FrontSector,
                s.FrontSector));

        var foreign = CreateScenario();

        Assert.Throws<ArgumentException>(() =>
            Capture9l(s, foreign.FrontSector));

        Assert.Throws<InvalidOperationException>(() =>
            new TacticalBattalionCommandSituationSystem(
                s.Orchestrator).Capture(
                    foreign.Battalion,
                    new[] { s.FrontSector },
                    maxAgeHours: 2));
    }

    [Fact]
    public void CommandPictureDoesNotModifyMilitaryState()
    {
        var s = CreateScenario();

        s.Engine.Tick();

        var resources =
            s.Battalion.ParentDivision!
                .GetCurrentResourceTotals();

        double cohesion = s.Front.Company.Cohesion;
        long revision = s.Battle.DeploymentRevision;

        var suppression =
            s.Battle.DirectFire.LastSuppressionPhase;

        var first = Capture9l(
            s,
            s.FrontSector,
            s.ReserveSector);

        var second = Capture9l(
            s,
            s.FrontSector,
            s.ReserveSector);

        Assert.Equal(
            first.Sectors.ToArray(),
            second.Sectors.ToArray());

        Assert.Equal(
            resources,
            s.Battalion.ParentDivision!
                .GetCurrentResourceTotals());

        Assert.Equal(cohesion, s.Front.Company.Cohesion, 6);
        Assert.Equal(revision, s.Battle.DeploymentRevision);

        Assert.Same(
            suppression,
            s.Battle.DirectFire.LastSuppressionPhase);

        Assert.Empty(s.Movements.PendingMovements);
    }


    private static void Register9m(
        Scenario s,
        params BattleSector[] sectors)
    {
        s.Orchestrator.RegisterCommandSituation(
            s.Battalion,
            sectors,
            maxAgeHours: 2);
    }

    [Fact]
    public void CommandPublicationIsDisabledWithoutRegistration()
    {
        var s = CreateScenario(
            enableAutomaticReinforcements: true);

        s.Engine.Tick(); // H2
        s.Engine.Tick(); // H3

        Assert.Null(
            s.Orchestrator.LastCommandSituationPhase);

        // L'automatisation des renforts existante
        // continue de fonctionner sans 9m.
        Assert.True(s.Reserve.IsInTransit);
    }

    [Fact]
    public void PublishedSituationMatchesImmediateCommandCapture()
    {
        var s = CreateScenario();

        Register9m(s, s.FrontSector, s.ReserveSector);

        s.Signatures.Set(
            s.Enemy,
            new TacticalUnitSignature(
                TacticalActivity.Stationary,
                TacticalConcealment.None));

        s.Engine.Tick(); // H2

        var phase = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        Assert.Equal(2, phase.Hour);
        Assert.Empty(phase.Failures);

        var published = Assert.Single(phase.Snapshots);

        var direct = Capture9l(
            s,
            s.FrontSector,
            s.ReserveSector);

        Assert.Equal(
            direct.Sectors.ToArray(),
            published.Sectors.ToArray());

        Assert.Equal(
            direct.DeploymentRevision,
            published.DeploymentRevision);

        Assert.True(
            published.Sectors[0].CurrentLocalizedReports > 0);
    }

    [Fact]
    public void CommandSituationIsPublishedBeforeAutomaticApproval()
    {
        var s = CreateScenario(
            enableAutomaticReinforcements: true);

        Register9m(s, s.FrontSector, s.ReserveSector);

        s.Engine.Tick(); // H2
        s.Engine.Tick(); // H3 : approbation.

        var phase = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        Assert.Equal(3, phase.Hour);
        Assert.Empty(phase.Failures);

        var published = Assert.Single(phase.Snapshots);

        Assert.Equal(3, published.Hour);

        // La reserve est maintenant en transit.
        Assert.True(s.Reserve.IsInTransit);

        // Le rapport a ete capture AVANT
        // le changement de revision.
        Assert.NotEqual(
            published.DeploymentRevision,
            s.Battle.DeploymentRevision);

        // Une nouvelle capture directe de H3
        // reste interdite par 9f.1.
        Assert.Throws<InvalidOperationException>(() =>
            Capture9l(s, s.FrontSector));

        // La photographie publiee, elle,
        // demeure accessible.
        Assert.Equal(2, published.Sectors.Count);
    }

    [Fact]
    public void PublishedSituationRemainsFrozenAfterNextHour()
    {
        var s = CreateScenario();

        Register9m(s, s.FrontSector);

        s.Engine.Tick(); // H2

        var h2 = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        var original = Assert.Single(h2.Snapshots);

        int originalPersonnel =
            original.Sectors[0].AvailableFrontLinePersonnel;

        s.Engine.Tick(); // H3

        var h3 = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        Assert.NotSame(h2, h3);
        Assert.Equal(2, h2.Hour);
        Assert.Equal(3, h3.Hour);

        Assert.Same(original, h2.Snapshots[0]);

        Assert.Equal(
            originalPersonnel,
            original.Sectors[0].AvailableFrontLinePersonnel);
    }

    [Fact]
    public void DifferentDivisionsWithSameBattalionIdArePublished()
    {
        var s = CreateScenario();

        Register9m(s, s.FrontSector);

        var enemyBattalion =
            s.Enemy.Company.ParentBattalion!;

        Assert.Equal(s.Battalion.Id, enemyBattalion.Id);

        s.Orchestrator.RegisterCommandSituation(
            enemyBattalion,
            new[] { s.FrontSector },
            maxAgeHours: 2);

        s.Engine.Tick();

        var phase = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        Assert.Empty(phase.Failures);
        Assert.Equal(2, phase.Snapshots.Count);

        Assert.Equal(
            phase.Snapshots[0].BattalionId,
            phase.Snapshots[1].BattalionId);

        Assert.NotEqual(
            phase.Snapshots[0].FriendlyDivisionName,
            phase.Snapshots[1].FriendlyDivisionName);
    }

    [Fact]
    public void CommandRegistrationRejectsInvalidFronts()
    {
        var s = CreateScenario();

        Assert.Throws<ArgumentException>(() =>
            Register9m(
                s,
                s.FrontSector,
                s.FrontSector));

        Assert.Throws<ArgumentException>(() =>
            Register9m(s));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            s.Orchestrator.RegisterCommandSituation(
                s.Battalion,
                new[] { s.FrontSector },
                maxAgeHours: -1));

        var foreign = CreateScenario();

        Assert.Throws<ArgumentException>(() =>
            Register9m(s, foreign.FrontSector));

        Assert.Throws<InvalidOperationException>(() =>
            s.Orchestrator.RegisterCommandSituation(
                foreign.Battalion,
                new[] { s.FrontSector }));

        Register9m(s, s.FrontSector);

        Assert.Throws<InvalidOperationException>(() =>
            Register9m(s, s.ReserveSector));
    }

    [Fact]
    public void CommandRegistrationCopiesResponsibilitySectors()
    {
        var s = CreateScenario();

        var configured = new[]
        {
            s.FrontSector,
            s.ReserveSector
        };

        s.Orchestrator.RegisterCommandSituation(
            s.Battalion,
            configured);

        // Modifier le tableau d'origine apres
        // l'enregistrement.
        configured[0] = s.RearSector;

        s.Engine.Tick();

        var phase = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        var picture = Assert.Single(phase.Snapshots);

        Assert.Empty(phase.Failures);
        Assert.Equal(s.FrontSector.Id,
            picture.Sectors[0].SectorId);

        Assert.Equal(s.ReserveSector.Id,
            picture.Sectors[1].SectorId);

        Assert.Throws<InvalidOperationException>(() =>
            Register9m(s, s.RearSector));
    }

    [Fact]
    public void PublishingCommandSituationDoesNotChangeCombat()
    {
        var normal = CreateScenario();
        var published = CreateScenario();

        Register9m(
            published,
            published.FrontSector,
            published.ReserveSector);

        normal.Engine.Tick();
        published.Engine.Tick();

        Assert.Equal(
            normal.Orchestrator.LastResult,
            published.Orchestrator.LastResult);

        Assert.Equal(
            normal.Battalion.ParentDivision!
                .GetCurrentResourceTotals(),
            published.Battalion.ParentDivision!
                .GetCurrentResourceTotals());

        Assert.Equal(
            normal.Front.Company.Cohesion,
            published.Front.Company.Cohesion,
            6);

        Assert.Equal(
            normal.Battle.DeploymentRevision,
            published.Battle.DeploymentRevision);

        Assert.Empty(
            published.Orchestrator
                .LastCommandSituationPhase!.Failures);
    }



    [Fact]
    public void UnexpectedCommandCaptureFailureDoesNotBlockTick()
    {
        Func<
            Battalion,
            IReadOnlyList<BattleSector>,
            long,
            TacticalBattalionCommandSituationSnapshot>
                failingCapture =
                    (_, _, _) => throw new NotSupportedException(
                        "Injected command capture failure.");

        var baseline = CreateScenario(
            killedProbability: 1,
            enableAutomaticReinforcements: true);

        var failing = CreateScenario(
            killedProbability: 1,
            enableAutomaticReinforcements: true,
            commandSituationCaptureOverride: failingCapture);

        Register9m(
            failing,
            failing.FrontSector,
            failing.ReserveSector);

        // Rendre l'adversaire observable pour
        // produire de vrais tirs et de vraies pertes.
        foreach (var s in new[] { baseline, failing })
        {
            s.Signatures.Set(
                s.Enemy,
                new TacticalUnitSignature(
                    TacticalActivity.Stationary,
                    TacticalConcealment.None));
        }

        // H2 puis H3 pour les deux batailles.
        for (int i = 0; i < 2; i++)
        {
            baseline.Engine.Tick();
            failing.Engine.Tick();
        }

        Assert.Equal(3, failing.Engine.Clock.CurrentHour);

        var result = failing.Orchestrator.LastResult!.Value;

        Assert.Equal(3, result.Hour);
        Assert.True(result.Killed > 0);

        Assert.Equal(
            baseline.Orchestrator.LastResult,
            failing.Orchestrator.LastResult);

        Assert.Equal(
            baseline.Battalion.ParentDivision!
                .GetCurrentResourceTotals(),
            failing.Battalion.ParentDivision!
                .GetCurrentResourceTotals());

        Assert.Equal(
            baseline.Enemy.Company.Composition!
                .Manpower.Current,
            failing.Enemy.Company.Composition!
                .Manpower.Current);

        Assert.Equal(
            3,
            failing.Battle.DirectFire
                .LastCasualtyPhase!.Hour);

        // H3 doit avoir publie un echec explicite.
        var phase = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                failing.Orchestrator.LastCommandSituationPhase);

        Assert.Equal(3, phase.Hour);
        Assert.Empty(phase.Snapshots);

        var failure = Assert.Single(phase.Failures);

        Assert.Contains(
            "NotSupportedException",
            failure.Reason);

        // L'erreur facultative ne doit pas bloquer
        // le commandement automatique existant.
        Assert.True(failing.Reserve.IsInTransit);

        // La simulation doit pouvoir passer a H4.
        baseline.Engine.Tick();
        failing.Engine.Tick();

        Assert.Equal(4, failing.Engine.Clock.CurrentHour);

        Assert.Equal(
            baseline.Orchestrator.LastResult,
            failing.Orchestrator.LastResult);
    }


    [Fact]
    public void OneUnexpectedFailureDoesNotBlockOtherBattalionPictures()
    {
        Scenario? scenario = null;
        int captures = 0;

        Func<
            Battalion,
            IReadOnlyList<BattleSector>,
            long,
            TacticalBattalionCommandSituationSnapshot>
                capture = (battalion, sectors, maxAge) =>
                {
                    captures++;

                    if (captures == 1)
                    {
                        throw new NotSupportedException(
                            "First battalion capture failed.");
                    }

                    return new TacticalBattalionCommandSituationSystem(
                        scenario!.Orchestrator).Capture(
                            battalion,
                            sectors,
                            maxAge);
                };

        scenario = CreateScenario(
            commandSituationCaptureOverride: capture);

        var s = scenario;

        Register9m(s, s.FrontSector);

        var enemyBattalion =
            s.Enemy.Company.ParentBattalion!;

        s.Orchestrator.RegisterCommandSituation(
            enemyBattalion,
            new[] { s.FrontSector },
            maxAgeHours: 2);

        s.Engine.Tick(); // H2

        Assert.Equal(2, s.Engine.Clock.CurrentHour);
        Assert.Equal(2, captures);

        var phase = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        Assert.Equal(2, phase.Hour);

        Assert.Single(phase.Failures);
        Assert.Single(phase.Snapshots);

        Assert.Contains(
            "NotSupportedException",
            phase.Failures[0].Reason);

        Assert.Equal(
            enemyBattalion.ParentDivision!.Name,
            phase.Snapshots[0].FriendlyDivisionName);
    }


    [Fact]
    public void ScopedCommandReadSeparatesEqualLocalBattalionIds()
    {
        var s = CreateScenario();

        var friendlyKey =
            s.Orchestrator.RegisterCommandSituation(
                s.Battalion,
                new[] { s.FrontSector });

        var enemyBattalion =
            s.Enemy.Company.ParentBattalion!;

        var enemyKey =
            s.Orchestrator.RegisterCommandSituation(
                enemyBattalion,
                new[] { s.FrontSector });

        Assert.Equal(s.Battalion.Id, enemyBattalion.Id);

        s.Engine.Tick(); // H2

        var friendly = s.Orchestrator.ReadCommandSituation(
            friendlyKey, 2);

        var enemy = s.Orchestrator.ReadCommandSituation(
            enemyKey, 2);

        Assert.NotNull(friendly);
        Assert.NotNull(enemy);

        Assert.NotNull(friendly!.Snapshot);
        Assert.NotNull(enemy!.Snapshot);

        Assert.Null(friendly.Failure);
        Assert.Null(enemy.Failure);

        Assert.Equal(
            s.Battalion.ParentDivision!.Name,
            friendly.Snapshot.FriendlyDivisionName);

        Assert.Equal(
            enemyBattalion.ParentDivision!.Name,
            enemy.Snapshot.FriendlyDivisionName);

        Assert.NotEqual(
            friendly.Snapshot.FriendlyDivisionName,
            enemy.Snapshot.FriendlyDivisionName);
    }

    [Fact]
    public void ScopedCommandReadRejectsForeignRegistration()
    {
        var first = CreateScenario();
        var second = CreateScenario();

        var foreignKey =
            second.Orchestrator.RegisterCommandSituation(
                second.Battalion,
                new[] { second.FrontSector });

        Assert.Throws<InvalidOperationException>(() =>
            first.Orchestrator.ReadCommandSituation(
                foreignKey, 2));

        var unknownKey =
            new TacticalCommandSituationRegistration();

        Assert.Throws<InvalidOperationException>(() =>
            second.Orchestrator.ReadCommandSituation(
                unknownKey, 2));
    }

    [Fact]
    public void ScopedCommandReadRequiresCurrentHourAndActiveBattle()
    {
        var s = CreateScenario();

        var key = s.Orchestrator.RegisterCommandSituation(
            s.Battalion,
            new[] { s.FrontSector });

        // Aucune publication avant H2.
        Assert.Null(
            s.Orchestrator.ReadCommandSituation(key, 2));

        s.Engine.Tick(); // H2

        Assert.NotNull(
            s.Orchestrator.ReadCommandSituation(key, 2));

        Assert.Null(
            s.Orchestrator.ReadCommandSituation(key, 1));

        Assert.Null(
            s.Orchestrator.ReadCommandSituation(key, 3));

        s.Engine.Tick(); // H3

        // La publication H2 n'est plus la derniere.
        Assert.Null(
            s.Orchestrator.ReadCommandSituation(key, 2));

        Assert.NotNull(
            s.Orchestrator.ReadCommandSituation(key, 3));

        // L'opposition cesse d'etre capable de combattre.
        var enemyDivision =
            s.Enemy.Company.ParentBattalion!
                .ParentDivision!;

        enemyDivision.LoseOrganization(
            enemyDivision.Organization);

        s.Engine.Tick(); // Cloture.

        Assert.False(s.Battle.Engagement.IsActive);

        // Meme si la derniere phase demeure en memoire,
        // elle n'est plus livree au commandement.
        Assert.Null(
            s.Orchestrator.ReadCommandSituation(key, 3));
    }

    [Fact]
    public void ScopedCommandReadDefersOnCaptureFailure()
    {
        var s = CreateScenario(
            commandSituationCaptureOverride:
                (_, _, _) => throw new NotSupportedException(
                    "Injected capture failure."));

        var key = s.Orchestrator.RegisterCommandSituation(
            s.Battalion,
            new[] { s.FrontSector });

        s.Engine.Tick(); // H2

        Assert.Equal(2, s.Engine.Clock.CurrentHour);

        var result =
            s.Orchestrator.ReadCommandSituation(key, 2);

        Assert.NotNull(result);
        Assert.Null(result!.Snapshot);
        Assert.NotNull(result.Failure);

        Assert.Contains(
            "NotSupportedException",
            result.Failure!.Reason);

        // L'echec appartient a ce bataillon.
        Assert.Equal(
            s.Battalion.Id,
            result.Failure.BattalionId);

        // Aucun rapport alternatif ne doit etre
        // reconstitue automatiquement pour decider.
    }

    [Fact]
    public void ScopedCommandReadSurvivesAutomaticApproval()
    {
        var s = CreateScenario(
            enableAutomaticReinforcements: true);

        var key = s.Orchestrator.RegisterCommandSituation(
            s.Battalion,
            new[]
            {
                s.FrontSector,
                s.ReserveSector
            });

        s.Engine.Tick(); // H2
        s.Engine.Tick(); // H3 : approbation.

        Assert.True(s.Reserve.IsInTransit);

        var result =
            s.Orchestrator.ReadCommandSituation(key, 3);

        Assert.NotNull(result);
        Assert.Null(result!.Failure);

        var snapshot = Assert.IsType<
            TacticalBattalionCommandSituationSnapshot>(
                result.Snapshot);

        Assert.Equal(3, snapshot.Hour);
        Assert.Equal(2, snapshot.Sectors.Count);

        // L'ordre a modifie le deploiement APRES
        // la photographie de commandement.
        Assert.NotEqual(
            snapshot.DeploymentRevision,
            s.Battle.DeploymentRevision);

        // Le lecteur cloisonne conserve le rapport H3.
        var again =
            s.Orchestrator.ReadCommandSituation(key, 3);

        Assert.Same(snapshot, again!.Snapshot);
    }



    [Fact]
    public void ArbitrationRecommendsClearlyDominantSector()
    {
        var (s, reader) = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.UnableToHold,
                localized: 1),
            Sector9n(
                2,
                TacticalBattalionSectorStatus.AtRisk,
                suspected: 1));

        s.Engine.Tick();

        var result =
            new TacticalBattalionSectorArbitrationPolicy(reader)
                .Evaluate(2);

        Assert.Equal(
            TacticalSectorArbitrationStatus.Recommended,
            result.Status);

        Assert.Equal(1, result.SectorId);
        Assert.Equal(
            TacticalSectorEvidence.CurrentLocalized,
            result.Evidence);
    }

    [Fact]
    public void ArbitrationUsesMissionPriorityAsTieBreaker()
    {
        var (s, reader) = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.AtRisk,
                suspected: 1),
            Sector9n(
                2,
                TacticalBattalionSectorStatus.AtRisk,
                priority: true,
                suspected: 1));

        s.Engine.Tick();

        var result =
            new TacticalBattalionSectorArbitrationPolicy(reader)
                .Evaluate(2);

        Assert.Equal(
            TacticalSectorArbitrationStatus.Recommended,
            result.Status);

        Assert.Equal(2, result.SectorId);
    }

    [Fact]
    public void ArbitrationDoesNotTreatReportCountAsEnemyStrength()
    {
        var (s, reader) = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.AtRisk,
                localized: 1),
            Sector9n(
                2,
                TacticalBattalionSectorStatus.AtRisk,
                localized: 7));

        s.Engine.Tick();

        var result =
            new TacticalBattalionSectorArbitrationPolicy(reader)
                .Evaluate(2);

        // Sept rapports ne prouvent pas la presence
        // de sept compagnies ennemies distinctes.
        Assert.Equal(
            TacticalSectorArbitrationStatus.DeferredAmbiguous,
            result.Status);

        Assert.Null(result.SectorId);
        Assert.Equal(
            new[] { 1, 2 },
            result.CandidateSectorIds);
    }

    [Fact]
    public void ArbitrationDefersConflictingEvidenceAndWeakness()
    {
        var (s, reader) = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.UnableToHold,
                stale: 1),
            Sector9n(
                2,
                TacticalBattalionSectorStatus.AtRisk,
                localized: 1));

        s.Engine.Tick();

        var result =
            new TacticalBattalionSectorArbitrationPolicy(reader)
                .Evaluate(2);

        // A est plus faible, mais B dispose
        // du renseignement le plus actuel.
        Assert.Equal(
            TacticalSectorArbitrationStatus.DeferredAmbiguous,
            result.Status);

        Assert.Null(result.SectorId);
    }

    [Fact]
    public void ArbitrationDefersWhenOnlyHistoricalContactExists()
    {
        var (s, reader) = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.UnableToHold,
                stale: 2),
            Sector9n(
                2,
                TacticalBattalionSectorStatus.Capable));

        s.Engine.Tick();

        var result =
            new TacticalBattalionSectorArbitrationPolicy(reader)
                .Evaluate(2);

        Assert.Equal(
            TacticalSectorArbitrationStatus
                .DeferredNoCurrentContact,
            result.Status);

        Assert.Null(result.SectorId);
    }

    [Fact]
    public void ArbitrationDoesNotReinforceCapableSectors()
    {
        var (s, reader) = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.Capable,
                localized: 2),
            Sector9n(
                2,
                TacticalBattalionSectorStatus.Capable,
                suspected: 1));

        s.Engine.Tick();

        var result =
            new TacticalBattalionSectorArbitrationPolicy(reader)
                .Evaluate(2);

        Assert.Equal(
            TacticalSectorArbitrationStatus.NoWeakSector,
            result.Status);

        Assert.Null(result.SectorId);
    }

    [Fact]
    public void ArbitrationDefersMissingOrFailedPublication()
    {
        var (s, reader) = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.UnableToHold,
                localized: 1));

        var policy =
            new TacticalBattalionSectorArbitrationPolicy(reader);

        Assert.Equal(
            TacticalSectorArbitrationStatus
                .DeferredNoPublication,
            policy.Evaluate(2).Status);

        var failing = CreateScenario(
            commandSituationCaptureOverride:
                (_, _, _) => throw new NotSupportedException(
                    "Injected 9n failure."));

        var key = failing.Orchestrator.RegisterCommandSituation(
            failing.Battalion,
            new[] { failing.FrontSector });

        var failedReader =
            failing.Orchestrator.BindCommandSituationReader(key);

        failing.Engine.Tick();

        var failedResult =
            new TacticalBattalionSectorArbitrationPolicy(
                failedReader).Evaluate(2);

        Assert.Equal(
            TacticalSectorArbitrationStatus
                .DeferredCaptureFailure,
            failedResult.Status);

        Assert.Null(failedResult.SectorId);
    }

    [Fact]
    public void ArbitrationRejectsMalformedPublishedSectorData()
    {
        var (s, reader) = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.UnableToHold,
                localized: -1),
            Sector9n(
                2,
                TacticalBattalionSectorStatus.AtRisk,
                suspected: 1));

        s.Engine.Tick();

        var result =
            new TacticalBattalionSectorArbitrationPolicy(reader)
                .Evaluate(2);

        Assert.Equal(
            TacticalSectorArbitrationStatus.DeferredCaptureFailure,
            result.Status);

        Assert.Null(result.SectorId);
    }

    [Fact]
    public void ArbitrationDoesNotModifyCombatOrIssueOrders()
    {
        var (s, reader) = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.UnableToHold,
                localized: 1),
            Sector9n(
                2,
                TacticalBattalionSectorStatus.AtRisk,
                suspected: 1));

        s.Engine.Tick();

        var resourcesBefore =
            s.Battalion.ParentDivision!
                .GetCurrentResourceTotals();

        var combatBefore = s.Orchestrator.LastResult;
        long revisionBefore = s.Battle.DeploymentRevision;
        double cohesionBefore = s.Front.Company.Cohesion;

        var publishedBefore = reader.Read(2)!.Snapshot;

        var policy =
            new TacticalBattalionSectorArbitrationPolicy(reader);

        var first = policy.Evaluate(2);
        var second = policy.Evaluate(2);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.SectorId, second.SectorId);

        Assert.Same(
            publishedBefore,
            reader.Read(2)!.Snapshot);

        Assert.Equal(
            resourcesBefore,
            s.Battalion.ParentDivision!
                .GetCurrentResourceTotals());

        Assert.Equal(
            combatBefore,
            s.Orchestrator.LastResult);

        Assert.Equal(
            revisionBefore,
            s.Battle.DeploymentRevision);

        Assert.Equal(
            cohesionBefore,
            s.Front.Company.Cohesion);

        Assert.Empty(s.Movements.PendingMovements);
    }


    [Fact]
    public void PublicationRejectsSectorOutsideRegisteredFront()
    {
        var (s, reader) = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.UnableToHold,
                localized: 1),
            Sector9n(
                3,
                TacticalBattalionSectorStatus.AtRisk,
                suspected: 1));

        // Le perimetre enregistre contient 1 et 2,
        // mais la capture injectee contient 1 et 3.
        s.Engine.Tick();

        var phase = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        Assert.Empty(phase.Snapshots);
        Assert.Single(phase.Failures);

        var decision =
            new TacticalBattalionSectorArbitrationPolicy(
                reader).Evaluate(2);

        Assert.Equal(
            TacticalSectorArbitrationStatus.DeferredCaptureFailure,
            decision.Status);

        Assert.Null(decision.SectorId);
    }

    [Fact]
    public void PublicationRejectsAtRiskWithoutFrontLine()
    {
        var impossible = Sector9n(
            1,
            TacticalBattalionSectorStatus.AtRisk,
            localized: 1) with
        {
            StationaryFrontLineCompanies = 0,
            AuthorizedFrontLinePersonnel = 0,
            AvailableFrontLinePersonnel = 0,
            FrontLineReadiness = 0
        };

        var (s, reader) = Create9nScenario(
            impossible,
            Sector9n(
                2,
                TacticalBattalionSectorStatus.Capable));

        s.Engine.Tick();

        var phase = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        Assert.Empty(phase.Snapshots);
        Assert.Single(phase.Failures);

        Assert.Equal(
            TacticalSectorArbitrationStatus.DeferredCaptureFailure,
            new TacticalBattalionSectorArbitrationPolicy(
                reader).Evaluate(2).Status);
    }

    [Fact]
    public void PublicationRejectsMissingAndDuplicatedSectors()
    {
        var missing = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.UnableToHold,
                localized: 1));

        var duplicated = Create9nScenario(
            Sector9n(
                1,
                TacticalBattalionSectorStatus.UnableToHold,
                localized: 1),
            Sector9n(
                1,
                TacticalBattalionSectorStatus.AtRisk,
                suspected: 1));

        foreach (var (s, reader) in new[]
        {
            missing,
            duplicated
        })
        {
            s.Engine.Tick();

            var phase = Assert.IsType<
                TacticalCommandSituationPhaseResult>(
                    s.Orchestrator.LastCommandSituationPhase);

            Assert.Empty(phase.Snapshots);
            Assert.Single(phase.Failures);

            Assert.Equal(
                TacticalSectorArbitrationStatus
                    .DeferredCaptureFailure,
                new TacticalBattalionSectorArbitrationPolicy(
                    reader).Evaluate(2).Status);
        }
    }

    [Fact]
    public void PublishedSituationIsIndependentOfInjectedMutableArray()
    {
        var mutableSectors = new[]
        {
            Sector9n(
                1,
                TacticalBattalionSectorStatus.UnableToHold,
                localized: 1),
            Sector9n(
                2,
                TacticalBattalionSectorStatus.Capable)
        };

        Scenario? linked = null;

        var s = CreateScenario(
            commandSituationCaptureOverride:
                (battalion, _, _) =>
                    new TacticalBattalionCommandSituationSnapshot(
                        BattalionId: battalion.Id,
                        BattalionName: battalion.Name,
                        FriendlyDivisionName:
                            battalion.ParentDivision!.Name,
                        Hour:
                            linked!.Orchestrator.LastResult!.Value.Hour,
                        DeploymentRevision:
                            linked.Battle.DeploymentRevision,
                        Sectors: mutableSectors,
                        OutsideLocalizedReports: 0,
                        OutsideSuspectedReports: 0,
                        OutsideStaleReports: 0));

        linked = s;

        var key = s.Orchestrator.RegisterCommandSituation(
            s.Battalion,
            new[]
            {
                s.FrontSector,
                s.ReserveSector
            });

        var reader =
            s.Orchestrator.BindCommandSituationReader(key);

        s.Engine.Tick();

        var policy =
            new TacticalBattalionSectorArbitrationPolicy(
                reader);

        var first = policy.Evaluate(2);

        Assert.Equal(
            TacticalSectorArbitrationStatus.Recommended,
            first.Status);

        Assert.Equal(1, first.SectorId);

        var published = reader.Read(2)!.Snapshot!;

        // Modifier le tableau d'origine APRES
        // sa publication : inverser la situation.
        mutableSectors[0] = Sector9n(
            1,
            TacticalBattalionSectorStatus.Capable);

        mutableSectors[1] = Sector9n(
            2,
            TacticalBattalionSectorStatus.UnableToHold,
            localized: 1);

        var second = policy.Evaluate(2);

        // Sans copie, la recommandation pourrait
        // changer pour la meme heure tactique.
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.SectorId, second.SectorId);

        Assert.Equal(
            TacticalBattalionSectorStatus.UnableToHold,
            published.Sectors[0].DefenseStatus);

        Assert.Equal(
            TacticalBattalionSectorStatus.Capable,
            published.Sectors[1].DefenseStatus);

        Assert.NotSame(
            mutableSectors,
            published.Sectors);

        Assert.Same(
            published,
            reader.Read(2)!.Snapshot);
    }




    [Fact]
    public void PublicationRejectsPositiveReadinessWithNoPersonnel()
    {
        var invalid = Sector9n(
            1,
            TacticalBattalionSectorStatus.UnableToHold,
            localized: 1) with
        {
            AvailableFrontLinePersonnel = 0,
            FrontLineReadiness = 15
        };

        var (s, reader) = Create9nScenario(
            invalid,
            Sector9n(
                2,
                TacticalBattalionSectorStatus.Capable));

        s.Engine.Tick();

        var phase = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        Assert.Empty(phase.Snapshots);
        Assert.Single(phase.Failures);

        var decision =
            new TacticalBattalionSectorArbitrationPolicy(
                reader).Evaluate(2);

        Assert.Equal(
            TacticalSectorArbitrationStatus.DeferredCaptureFailure,
            decision.Status);
    }



    [Fact]
    public void PublicationAcceptsAtRiskWithZeroReadinessAtZeroThreshold()
    {
        Scenario? linked = null;

        var s = CreateScenario(
            riskThreshold: 0,
            commandSituationCaptureOverride:
                (battalion, _, _) =>
                    new TacticalBattalionCommandSituationSnapshot(
                        BattalionId: battalion.Id,
                        BattalionName: battalion.Name,
                        FriendlyDivisionName:
                            battalion.ParentDivision!.Name,
                        Hour:
                            linked!.Orchestrator.LastResult!.Value.Hour,
                        DeploymentRevision:
                            linked.Battle.DeploymentRevision,
                        Sectors: Array.AsReadOnly(new[]
                        {
                            Sector9n(
                                1,
                                TacticalBattalionSectorStatus.AtRisk,
                                localized: 1) with
                            {
                                FrontLineReadiness = 0
                            },
                            Sector9n(
                                2,
                                TacticalBattalionSectorStatus.Capable)
                        }),
                        OutsideLocalizedReports: 0,
                        OutsideSuspectedReports: 0,
                        OutsideStaleReports: 0));

        linked = s;

        s.Orchestrator.RegisterCommandSituation(
            s.Battalion,
            new[]
            {
                s.FrontSector,
                s.ReserveSector
            });

        s.Engine.Tick();

        var phase = Assert.IsType<
            TacticalCommandSituationPhaseResult>(
                s.Orchestrator.LastCommandSituationPhase);

        Assert.Empty(phase.Failures);

        var snapshot = Assert.Single(phase.Snapshots);

        Assert.Equal(
            TacticalBattalionSectorStatus.AtRisk,
            snapshot.Sectors[0].DefenseStatus);

        Assert.Equal(
            0,
            snapshot.Sectors[0].FrontLineReadiness);
    }


}

