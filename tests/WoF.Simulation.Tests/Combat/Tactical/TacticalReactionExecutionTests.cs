
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
        BattleSector RearSector);

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
        bool includeSecondEnemyBattalion = false)
    {
        var engine = new SimulationEngine();

        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var friendly = MakeDivision(france, province, 2);
        int enemyCompanyCount = 1 + (includeEnemyReserve ? 1 : 0)
            + (includeSecondEnemyBattalion ? 1 : 0);
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
            fire: new TacticalDirectFireProfile(250),
            hit: new TacticalHitProfile(2, 1, 0.5),
            defense: new TacticalDefenseProfile(
                1, 1, 1, 0, 0, 0),
            effects: new TacticalPersonnelEffectProfile(0, 0),
            maxWeaponsPerCompany: 10,
            roundsPerWeapon: 3,
            baseSeed: 12345,
            suppression: new TacticalSuppressionProfile(
                10, 2, 10, 0.8),
            cohesion: new TacticalCohesionProfile(
                1.5, 40, 0.1, 3, 20, 0.5),
            battalionAssessment:
                new TacticalBattalionAssessmentProfile(
                    3, 2, 1, 2, 60, 30),
            battalionReactions:
                new TacticalBattalionReactionProfile(
                    2, 2, 10),
            reactionMovement:
                new TacticalReactionMovementProfile(3000,
                frontLineExposureFraction: 0.25,
                transitContactDistanceMeters: transitContactDistanceMeters,
                useSimultaneousContacts: useSimultaneousContacts));

        var orchestrator = new TacticalHourlyCombatOrchestrator(
            battle,
            geometry,
            new TacticalPerceptionState(battle),
            new TacticalSignatureFrame(battle),
            engine.Diplomacy,
            settings);

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
            rearSector);
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
            s.Enemy, s.RearSector);

        s.Enemy.ApplyMovementArrival(
            s.RearSector,
            CompanyDeploymentRole.FrontLine);

        s.Battle.NotifyDeploymentChanged(s.Enemy);

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
            s.Geometry.RelocateCompany(s.Enemy, s.ReserveSector);
            s.Enemy.ApplyMovementArrival(s.ReserveSector, CompanyDeploymentRole.FrontLine);
            s.Battle.NotifyDeploymentChanged(s.Enemy);
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
            s.Enemy, s.ReserveSector);

        s.Enemy.ApplyMovementArrival(
            s.ReserveSector,
            CompanyDeploymentRole.FrontLine);

        s.Battle.NotifyDeploymentChanged(s.Enemy);

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
        s.Geometry.RelocateCompany(company, sector);
        company.ApplyMovementArrival(sector, company.Role);
        s.Battle.NotifyDeploymentChanged(company);
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

    [Fact]
    public void OrdersCrossingAtDifferentTimesContinue()
    {
        var s = CreateScenario(
            hasReserve: false, transitContactDistanceMeters: 100,
            useSimultaneousContacts: true);
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

    [Fact]
    public void InvalidPendingMovementLeavesOtherDeploymentsUntouched()
    {
        var s = CreateScenario(
            hasReserve: false, enemyInRear: true, transitContactDistanceMeters: 100,
            useSimultaneousContacts: true);
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
        long deploymentRevision = s.Battle.DeploymentRevision;
        long geometryRevision = s.Geometry.Revision;
        s.Enemy.SetInTransit(false);

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
        s.Geometry.RelocateCompany(s.Enemy, farRear);

        s.Enemy.ApplyMovementArrival(
            farRear,
            CompanyDeploymentRole.FrontLine);

        s.Battle.NotifyDeploymentChanged(s.Enemy);

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
            s.Enemy, s.RearSector);

        s.Enemy.ApplyMovementArrival(
            s.RearSector,
            CompanyDeploymentRole.FrontLine);

        s.Battle.NotifyDeploymentChanged(s.Enemy);

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

}
