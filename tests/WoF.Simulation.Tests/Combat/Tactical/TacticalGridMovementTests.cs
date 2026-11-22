using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;
using static WoF.Simulation.Tests.Combat.Tactical.TacticalGridTestFactory;

namespace WoF.Simulation.Tests.Combat.Tactical;

/// <summary>
/// Le scénario de renfort de TacticalReactionExecutionTests, sur une grille
/// au relief irrégulier : la réserve est à 20 cases (4 000 m) à l'ouest du
/// front. Chaque position de son itinéraire est au sol interpolé.
/// </summary>
public sealed class TacticalGridMovementTests
{
    private sealed record Scenario(
        SimulationEngine Engine,
        ProvinceHexGrid Grid,
        TacticalGeometryMap Map,
        TacticalHourlyCombatOrchestrator Orchestrator,
        TacticalReactionExecutionState Movements,
        Battalion Battalion,
        CompanyDeployment Reserve,
        BattleSector FrontSector);

    private static double Relief(double x, double y) =>
        60 + 25 * Math.Sin(x / 310) + 18 * Math.Cos(y / 140);

    [Fact]
    public void Reinforcement_StaysOnTheGround_AndArrivesAtItsPoint()
    {
        var s = CreateScenario(movementSpeed: 2900, reserveOffsetY: 0);
        var request = ReachConfirmedRupture(s);

        var frontCenter = s.Map.GetSectorCenterPoint(s.FrontSector);
        var arrival = s.Map.GetGroundPoint(
            s.FrontSector, frontCenter.XMeters - 60, frontCenter.YMeters + 45);

        var order = s.Movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: s.Reserve,
            destinationPoint: arrival).Movement!;

        // Vingt liaisons de 200 m le long de la rangée 4.
        Assert.Equal(21, order.Route.Count);
        Assert.Equal(4000, order.TotalDistanceMeters, 6);

        s.Engine.Tick(); // Heure 4 : 2 900 m, au milieu de la quinzième liaison.

        var moving = s.Map.GetPosition(s.Reserve)!.Point;

        Assert.True(s.Reserve.IsInTransit);
        Assert.Same(order.Route[14], s.Reserve.Sector);
        Assert.Equal(Ground(s, moving), moving.GroundElevationMeters, 9);

        s.Engine.Tick(); // Heure 5 : arrivée au point de l'ordre.

        Assert.False(s.Reserve.IsInTransit);
        Assert.Same(s.FrontSector, s.Reserve.Sector);
        Assert.Equal(arrival, s.Map.GetPosition(s.Reserve)!.Point);
    }

    [Fact]
    public void OffCenterStart_FirstLegFollowsTheGroundRatherThanAStraightLine()
    {
        // 100 m/h, départ à 60 m au nord du centre de la case de réserve :
        // après une heure, la compagnie est au milieu de sa première liaison.
        var s = CreateScenario(movementSpeed: 100, reserveOffsetY: 60);
        var request = ReachConfirmedRupture(s);

        var start = s.Map.GetPosition(s.Reserve)!.Point;

        var order = s.Movements.Decide(
            request, TacticalReactionDecision.Approve, company: s.Reserve).Movement!;

        var next = s.Map.GetSectorCenterPoint(order.Route[1]);

        s.Engine.Tick(); // Heure 4.

        var moving = s.Map.GetPosition(s.Reserve)!.Point;

        Assert.Equal((start.XMeters + next.XMeters) / 2, moving.XMeters, 6);
        Assert.Equal((start.YMeters + next.YMeters) / 2, moving.YMeters, 6);
        Assert.Equal(Ground(s, moving), moving.GroundElevationMeters, 9);

        // L'altitude n'est pas la moyenne des deux extrémités :
        // la ligne droite passerait au-dessus ou au-dessous du sol.
        double straight = (start.GroundElevationMeters + next.GroundElevationMeters) / 2;
        Assert.True(Math.Abs(straight - moving.GroundElevationMeters) > 0.01);
    }

    private static double Ground(Scenario s, TacticalPoint point)
    {
        Assert.True(s.Grid.TryGetGroundElevation(point.XMeters, point.YMeters, out double ground));
        return ground;
    }

    private static TacticalBattalionReactionRequest ReachConfirmedRupture(Scenario s)
    {
        s.Engine.Tick(); // Heure 2 : situation menacée.
        s.Engine.Tick(); // Heure 3 : rupture confirmée.

        Assert.Equal(3, s.Engine.Clock.CurrentHour);

        return Assert.Single(
            s.Orchestrator.LastBattalionReactionPhase!.Requests,
            r => ReferenceEquals(r.Battalion, s.Battalion));
    }

    private static Scenario CreateScenario(double movementSpeed, double reserveOffsetY)
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

        var engagement = Assert.Single(engine.Engagements.ActiveEngagements);

        var grid = CreateGrid(45, 9, Relief);
        var partition = GrandSectorPartition.Build(grid);
        var layout = TacticalBattlefieldBuilder.Build(
            grid, partition, partition.Sectors.Select(sector => sector.Id));

        var battle = new TacticalBattle(engagement, layout.Battlefield);
        var map = new TacticalGeometryMap(battle);
        layout.DefineGeometry(map);

        var frontSector = layout.GetSector(Cell(grid, 30, 4));
        var reserveSector = layout.GetSector(Cell(grid, 10, 4));

        var battalion = friendly.Battalions[0];

        var front = battle.DeployCompany(
            battalion.Companies[0], frontSector, CompanyDeploymentRole.FrontLine);

        var reserve = battle.DeployCompany(
            battalion.Companies[1], reserveSector, CompanyDeploymentRole.Reserve);

        var enemyDeployment = battle.DeployCompany(
            enemy.Battalions[0].Companies[0], frontSector, CompanyDeploymentRole.FrontLine);

        var frontCenter = map.GetSectorCenterPoint(frontSector);
        var reserveCenter = map.GetSectorCenterPoint(reserveSector);

        map.PlaceCompany(front, frontCenter, facingDegrees: 90, frontageMeters: 20);

        map.PlaceCompany(
            reserve,
            map.GetGroundPoint(reserveSector, reserveCenter.XMeters, reserveCenter.YMeters + reserveOffsetY),
            facingDegrees: 90,
            frontageMeters: 20);

        map.PlaceCompany(
            enemyDeployment,
            map.GetGroundPoint(frontSector, frontCenter.XMeters + 20, frontCenter.YMeters),
            facingDegrees: 270,
            frontageMeters: 20);

        front.Company.Composition!.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(KilledInAction: 80, WoundedInAction: 0, MissingOrCaptured: 0));

        battle.PersonnelExposure.Set(front, new TacticalPersonnelExposure(
            exposed: 20, sheltered: 0, reserve: 0, exposedDepthMeters: 20));

        battle.PersonnelExposure.Set(reserve, new TacticalPersonnelExposure(
            exposed: 0, sheltered: 0, reserve: 100, exposedDepthMeters: 20));

        battle.PersonnelExposure.Set(enemyDeployment, new TacticalPersonnelExposure(
            exposed: 100, sheltered: 0, reserve: 0, exposedDepthMeters: 20));

        battle.BattalionAssessments.SetPrioritySector(battalion, frontSector);

        var settings = new TacticalHourlyCombatSettings(
            detection: new TacticalDetectionProfile(
                maxVisualRangeMeters: 500,
                maxAcousticRangeMeters: 0,
                fieldOfViewDegrees: 360,
                localizationCellMeters: 25),
            fire: new TacticalDirectFireProfile(250, fireArcDegrees: 120),
            hit: new TacticalHitProfile(2, 1, 0.5),
            defense: new TacticalDefenseProfile(1, 1, 1, 0, 0, 0),
            effects: new TacticalPersonnelEffectProfile(0, 0),
            maxWeaponsPerCompany: 10,
            roundsPerWeapon: 3,
            baseSeed: 12345,
            suppression: new TacticalSuppressionProfile(10, 2, 10, 0.8),
            cohesion: new TacticalCohesionProfile(1.5, 40, 0.1, 3, 20, 0.5),
            battalionAssessment: new TacticalBattalionAssessmentProfile(3, 2, 1, 2, 60, 30),
            battalionReactions: new TacticalBattalionReactionProfile(2, 2, 10),
            reactionMovement: new TacticalReactionMovementProfile(
                movementSpeed,
                frontLineExposureFraction: 0.25,
                transitContactDistanceMeters: 0,
                useSimultaneousContacts: false),
            enableMobileCombat: false,
            enableAutomaticReinforcements: false);

        var orchestrator = new TacticalHourlyCombatOrchestrator(
            battle,
            map,
            new TacticalPerceptionState(battle),
            new TacticalSignatureFrame(battle),
            engine.Diplomacy,
            settings);

        engine.Engagements.AttachTacticalBattle(battle);
        engine.Engagements.RegisterTacticalOrchestrator(orchestrator);

        return new Scenario(
            engine, grid, map, orchestrator, orchestrator.ReactionExecution!,
            battalion, reserve, frontSector);
    }

    private static Division MakeDivision(Country country, Province province, int companyCount)
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

        var battalion = new Battalion(1, "Infantry Battalion", BattalionType.Infantry);

        for (int i = 1; i <= companyCount; i++)
        {
            battalion.AddCompany(new Company(
                i, $"Company {i}", CompanyType.Infantry,
                new CompanyComposition(
                    manpower: 100,
                    infantryEquipment: 100,
                    ammunition: new AmmunitionTotals(smallArms: 1000))));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }
}
