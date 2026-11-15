using WoF.Simulation.Core;
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Benchmarks;

internal enum TacticalMobileBenchmarkDensity
{
    Sparse,
    Dense
}

internal sealed record TacticalMobileBenchmarkOptions(
    int TotalCompanies,
    double MobilityPercent,
    TacticalMobileBenchmarkDensity Density,
    int Seed,
    bool EnableMobileCombat = true);

internal sealed record TacticalMobileDetectorScenario(
    TacticalMobileBenchmarkOptions Options,
    TacticalBattle Battle,
    TacticalGeometryMap Geometry,
    DiplomacySystem Diplomacy,
    IReadOnlyList<TacticalTimedTrajectorySegment> Trajectories,
    int MobileCompanies,
    double ContactRadiusMeters)
{
    internal int TotalCompanies => Battle.Deployments.Count;
    internal int SectorCount => Battle.Battlefield.Sectors.Count;
    internal int ActiveMovements => 0; // Trajectoires synthétiques.
    internal int TrajectoryCount => Trajectories.Count;
}

internal sealed record TacticalMobileTickScenario(
    TacticalMobileBenchmarkOptions Options,
    SimulationEngine Engine,
    TacticalBattle Battle,
    TacticalGeometryMap Geometry,
    TacticalHourlyCombatOrchestrator Orchestrator)
{
    internal int TotalCompanies => Battle.Deployments.Count;
    internal int MobileCompanies => Battle.Deployments.Count(company => company.IsInTransit);
    internal double ActualMobilityPercent => 100.0 * MobileCompanies / TotalCompanies;
    internal int SectorCount => Battle.Battlefield.Sectors.Count;
    internal int ActiveMovements => Orchestrator.ReactionExecution!.PendingMovements.Count;
}

internal static class TacticalMobileBenchmarkScenarioFactory
{
    private const int CompaniesPerBattalion = 4;
    private const int BattalionsPerDivision = 12;
    private const int InfantryPerCompany = 100;
    private const int RoundsPerCompany = 1000;
    private const int UnderstrengthPersonnel = 20; // Provoque une demande réelle après deux heures.
    private const double HalfRouteMeters = 600;
    private const double ContactRadiusMeters = 100;
    private const double FootprintMeters = 20;
    private const double LateralOffsetMeters = 12;
    private const double SparseLaneSpacingMeters = 2400;
    private const double DenseLaneSpacingMeters = 24;
    private const double SectorRadiusMeters = 75;

    private sealed record Lane(BattleSector West, BattleSector Center, BattleSector East);
    private sealed record Layout(
        SimulationEngine Engine, TacticalBattle Battle, TacticalGeometryMap Geometry,
        IReadOnlyList<Lane> Lanes);
    private sealed record BattalionPair(Battalion? West, Battalion? East);

    internal static TacticalMobileDetectorScenario CreateDetector(TacticalMobileBenchmarkOptions options)
    {
        Validate(options);
        var layout = CreateLayout(options, sharedDenseLane: false);
        var random = new Random(options.Seed);
        int requested = RequestedMovers(options);
        var battalions = GetBattalionPairs(layout);
        var pairs = new List<(CompanyDeployment? West, CompanyDeployment? East, int LaneIndex)>();

        for (int i = 0; i < battalions.Length; i++)
        {
            var lane = layout.Lanes[i];
            var formation = battalions[i];
            for (int j = 0; j < CompaniesPerBattalion; j++)
            {
                var west = DeployAt(formation.West, j, lane.West);
                var east = DeployAt(formation.East, j, lane.East);
                if (west is not null || east is not null)
                    pairs.Add((west, east, i));
            }
        }

        // La sélection porte sur les paires pour conserver des croisements aux taux usuels.
        var pairOrder = Enumerable.Range(0, pairs.Count).ToArray();
        random.Shuffle(pairOrder.AsSpan());
        var trajectories = new List<TacticalTimedTrajectorySegment>(options.TotalCompanies);
        int mobile = 0;
        foreach (int index in pairOrder)
        {
            var pair = pairs[index];
            AddTrajectory(pair.West, eastbound: true, pair.LaneIndex % 3);
            AddTrajectory(pair.East, eastbound: false, pair.LaneIndex % 3);
        }

        return new TacticalMobileDetectorScenario(
            options, layout.Battle, layout.Geometry, layout.Engine.Diplomacy,
            trajectories.AsReadOnly(), mobile, ContactRadiusMeters);

        CompanyDeployment? DeployAt(Battalion? battalion, int index, BattleSector sector)
        {
            if (battalion is null || index >= battalion.Companies.Count)
                return null;

            return Deploy(layout, battalion.Companies[index], sector,
                CompanyDeploymentRole.FrontLine, random);
        }

        void AddTrajectory(CompanyDeployment? deployment, bool eastbound, int pattern)
        {
            if (deployment is null)
                return;

            var start = layout.Geometry.GetPosition(deployment)!.Point;
            if (mobile == requested)
            {
                trajectories.Add(new(deployment, start, start, 0, 1, IsMoving: false));
                return;
            }

            // 0 : croisements simultanés. 1 : trajets courts qui restent éloignés.
            // 2 : passages décalés au milieu de la liaison. Le premier mobile
            // libère le passage avant que le second ne commence son trajet.
            if (pattern == 2 && eastbound)
            {
                var middle = new TacticalPoint(0, start.YMeters, start.GroundElevationMeters);
                var exit = new TacticalPoint(0, start.YMeters + 3 * ContactRadiusMeters,
                    start.GroundElevationMeters);
                trajectories.Add(new(deployment, start, middle, 0, 0.25, IsMoving: true));
                trajectories.Add(new(deployment, middle, exit, 0.25, 0.45, IsMoving: true));
                trajectories.Add(new(deployment, exit, exit, 0.45, 1, IsMoving: false));
            }
            else
            {
                double distance = pattern == 1 ? HalfRouteMeters / 3 : 1.5 * HalfRouteMeters;
                var end = new TacticalPoint(
                    start.XMeters + (eastbound ? distance : -distance),
                    start.YMeters, start.GroundElevationMeters);
                double startTime = pattern == 2 ? 0.55 : 0;
                if (startTime > 0)
                    trajectories.Add(new(deployment, start, start, 0, startTime, IsMoving: false));
                trajectories.Add(new(deployment, start, end, startTime, 1, IsMoving: true));
            }
            mobile++;
        }
    }

    internal static TacticalMobileTickScenario CreateTick(
        TacticalMobileBenchmarkOptions options,
        TacticalPhaseTimingRecorder? recorder = null)
    {
        Validate(options);
        var layout = CreateLayout(options, sharedDenseLane: true);
        var random = new Random(options.Seed);
        var signatures = new TacticalSignatureFrame(layout.Battle);
        var pairs = GetBattalionPairs(layout);
        var reinforcements = new List<(CompanyDeployment Front, CompanyDeployment Reserve)>();

        for (int i = 0; i < pairs.Length; i++)
        {
            var lane = layout.Lanes[i % layout.Lanes.Count];
            DeployBattalion(pairs[i].West, lane.West, lane.East);
            DeployBattalion(pairs[i].East, lane.East, lane.West);
        }

        // Les règles du moteur autorisent une seule décision par demande.
        // Choisir les bataillons par paires pour que les renforts des deux camps se rencontrent.
        var selectedReinforcements = reinforcements.Take(RequestedMovers(options))
            .ToDictionary(pair => pair.Front.Company.ParentBattalion!, pair => pair);
        foreach (var (front, _) in selectedReinforcements.Values)
        {
            var manpower = front.Company.Composition!.Manpower;
            manpower.ApplyCasualties(new PersonnelCasualtyReport(
                manpower.Current - UnderstrengthPersonnel, 0, 0));
            layout.Battle.PersonnelExposure.Set(front, new(
                UnderstrengthPersonnel, 0, 0, FootprintMeters));
            layout.Battle.BattalionAssessments.SetPrioritySector(front.Company.ParentBattalion!, front.Sector);
        }

        var settings = new TacticalHourlyCombatSettings(
            detection: new TacticalDetectionProfile(500, 0, 360, 25),
            fire: new TacticalDirectFireProfile(250, fireArcDegrees: 360),
            hit: new TacticalHitProfile(2, 1, 0.5),
            defense: new TacticalDefenseProfile(1, 0.75, 0.5, 0, 0, 0),
            effects: new TacticalPersonnelEffectProfile(0.2, 0.3),
            maxWeaponsPerCompany: 10, roundsPerWeapon: 3,
            baseSeed: unchecked((ulong)(uint)options.Seed),
            suppression: new TacticalSuppressionProfile(10, 2, 10, 0.8),
            cohesion: new TacticalCohesionProfile(1.5, 40, 0.1, 3, 20, 0.5),
            battalionAssessment: new TacticalBattalionAssessmentProfile(3, 2, 1, 2, 60, 30),
            battalionReactions: new TacticalBattalionReactionProfile(2, 2, 10),
            reactionMovement: new TacticalReactionMovementProfile(
                2 * HalfRouteMeters, transitContactDistanceMeters: ContactRadiusMeters,
                useSimultaneousContacts: true),
            enableMobileCombat: options.EnableMobileCombat);

        var orchestrator = new TacticalHourlyCombatOrchestrator(
            layout.Battle,
            layout.Geometry,
            new TacticalPerceptionState(layout.Battle),
            signatures,
            layout.Engine.Diplomacy,
            settings,
            recorder);

        layout.Engine.Engagements.AttachTacticalBattle(layout.Battle);
        layout.Engine.Engagements.RegisterTacticalOrchestrator(orchestrator);

        if (selectedReinforcements.Count > 0)
        {
            // Heures de préparation, hors de la mesure du tick par le runner.
            layout.Engine.Tick();
            layout.Engine.Tick();
            foreach (var request in orchestrator.LastBattalionReactionPhase!.Requests)
            {
                if (request.Type == TacticalBattalionReactionType.Reinforce
                    && selectedReinforcements.TryGetValue(request.Battalion, out var pair))
                {
                    orchestrator.ReactionExecution!.Decide(
                        request, TacticalReactionDecision.Approve, company: pair.Reserve);
                }
            }
        }

        recorder?.Reset();
        return new TacticalMobileTickScenario(
            options, layout.Engine, layout.Battle, layout.Geometry, orchestrator);

        void DeployBattalion(Battalion? battalion, BattleSector front, BattleSector staging)
        {
            if (battalion is null)
                return;

            CompanyDeployment? frontline = null;
            for (int i = 0; i < battalion.Companies.Count; i++)
            {
                var role = i switch
                {
                    0 => CompanyDeploymentRole.FrontLine,
                    1 => CompanyDeploymentRole.Reserve,
                    _ => CompanyDeploymentRole.Support
                };
                var deployment = Deploy(layout, battalion.Companies[i],
                    role == CompanyDeploymentRole.Reserve ? staging : front, role, random);
                int personnel = deployment.Company.Composition!.Manpower.Current;
                int exposed = role == CompanyDeploymentRole.FrontLine ? personnel / 4 : 0;
                layout.Battle.PersonnelExposure.Set(deployment, new(
                    exposed, 0, personnel - exposed, FootprintMeters));
                signatures.Set(deployment, new(TacticalActivity.Stationary, TacticalConcealment.None));

                if (role == CompanyDeploymentRole.FrontLine)
                    frontline = deployment;
                else if (role == CompanyDeploymentRole.Reserve)
                    reinforcements.Add((frontline!, deployment));
            }
        }
    }

    private static Layout CreateLayout(TacticalMobileBenchmarkOptions options, bool sharedDenseLane)
    {
        var engine = new SimulationEngine();
        var west = new Country(1, "West");
        var east = new Country(2, "East");
        var province = new Province(1, "Benchmark", owner: west);
        engine.DeclareWar(west, east);
        int westCount = options.TotalCompanies / 2 + options.TotalCompanies % 2;
        AddFormations(engine, west, province, westCount);
        AddFormations(engine, east, province, options.TotalCompanies - westCount);
        engine.Tick();
        var field = new TacticalBattlefield();
        int laneCount = sharedDenseLane && options.Density == TacticalMobileBenchmarkDensity.Dense
            ? 1
            : (westCount - 1) / CompaniesPerBattalion + 1;
        double spacing = options.Density == TacticalMobileBenchmarkDensity.Dense
            ? DenseLaneSpacingMeters : SparseLaneSpacingMeters;
        var lanes = new List<Lane>(laneCount);
        for (int i = 0; i < laneCount; i++)
        {
            var lane = new Lane(AddSector(),
                AddSector(), AddSector());
            field.Connect(lane.West, lane.Center, HalfRouteMeters);
            field.Connect(lane.Center, lane.East, HalfRouteMeters);
            if (i > 0)
                field.Connect(lanes[i - 1].Center, lane.Center, spacing);

            lanes.Add(lane);
        }

        var battle = new TacticalBattle(engine.Engagements.ActiveEngagements.Single(), field);
        var geometry = new TacticalGeometryMap(battle);
        for (int i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            double y = i * spacing;
            geometry.DefineSector(lane.West, -HalfRouteMeters, y, SectorRadiusMeters);
            geometry.DefineSector(lane.Center, 0, y, SectorRadiusMeters);
            geometry.DefineSector(lane.East, HalfRouteMeters, y, SectorRadiusMeters);
        }

        return new Layout(engine, battle, geometry, lanes.AsReadOnly());

        BattleSector AddSector()
        {
            int id = field.Sectors.Count + 1;
            var sector = new BattleSector(id, $"Sector {id}", TerrainType.Plains);
            field.AddSector(sector);
            return sector;
        }
    }

    private static void AddFormations(SimulationEngine engine, Country country, Province province, int count)
    {
        const int capacity = BattalionsPerDivision * CompaniesPerBattalion;
        int divisionNumber = 0;
        while (count > 0)
        {
            int divisionSize = Math.Min(count, capacity);
            var division = new Division(
                $"{country.Name} Division {++divisionNumber}", province,
                fuel: 100, ammunition: 100, country: country,
                composition: new DivisionComposition(
                    manpower: divisionSize * InfantryPerCompany,
                    infantryEquipment: divisionSize * InfantryPerCompany,
                    artillery: 0, tanks: 0));

            for (int first = 0; first < divisionSize; first += CompaniesPerBattalion)
            {
                int id = first / CompaniesPerBattalion + 1;
                var battalion = new Battalion(id, $"Battalion {id}", BattalionType.Infantry);
                int battalionSize = Math.Min(divisionSize - first, CompaniesPerBattalion);
                for (int i = 1; i <= battalionSize; i++)
                {
                    battalion.AddCompany(new Company(i, $"Company {i}", CompanyType.Infantry,
                        new CompanyComposition(
                            manpower: InfantryPerCompany, infantryEquipment: InfantryPerCompany,
                            ammunition: new AmmunitionTotals(smallArms: RoundsPerCompany))));
                }
                division.AddBattalion(battalion);
            }

            division.EnableHierarchicalResources();
            engine.AddDivision(division);
            count -= divisionSize;
        }
    }

    private static BattalionPair[] GetBattalionPairs(Layout layout)
    {
        var camps = layout.Engine.Divisions.GroupBy(division => division.Country)
            .Select(camp => camp.SelectMany(division => division.Battalions).ToArray())
            .ToArray();
        return Enumerable.Range(0, Math.Max(camps[0].Length, camps[1].Length))
            .Select(i => new BattalionPair(camps[0].ElementAtOrDefault(i), camps[1].ElementAtOrDefault(i)))
            .ToArray();
    }

    private static CompanyDeployment Deploy(
        Layout layout, Company company, BattleSector sector, CompanyDeploymentRole role, Random random)
    {
        var deployment = layout.Battle.DeployCompany(company, sector, role);
        var geometry = layout.Geometry.GetSectorGeometry(sector)!;
        var position = new TacticalPoint(geometry.CenterXMeters,
            geometry.CenterYMeters + random.NextDouble() * LateralOffsetMeters, sector.ElevationMeters);
        double facing = geometry.CenterXMeters < 0 ? 90 : 270;
        layout.Geometry.PlaceCompany(deployment, position, facing, FootprintMeters);
        return deployment;
    }

    private static int RequestedMovers(TacticalMobileBenchmarkOptions options) =>
        (int)Math.Floor(options.TotalCompanies * options.MobilityPercent / 100);

    private static void Validate(TacticalMobileBenchmarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TotalCompanies < 2)
            throw new ArgumentOutOfRangeException(nameof(options.TotalCompanies));
        if (!double.IsFinite(options.MobilityPercent) || options.MobilityPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(options.MobilityPercent));
        if (!Enum.IsDefined(options.Density))
            throw new ArgumentOutOfRangeException(nameof(options.Density));
    }
}

