
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
    TacticalSimultaneousMovementResolution? SimultaneousResolution = null,
   IReadOnlyList<TacticalConfirmedMobileContact>? ConfirmedMobileContacts = null);

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

    private readonly  bool _enableMobileCombat ;

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
        TacticalReactionMovementProfile profile,
        bool enableMobileCombat = false
        )
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));

        _geometry = geometry
            ?? throw new ArgumentNullException(nameof(geometry));

        _diplomacy = diplomacy
            ?? throw new ArgumentNullException(nameof(diplomacy));

        _profile = profile
            ?? throw new ArgumentNullException(nameof(profile));

        _enableMobileCombat = enableMobileCombat;

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

        if (_enableMobileCombat)
            return AdvanceHourWithMobileCombat(hour);

        if (_profile.UseSimultaneousContacts)
            return AdvanceHourSimultaneously(hour);

        return AdvanceHourLegacy(hour);

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

    private TacticalMovementPhaseResult AdvanceHourWithMobileCombat(long hour)
    {
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
        var timelines = new Dictionary<
            CompanyDeployment, IReadOnlyList<TacticalTimedTrajectorySegment>>(
            ReferenceEqualityComparer.Instance);
        var movementEnds = new Dictionary<CompanyDeployment, double>(
            ReferenceEqualityComparer.Instance);

        // 1. Préparer tous les trajets à partir du même état initial.
        foreach (var company in deployments)
        {
            if (_pending.TryGetValue(company, out var state))
            {
                var plan = PrepareMovement(state, positions[company]!.Value, occupants);
                var movingSegments = plan.Trajectory
                    .Where(segment => segment.IsMoving)
                    .ToArray();

                if (movingSegments.Length != plan.Steps.Count)
                    throw new InvalidOperationException("Movement steps and timeline disagree.");

                plans.Add(company, plan);
                timelines.Add(company, plan.Trajectory);

                if (movingSegments.Length > 0)
                    movementEnds.Add(company, movingSegments[^1].EndTime);
            }
            else if (positions[company] is TacticalPoint point)
            {
                timelines.Add(company, new[]
                {
                    new TacticalTimedTrajectorySegment(
                        company, point, point, 0, 1, IsMoving: false)
                });
            }
        }

        var stops = new Dictionary<CompanyDeployment, double>(
            ReferenceEqualityComparer.Instance);
        var encounters = new Dictionary<
            (CompanyDeployment First, CompanyDeployment Second),
            TacticalFirstMobileContactEvent>();

        IReadOnlyList<TacticalPredictedEncounter> initialPredictions =
            Array.Empty<TacticalPredictedEncounter>();
        int passes = 0;
        long candidates = 0;
        long exactChecks = 0;

        bool CanStop(CompanyDeployment company, double time)
        {
            return movementEnds.TryGetValue(company, out double end)
                && time < end
                && !stops.ContainsKey(company);
        }

        void StopAt(CompanyDeployment company, double time)
        {
            if (!CanStop(company, time))
                return;

            stops.Add(company, time);
            timelines[company] = TruncateTimeline(timelines[company], time);
        }

        // 2. Retenir le prochain arrêt, puis recalculer les rencontres.
        while (movementEnds.Count > 0)
        {
            if (++passes > movementEnds.Count + 1)
                throw new InvalidOperationException("Mobile contacts did not stabilize.");

            var detection = TacticalSimultaneousContactDetector.Detect(
                _battle,
                _diplomacy,
                timelines.Values.SelectMany(segments => segments).ToArray(),
                hour,
                _profile.TransitContactDistanceMeters,
                includeFirstContacts: true);

            if (passes == 1)
                initialPredictions = detection.Encounters;

            candidates += detection.Statistics.BroadPhaseCandidates;
            exactChecks += detection.Statistics.ExactPairChecks;

            // FirstContacts est déjà trié par instant.
            var next = detection.FirstContacts.FirstOrDefault(contact =>
                CanStop(contact.First, contact.TimeFraction)
                || CanStop(contact.Second, contact.TimeFraction));

            if (next is null)
            {
                foreach (var contact in detection.FirstContacts)
                    encounters.TryAdd((contact.First, contact.Second), contact);

                break;
            }

            // Conserver les contacts atteints jusqu'au prochain arrêt inclus.
            foreach (var contact in detection.FirstContacts)
            {
                if (contact.TimeFraction > next.TimeFraction)
                    break;

                encounters.TryAdd((contact.First, contact.Second), contact);
                StopAt(contact.First, contact.TimeFraction);
                StopAt(contact.Second, contact.TimeFraction);
            }
        }

        // 3. Projeter les positions finales avant de modifier la carte.
        var finalPositions = new Dictionary<CompanyDeployment, TacticalPoint>(
            ReferenceEqualityComparer.Instance);
        var stillInTransit = new HashSet<CompanyDeployment>(
            ReferenceEqualityComparer.Instance);

        foreach (var (company, timeline) in timelines)
        {
            var point = timeline[^1].To;

            if (plans.TryGetValue(company, out var plan))
            {
                var state = _pending[company];

                if (stops.ContainsKey(company))
                {
                    stillInTransit.Add(company);
                }
                else
                {
                    int finalLeg = state.LegIndex
                        + plan.Steps.Count(step => step.AtWaypoint);
                    var finalSector = state.Order.Route[finalLeg];

                    if (plan.Cancellation is not null)
                    {
                        // Cancel ramène la compagnie au centre du dernier secteur atteint.
                        point = SectorCenter(finalSector, _geometry.GetSectorGeometry(finalSector)!);
                    }
                    else if (finalLeg < state.Order.Route.Count - 1)
                    {
                        stillInTransit.Add(company);
                    }
                }
            }

            finalPositions.Add(company, point);
        }

        const double positionTolerance = 0.01;
        double radius = _profile.TransitContactDistanceMeters;

        var projectedContacts = encounters.Values
            .Where(contact =>
                (stillInTransit.Contains(contact.First) || stillInTransit.Contains(contact.Second))
                && finalPositions[contact.First].HorizontalDistanceTo(
                    finalPositions[contact.Second]) <= radius + positionTolerance)
            .OrderBy(contact => contact.TimeFraction)
            .ToArray();

        // 4. Appliquer les plans validés.
        var completions = new List<TacticalMovementCompletion>();
        foreach (var (company, plan) in plans)
        {
            var state = _pending[company];
            state.PausedByContact = stops.TryGetValue(company, out double stopTime);

            if (state.PausedByContact)
            {
                ApplyPlanUntil(state, plan, stopTime, completions);
                continue;
            }

            foreach (var step in plan.Steps)
                ApplyStep(state, step, completions, spatialIndex: null);

            if (plan.Cancellation is TacticalMovementOutcome outcome)
                Cancel(state, outcome, completions, spatialIndex: null);
        }

        // 5. Confirmer les contacts à partir des positions réellement appliquées.
        var confirmed = new List<TacticalConfirmedMobileContact>();
        foreach (var contact in projectedContacts)
        {
            var first = _geometry.GetPosition(contact.First);
            var second = _geometry.GetPosition(contact.Second);

            if (first is null || second is null
                || (!contact.First.IsInTransit && !contact.Second.IsInTransit)
                || first.Point.HorizontalDistanceTo(second.Point) > radius + positionTolerance)
            {
                continue;
            }

            confirmed.Add(new TacticalConfirmedMobileContact(
                hour, contact.First, contact.Second, contact.TimeFraction,
                first.Point, second.Point));
        }

        var resolution = new TacticalSimultaneousMovementResolution(
            new HashSet<CompanyDeployment>(stops.Keys, ReferenceEqualityComparer.Instance),
            initialPredictions,
            passes,
            candidates,
            exactChecks);

        LastSpatialIndexStatistics = null;
        return PublishMovementPhase(
            hour, completions, Array.Empty<TacticalTransitContact>(),
            resolution, confirmed.AsReadOnly());
    }

    private static IReadOnlyList<TacticalTimedTrajectorySegment> TruncateTimeline(
        IReadOnlyList<TacticalTimedTrajectorySegment> timeline,
        double stopTime)
    {
        var truncated = new List<TacticalTimedTrajectorySegment>();
        var stopPoint = timeline[0].From;

        foreach (var segment in timeline)
        {
            if (segment.StartTime >= stopTime)
                break;

            if (segment.EndTime <= stopTime)
            {
                truncated.Add(segment);
                stopPoint = segment.To;
                continue;
            }

            double fraction = (stopTime - segment.StartTime)
                / (segment.EndTime - segment.StartTime);
            stopPoint = Interpolate(segment.From, segment.To, fraction);
            truncated.Add(segment with { To = stopPoint, EndTime = stopTime });
            break;
        }

        if (stopTime < 1)
        {
            truncated.Add(new TacticalTimedTrajectorySegment(
                timeline[0].Company, stopPoint, stopPoint,
                stopTime, 1, IsMoving: false));
        }

        return truncated;
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
        TacticalSimultaneousMovementResolution? resolution = null,
        IReadOnlyList<TacticalConfirmedMobileContact>? ConfirmedMobileContacts = null)
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
            resolution,
            ConfirmedMobileContacts);

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

    private void ApplyPlanUntil(
        ActiveMovement state,
        MovementPlan plan,
        double stopTime,
        List<TacticalMovementCompletion> completions)
    {
        int stepIndex = 0;

        foreach (var segment in plan.Trajectory)
        {
            if (!segment.IsMoving)
                continue;

            var step = plan.Steps[stepIndex++];

            if (segment.StartTime >= stopTime)
                break;

            if (segment.EndTime <= stopTime)
            {
                ApplyStep(
                    state,
                    step,
                    completions,
                    spatialIndex: null);

                continue;
            }

            double fraction = Math.Clamp(
                (stopTime - segment.StartTime)
                    / (segment.EndTime - segment.StartTime),
                0, 1);

            double partialDistance =
                step.Distance * fraction;

            var point = new TacticalPoint(
                segment.From.XMeters
                    + (segment.To.XMeters
                    - segment.From.XMeters) * fraction,
                segment.From.YMeters
                    + (segment.To.YMeters
                    - segment.From.YMeters) * fraction,
                segment.From.GroundElevationMeters
                    + (segment.To.GroundElevationMeters
                    - segment.From.GroundElevationMeters)
                    * fraction);

            ApplyStep(
                state,
                new MovementStep(
                    step.Destination,
                    point,
                    partialDistance,
                    AtWaypoint: false),
                completions,
                spatialIndex: null);

            break;
        }
    }


}
