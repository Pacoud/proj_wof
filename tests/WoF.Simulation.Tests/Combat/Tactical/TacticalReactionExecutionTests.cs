
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
        int companyCount)
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

        for (int i = 1; i <= companyCount; i++)
        {
            battalion.AddCompany(new Company(
                i, $"Company {i}", CompanyType.Infantry,
                new CompanyComposition(
                    manpower: 100,
                    infantryEquipment: 100,
                    ammunition: new AmmunitionTotals(
                        smallArms: 1000))));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario(
        bool hasReserve = true,
        bool enemyInRear = false,
        bool connectReserve = true)
    {
        var engine = new SimulationEngine();

        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var friendly = MakeDivision(france, province, 2);
        var enemy = MakeDivision(germany, province, 1);

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
                new TacticalReactionMovementProfile(3000));

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



    [Fact]
    public void WithdrawalIsBlockedIfEnemyOccupiesDestinationBeforeArrival()
    {
        var s = CreateScenario(hasReserve: false);

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

}
