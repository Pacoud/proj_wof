
using WoF.Simulation.Diplomacy;

namespace WoF.Simulation.Military.Combat.Tactical;

internal enum TacticalReactionDecision
{
    Reject,
    Approve
}

internal enum TacticalMovementOutcome
{
    Arrived,
    BlockedByEnemy,
    UnitUnavailable
}

internal sealed record TacticalSectorMovementOrder(
    CompanyDeployment Company,
    BattleSector Origin,
    BattleSector Destination,
    CompanyDeploymentRole ArrivalRole,
    TacticalBattalionReactionType Reason,
    long IssuedHour,
    long ArrivalHour,
    IReadOnlyList<BattleSector> Route,
    double TotalDistanceMeters);

internal sealed record TacticalReactionDecisionResult(
    TacticalBattalionReactionRequest Request,
    TacticalReactionDecision Decision,
    TacticalSectorMovementOrder? Movement);

internal sealed record TacticalMovementCompletion(
    TacticalSectorMovementOrder Order,
    TacticalMovementOutcome Outcome);

internal sealed record TacticalMovementProgress(
    TacticalSectorMovementOrder Order,
    BattleSector CurrentSector,
    BattleSector NextSector,
    TacticalPoint Position,
    double DistanceTravelledMeters,
    double ProgressFraction,
    bool PausedByContact);

internal sealed record TacticalTransitContact(
    long Hour,
    CompanyDeployment MovingCompany,
    CompanyDeployment EnemyCompany,
    TacticalPoint Position,
    double DistanceMeters);

internal sealed record TacticalMovementPhaseResult(
    long Hour,
    IReadOnlyList<TacticalMovementCompletion> Completed,
    IReadOnlyList<TacticalMovementProgress> Active,
    IReadOnlyList<TacticalTransitContact> Contacts,
    IReadOnlyList<TacticalPredictedEncounter>? PredictedEncounters = null,
    TacticalSimultaneousMovementResolution? SimultaneousResolution = null);

/// <summary>
/// Routes multi-secteurs et progression horaire.
/// Les positions changent au cours du trajet.
/// Les pertes physiques restent gerees par le combat.
/// </summary>
internal sealed class TacticalReactionExecutionState
{
    private sealed class ActiveMovement
    {
        internal TacticalSectorMovementOrder Order { get; }
        internal TacticalPoint StartingPoint { get; }

        internal int LegIndex { get; set; }
        internal double DistanceOnLeg { get; set; }
        internal double DistanceTravelled { get; set; }
        internal bool PausedByContact { get; set; }

        internal ActiveMovement(
            TacticalSectorMovementOrder order,
            TacticalPoint startingPoint)
        {
            Order = order;
            StartingPoint = startingPoint;
        }
    }

    private readonly record struct MovementStep(
        BattleSector Destination,
        TacticalPoint Position,
        double Distance,
        bool AtWaypoint);

    private sealed record MovementPlan(
        IReadOnlyList<MovementStep> Steps,
        IReadOnlyList<TacticalTimedTrajectorySegment> Trajectory,
        TacticalMovementOutcome? Cancellation);

    private readonly TacticalBattle _battle;
    private readonly TacticalGeometryMap _geometry;
    private readonly DiplomacySystem _diplomacy;
    private readonly TacticalReactionMovementProfile _profile;

    private readonly Dictionary<
        CompanyDeployment, ActiveMovement> _pending =
            new(ReferenceEqualityComparer.Instance);

    private readonly HashSet<TacticalBattalionReactionRequest>
        _decided = new(ReferenceEqualityComparer.Instance);

    private long? _lastAdvancedHour;

    internal TacticalMovementPhaseResult? LastMovementPhase
    {
        get;
        private set;
    }

    internal TacticalSpatialIndexStatistics? LastSpatialIndexStatistics { get; private set; }

    internal IReadOnlyList<TacticalSectorMovementOrder>
        PendingMovements =>
            Array.AsReadOnly(
                _pending.Values.Select(p => p.Order).ToArray());

    internal TacticalReactionExecutionState(
        TacticalBattle battle,
        TacticalGeometryMap geometry,
        DiplomacySystem diplomacy,
        TacticalReactionMovementProfile profile)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));

        _geometry = geometry
            ?? throw new ArgumentNullException(nameof(geometry));

        _diplomacy = diplomacy
            ?? throw new ArgumentNullException(nameof(diplomacy));

        _profile = profile
            ?? throw new ArgumentNullException(nameof(profile));

        if (!ReferenceEquals(geometry.Battle, battle))
            throw new InvalidOperationException(
                "Geometry belongs to another battle.");
    }

    internal bool IsMoving(CompanyDeployment company)
    {
        ArgumentNullException.ThrowIfNull(company);
        return _pending.ContainsKey(company);
    }

    /// <summary>
    /// Valide une demande de C2.4d.5 et cree un
    /// itineraire complet avant toute mutation.
    /// </summary>
    internal TacticalReactionDecisionResult Decide(
        TacticalBattalionReactionRequest request,
        TacticalReactionDecision decision,
        CompanyDeployment? company = null,
        BattleSector? destination = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(decision))
            throw new ArgumentOutOfRangeException(
                nameof(decision));

        EnsureActive();

        var phase = _battle.BattalionReactions.LastPhase;

        if (phase is null
            || phase.Hour != request.Hour
            || !phase.Requests.Any(r =>
                ReferenceEquals(r, request)))
        {
            throw new InvalidOperationException(
                "Request is not in the latest reaction phase.");
        }

        if (_decided.Contains(request))
            throw new InvalidOperationException(
                "Reaction already decided.");

        if (decision == TacticalReactionDecision.Reject
            || request.Type ==
                TacticalBattalionReactionType.Reorganize)
        {
            if (company is not null || destination is not null)
                throw new ArgumentException(
                    "This decision does not accept a movement.");

            _decided.Add(request);

            return new TacticalReactionDecisionResult(
                request, decision, null);
        }

        if (company is null)
            throw new ArgumentNullException(nameof(company));

        if (!_battle.ContainsDeployment(company)
            || !ReferenceEquals(
                company.Company.ParentBattalion,
                request.Battalion))
        {
            throw new InvalidOperationException(
                "Company is not an eligible member of the battalion.");
        }

        if (_pending.ContainsKey(company)
            || company.IsInTransit)
        {
            throw new InvalidOperationException(
                "Company already has a movement.");
        }

        BattleSector target;
        CompanyDeploymentRole arrivalRole;

        if (request.Type ==
            TacticalBattalionReactionType.Reinforce)
        {
            if (!request.ReserveCandidates.Any(c =>
                    ReferenceEquals(c, company))
                || company.Role != CompanyDeploymentRole.Reserve)
            {
                throw new InvalidOperationException(
                    "Selected company is not a requested reserve.");
            }

            target = request.Sector;

            if (destination is not null
                && !ReferenceEquals(destination, target))
            {
                throw new InvalidOperationException(
                    "Invalid reinforcement destination.");
            }

            arrivalRole = CompanyDeploymentRole.FrontLine;
        }
        else if (request.Type ==
                 TacticalBattalionReactionType.RequestWithdrawal)
        {
            if (!request.AffectedFrontline.Any(c =>
                    ReferenceEquals(c, company))
                || company.Role != CompanyDeploymentRole.FrontLine)
            {
                throw new InvalidOperationException(
                    "Selected company is not an affected frontline unit.");
            }

            target = destination
                ?? throw new ArgumentNullException(
                    nameof(destination));

            arrivalRole = CompanyDeploymentRole.Reserve;
        }
        else
        {
            throw new InvalidOperationException(
                "Unsupported reaction request.");
        }

        if (company.Company.Composition!.Manpower.Current <= 0)
            throw new InvalidOperationException(
                "Company has no available personnel.");

        if (!_battle.Battlefield.ContainsSector(target))
            throw new InvalidOperationException(
                "Destination belongs to another battlefield.");

        if (request.Type ==
                TacticalBattalionReactionType.RequestWithdrawal
            && IsEnemyOccupied(target, company))
        {
            throw new InvalidOperationException(
                "Withdrawal destination is enemy occupied.");
        }
        TacticalSectorRoute route = TacticalRouteFinder.Find(
            _battle.Battlefield,
            company.Sector,
            target,
            _ => true);
 
        // Toute la geometrie doit exister avant
        // d'autoriser le mouvement.
        foreach (var sector in route.Sectors)
        {
            if (_geometry.GetSectorGeometry(sector) is null)
                throw new InvalidOperationException(
                    "Route has missing sector geometry.");
        }

        var initial = _geometry.GetPosition(company)
            ?? throw new InvalidOperationException(
                "Company has no initial position.");

        double theoreticalHours = Math.Max(
            1,
            Math.Ceiling(
                route.TotalDistanceMeters / _profile.MetersPerHour));

        if (!double.IsFinite(theoreticalHours)
            || theoreticalHours > long.MaxValue / 4.0)
        {
            throw new InvalidOperationException(
                "Movement duration exceeds supported range.");
        }

        long arrivalHour = checked(
            request.Hour + (long)theoreticalHours);

        var order = new TacticalSectorMovementOrder(
            company,
            company.Sector,
            target,
            arrivalRole,
            request.Type,
            request.Hour,
            arrivalHour,
            route.Sectors,
            route.TotalDistanceMeters);

        _pending.Add(
            company,
            new ActiveMovement(order, initial.Point));

        company.SetInTransit(true);
        _battle.NotifyDeploymentChanged(company);
        _battle.DefensiveState.Clear(company);

        _decided.Add(request);

        return new TacticalReactionDecisionResult(
            request, decision, order);
    }

    internal void EnsureCanAdvance(long hour)
    {
        EnsureActive();

        if (hour < _battle.Engagement.StartHour)
            throw new ArgumentOutOfRangeException(nameof(hour));

        if (_lastAdvancedHour is long last && hour <= last)
            throw new InvalidOperationException(
                "Movement hour already processed.");

        foreach (var state in _pending.Values)
        {
            var order = state.Order;

            if (hour <= order.IssuedHour
                || !order.Company.IsInTransit
                || state.LegIndex < 0
                || state.LegIndex >= order.Route.Count - 1
                || !ReferenceEquals(
                    order.Company.Sector,
                    order.Route[state.LegIndex])
                || _geometry.GetPosition(order.Company) is null)
            {
                throw new InvalidOperationException(
                    "Pending tactical movement is inconsistent.");
            }

            // Verification de la repartition avant
            // d'eventuelles mutations d'arrivee.
            _battle.PersonnelExposure.Get(order.Company);

            foreach (var sector in order.Route)
            {
                if (!_battle.Battlefield.ContainsSector(sector)
                    || _geometry.GetSectorGeometry(sector) is null)
                {
                    throw new InvalidOperationException(
                        "Movement route has invalid geometry.");
                }
            }
        }
    }

    /// <summary>
    /// Avance chaque compagnie d'une distance maximale
    /// egale a la vitesse horaire du profil.
    /// </summary>
    internal TacticalMovementPhaseResult AdvanceHour(long hour)
    {
        EnsureCanAdvance(hour);

        return _profile.UseSimultaneousContacts
            ? AdvanceHourSimultaneously(hour)
            : AdvanceHourLegacy(hour);
    }

    private TacticalMovementPhaseResult AdvanceHourLegacy(long hour)
    {
        // Index construit une seule fois par heure,
        // uniquement si des contacts de transit (compagnies)
        // doivent etre recherches.
        TacticalCompanySpatialIndex? spatialIndex =
            _pending.Count > 0
            && _profile.TransitContactDistanceMeters > 0
                ? new TacticalCompanySpatialIndex(
                    _battle,
                    _geometry,
                    _profile.SpatialCellSizeMeters)
                : null;

        var completions = new List<TacticalMovementCompletion>();
        var contacts = new List<TacticalTransitContact>();

        foreach (var state in _pending.Values.ToArray())
        {
            var company = state.Order.Company;
            state.PausedByContact = false;

            if (company.Company.Composition!.Manpower.Current <= 0)
            {
                Cancel(state, TacticalMovementOutcome.UnitUnavailable,
                    completions, spatialIndex);
                continue;
            }

            double budget = _profile.MetersPerHour;

            while (budget > 1e-9 && _pending.ContainsKey(company))
            {
                var to = state.Order.Route[state.LegIndex + 1];
                bool contestedReinforcement =
                    IsContestedReinforcement(state.Order, state.LegIndex);

                // Revalider les secteurs avant chaque
                // franchissement, meme si la route
                // etait libre lors de l'approbation.
                if (!contestedReinforcement
                    && IsEnemyOccupied(to, company))
                {
                    Cancel(state,
                        TacticalMovementOutcome.BlockedByEnemy,
                        completions, spatialIndex);
                    break;
                }

                var step = CalculateStep(
                    state.Order, state.StartingPoint, state.LegIndex,
                    state.DistanceOnLeg, budget);
                TacticalPoint actual = _geometry.GetPosition(company)!.Point;

                // Les contacts sur une liaison sont
                // rapportes sans inventer de tirs.
                //
                // Le dernier troncon d'un renfort
                // peut aboutir sur un front conteste.
                if (!contestedReinforcement
                    && _profile.TransitContactDistanceMeters > 0)
                {
                    var contact = FindContact(
                        hour, company, actual, step.Position, spatialIndex!);

                    if (contact is not null)
                    {
                        contacts.Add(contact);
                        state.PausedByContact = true;
                        break;
                    }
                }

                budget -= step.Distance;
                ApplyStep(state, step, completions, spatialIndex);
            }
        }

        LastSpatialIndexStatistics = spatialIndex?.Statistics;
        return PublishMovementPhase(hour, completions, contacts);
    }

    private TacticalMovementPhaseResult AdvanceHourSimultaneously(long hour)
    {
        if (_pending.Count == 0)
        {
            LastSpatialIndexStatistics = null;
            var emptyResolution = new TacticalSimultaneousMovementResolution(
                new HashSet<CompanyDeployment>(ReferenceEqualityComparer.Instance),
                Array.Empty<TacticalPredictedEncounter>(), 0, 0, 0);
            return PublishMovementPhase(
                hour, Array.Empty<TacticalMovementCompletion>(),
                Array.Empty<TacticalTransitContact>(), emptyResolution);
        }

        var deployments = _battle.Deployments.ToArray();
        var positions = deployments.ToDictionary(
            company => company,
            company => _geometry.GetPosition(company)?.Point,
            (IEqualityComparer<CompanyDeployment>)ReferenceEqualityComparer.Instance);
        var occupants = deployments.ToLookup(
            company => company.Sector,
            (IEqualityComparer<BattleSector>)ReferenceEqualityComparer.Instance);

        var plans = new Dictionary<CompanyDeployment, MovementPlan>(
            ReferenceEqualityComparer.Instance);
        foreach (var company in deployments)
        {
            if (_pending.TryGetValue(company, out var state))
            {
                plans.Add(company, PrepareMovement(
                    state, positions[company]!.Value, occupants));
            }
        }

        IReadOnlyList<TacticalTimedTrajectorySegment> BuildTimelines(
            IReadOnlySet<CompanyDeployment> paused)
        {
            var segments = new List<TacticalTimedTrajectorySegment>();
            foreach (var company in deployments)
            {
                if (plans.TryGetValue(company, out var plan) && !paused.Contains(company))
                {
                    segments.AddRange(plan.Trajectory);
                }
                else if (positions[company] is TacticalPoint point)
                {
                    segments.Add(new TacticalTimedTrajectorySegment(
                        company, point, point, 0, 1, IsMoving: false));
                }
            }

            return segments;
        }

        var resolution = TacticalSimultaneousMovementResolver.Resolve(
            _battle, _diplomacy,
            plans.Where(pair => pair.Value.Steps.Count > 0)
                .Select(pair => pair.Key).ToArray(),
            BuildTimelines, hour, _profile.TransitContactDistanceMeters);

        var completions = new List<TacticalMovementCompletion>();
        foreach (var (company, plan) in plans)
        {
            var state = _pending[company];
            state.PausedByContact = resolution.PausedCompanies.Contains(company);
            if (state.PausedByContact)
                continue;

            foreach (var step in plan.Steps)
                ApplyStep(state, step, completions, spatialIndex: null);

            if (plan.Cancellation is TacticalMovementOutcome outcome)
                Cancel(state, outcome, completions, spatialIndex: null);
        }

        LastSpatialIndexStatistics = null;
        return PublishMovementPhase(
            hour, completions, Array.Empty<TacticalTransitContact>(), resolution);
    }

    private MovementPlan PrepareMovement(
        ActiveMovement state,
        TacticalPoint initialPosition,
        ILookup<BattleSector, CompanyDeployment> occupants)
    {
        var steps = new List<MovementStep>();
        var trajectory = new List<TacticalTimedTrajectorySegment>();
        var company = state.Order.Company;
        var point = initialPosition;
        int legIndex = state.LegIndex;
        double distanceOnLeg = state.DistanceOnLeg;
        double budget = _profile.MetersPerHour;
        double elapsed = 0;
        double travelledThisHour = 0;
        TacticalMovementOutcome? cancellation = null;

        if (company.Company.Composition!.Manpower.Current <= 0)
        {
            cancellation = TacticalMovementOutcome.UnitUnavailable;
        }
        else
        {
            while (budget > 1e-9 && legIndex < state.Order.Route.Count - 1)
            {
                var destination = state.Order.Route[legIndex + 1];
                bool contestedReinforcement = IsContestedReinforcement(state.Order, legIndex);
                if (!contestedReinforcement
                    && IsEnemyOccupied(destination, company, occupants[destination]))
                {
                    cancellation = TacticalMovementOutcome.BlockedByEnemy;
                    break;
                }

                var step = CalculateStep(
                    state.Order, state.StartingPoint, legIndex, distanceOnLeg, budget);
                steps.Add(step);
                budget -= step.Distance;
                travelledThisHour += step.Distance;
                double endTime = Math.Min(travelledThisHour / _profile.MetersPerHour, 1);
                if (endTime > elapsed)
                {
                    trajectory.Add(new TacticalTimedTrajectorySegment(
                        company, point, step.Position, elapsed, endTime,
                        IsMoving: true,
                        IsContestedReinforcementLeg: contestedReinforcement));
                }

                elapsed = endTime;
                point = step.Position;
                if (step.AtWaypoint)
                {
                    legIndex++;
                    distanceOnLeg = 0;
                }
                else
                {
                    distanceOnLeg += step.Distance;
                }
            }
        }

        if (elapsed < 1)
        {
            trajectory.Add(new TacticalTimedTrajectorySegment(
                company, point, point, elapsed, 1, IsMoving: false));
        }

        return new MovementPlan(steps, trajectory, cancellation);
    }

    private static bool IsContestedReinforcement(
        TacticalSectorMovementOrder order, int legIndex)
    {
        return legIndex == order.Route.Count - 2
            && order.Reason == TacticalBattalionReactionType.Reinforce;
    }

    private MovementStep CalculateStep(
        TacticalSectorMovementOrder order,
        TacticalPoint startingPoint,
        int legIndex,
        double distanceOnLeg,
        double budget)
    {
        var from = order.Route[legIndex];
        var to = order.Route[legIndex + 1];
        double length = ConnectionLength(from, to);
        double distance = Math.Min(budget, length - distanceOnLeg);
        bool atWaypoint = distanceOnLeg + distance >= length - 1e-9;
        var end = SectorCenter(to, _geometry.GetSectorGeometry(to)!);
        var start = legIndex == 0
            ? startingPoint
            : SectorCenter(from, _geometry.GetSectorGeometry(from)!);
        double fraction = Math.Clamp((distanceOnLeg + distance) / length, 0, 1);
        var position = Interpolate(start, end, fraction);
        return new MovementStep(to, position, distance, atWaypoint);
    }

    private void ApplyStep(
        ActiveMovement state,
        MovementStep step,
        List<TacticalMovementCompletion> completions,
        TacticalCompanySpatialIndex? spatialIndex)
    {
        var company = state.Order.Company;
        state.DistanceOnLeg += step.Distance;
        state.DistanceTravelled += step.Distance;

        if (!step.AtWaypoint)
        {
            _geometry.MoveCompanyAlongConnection(company, step.Position);
            spatialIndex?.RefreshPosition(company);
            return;
        }

        if (state.LegIndex == state.Order.Route.Count - 2)
        {
            CompleteArrival(state, step.Destination, completions, spatialIndex);
            return;
        }

        _geometry.RelocateCompany(company, step.Destination);
        company.ApplyMovementArrival(step.Destination, company.Role);
        _battle.NotifyDeploymentChanged(company);
        _battle.DefensiveState.Clear(company);
        spatialIndex?.RefreshPosition(company);
        state.LegIndex++;
        state.DistanceOnLeg = 0;
    }

    private TacticalMovementPhaseResult PublishMovementPhase(
        long hour,
        IReadOnlyCollection<TacticalMovementCompletion> completions,
        IReadOnlyCollection<TacticalTransitContact> contacts,
        TacticalSimultaneousMovementResolution? resolution = null)
    {
        var active = _pending.Values
            .Select(state =>
            {
                var route = state.Order.Route;

                return new TacticalMovementProgress(
                    state.Order,
                    route[state.LegIndex],
                    route[state.LegIndex + 1],
                    _geometry.GetPosition(
                        state.Order.Company)!.Point,
                    state.DistanceTravelled,
                    Math.Clamp(
                        state.DistanceTravelled
                            / state.Order.TotalDistanceMeters,
                        0, 1),
                    state.PausedByContact);
            })
            .ToArray();

        var result = new TacticalMovementPhaseResult(
            hour,
            Array.AsReadOnly(completions.ToArray()),
            Array.AsReadOnly(active),
            Array.AsReadOnly(contacts.ToArray()),
            resolution?.PredictedEncounters,
            resolution);

        _lastAdvancedHour = hour;
        LastMovementPhase = result;

        return result;
    }

    private void CompleteArrival(
        ActiveMovement state,
        BattleSector destination,
        List<TacticalMovementCompletion> completions,
        TacticalCompanySpatialIndex? spatialIndex)
    {
        var company = state.Order.Company;

        var oldExposure =
            _battle.PersonnelExposure.Get(company);

        int available =
            company.Company.Composition!.Manpower.Current;

        int exposed = state.Order.ArrivalRole ==
            CompanyDeploymentRole.FrontLine
                ? Math.Clamp(
                    (int)Math.Ceiling(
                        available
                        * _profile.FrontLineExposureFraction),
                    1, available)
                : 0;

        var newExposure = new TacticalPersonnelExposure(
            exposed: exposed,
            sheltered: 0,
            reserve: available - exposed,
            exposedDepthMeters:
                oldExposure.ExposedDepthMeters);

        _geometry.RelocateCompany(company, destination);
        spatialIndex?.RefreshPosition(company);

        company.ApplyMovementArrival(
            destination, state.Order.ArrivalRole);

        company.SetInTransit(false);
        _battle.NotifyDeploymentChanged(company);
        _battle.DefensiveState.Clear(company);
        spatialIndex?.RefreshPosition(company);

        _battle.PersonnelExposure.Set(company, newExposure);

        _pending.Remove(company);

        completions.Add(new TacticalMovementCompletion(
            state.Order, TacticalMovementOutcome.Arrived));
    }

    private void Cancel(
        ActiveMovement state,
        TacticalMovementOutcome outcome,
        List<TacticalMovementCompletion> completions,
        TacticalCompanySpatialIndex? spatialIndex)
    {
        var company = state.Order.Company;

        // Revenir au dernier secteur effectivement
        // atteint, et non a l'origine de toute la route.
        _geometry.RelocateCompany(company, company.Sector);
        spatialIndex?.RefreshPosition(company);

        company.SetInTransit(false);
        _battle.NotifyDeploymentChanged(company);
        _battle.DefensiveState.Clear(company);
        spatialIndex?.RefreshPosition(company);

        _pending.Remove(company);

        completions.Add(
            new TacticalMovementCompletion(
                state.Order, outcome));
    }

    private double ConnectionLength(
        BattleSector from,
        BattleSector to)
    {
        var connection = _battle.Battlefield.Connections
            .SingleOrDefault(c => c.Connects(from, to));

        return connection?.DistanceMeters
            ?? throw new InvalidOperationException(
                "Tactical route connection disappeared.");
    }

    private static TacticalPoint SectorCenter(
        BattleSector sector,
        SectorGeometry geometry)
    {
        return new TacticalPoint(
            geometry.CenterXMeters,
            geometry.CenterYMeters,
            sector.ElevationMeters);
    }

    private static TacticalPoint Interpolate(
        TacticalPoint a,
        TacticalPoint b,
        double fraction)
    {
        return new TacticalPoint(
            a.XMeters + (b.XMeters - a.XMeters) * fraction,
            a.YMeters + (b.YMeters - a.YMeters) * fraction,
            a.GroundElevationMeters
                + (b.GroundElevationMeters
                    - a.GroundElevationMeters) * fraction);
    }

    private TacticalTransitContact? FindContact(
        long hour,
        CompanyDeployment moving,
        TacticalPoint start,
        TacticalPoint end,
        TacticalCompanySpatialIndex spatialIndex)
    {
        var country = moving.Company.ParentBattalion!
            .ParentDivision!.Country;

        if (country is null)
            return null;

        foreach (var enemy in spatialIndex.QueryNearSegment(
            start,
            end,
            _profile.TransitContactDistanceMeters
        ))
        {
            if (ReferenceEquals(enemy, moving)
                || enemy.Company.Composition!.Manpower.Current <= 0)
            {
                continue;
            }

            var enemyCountry = enemy.Company.ParentBattalion!
                .ParentDivision!.Country;

            if (enemyCountry is null
                || !_diplomacy.AreAtWar(country, enemyCountry))
            {
                continue;
            }

            var enemyPosition = _geometry.GetPosition(enemy);

            if (enemyPosition is null)
                continue;

            double distance = DistanceToSegment(
                enemyPosition.Point, start, end);

            if (distance <=
                _profile.TransitContactDistanceMeters)
            {
                return new TacticalTransitContact(
                    hour,
                    moving,
                    enemy,
                    start,
                    distance);
            }
        }

        return null;
    }

    private static double DistanceToSegment(
        TacticalPoint point,
        TacticalPoint a,
        TacticalPoint b)
    {
        double dx = b.XMeters - a.XMeters;
        double dy = b.YMeters - a.YMeters;

        double squared = dx * dx + dy * dy;

        double fraction = squared <= 1e-12
            ? 0.0
            : Math.Clamp(
                ((point.XMeters - a.XMeters) * dx
                 + (point.YMeters - a.YMeters) * dy)
                / squared,
                0, 1);

        double closestX = a.XMeters + fraction * dx;
        double closestY = a.YMeters + fraction * dy;

        return double.Hypot(
            point.XMeters - closestX,
            point.YMeters - closestY);
    }

    private bool IsEnemyOccupied(
        BattleSector sector,
        CompanyDeployment moving,
        IEnumerable<CompanyDeployment>? occupants = null)
    {
        var country = moving.Company.ParentBattalion!
            .ParentDivision!.Country;

        if (country is null)
            return true;

        return (occupants ?? _battle.Deployments).Any(other =>
        {
            if (ReferenceEquals(other, moving)
                || !ReferenceEquals(other.Sector, sector)
                || other.Company.Composition!.Manpower.Current <= 0)
                return false;

            var otherCountry = other.Company.ParentBattalion!
                .ParentDivision!.Country;

            return otherCountry is null
                || _diplomacy.AreAtWar(country, otherCountry);
        });
    }

    private void EnsureActive()
    {
        if (!_battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "The tactical engagement has ended.");
    }
}
