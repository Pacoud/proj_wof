
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
    long ArrivalHour);

internal sealed record TacticalReactionDecisionResult(
    TacticalBattalionReactionRequest Request,
    TacticalReactionDecision Decision,
    TacticalSectorMovementOrder? Movement);

internal sealed record TacticalMovementCompletion(
    TacticalSectorMovementOrder Order,
    TacticalMovementOutcome Outcome);

internal sealed record TacticalMovementPhaseResult(
    long Hour,
    IReadOnlyList<TacticalMovementCompletion> Completed);

/// <summary>
/// Execution controlee des demandes de C2.4d.5.
/// Une demande doit etre authentique et approuvee.
/// Les mouvements portent sur les vrais deploiements.
/// </summary>
internal sealed class TacticalReactionExecutionState
{
    private readonly TacticalBattle _battle;
    private readonly TacticalGeometryMap _geometry;
    private readonly DiplomacySystem _diplomacy;
    private readonly TacticalReactionMovementProfile _profile;

    private readonly Dictionary<
        CompanyDeployment,
        TacticalSectorMovementOrder> _pending =
            new(ReferenceEqualityComparer.Instance);

    private readonly HashSet<TacticalBattalionReactionRequest>
        _decided = new(ReferenceEqualityComparer.Instance);

    private long? _lastAdvancedHour;

    internal TacticalMovementPhaseResult? LastMovementPhase
    {
        get;
        private set;
    }

    internal IReadOnlyList<TacticalSectorMovementOrder>
        PendingMovements =>
            Array.AsReadOnly(_pending.Values.ToArray());

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
    /// Le commandement choisit explicitement la reaction.
    ///
    /// Reinforce : reserve choisie et destination imposee
    /// au secteur demande.
    ///
    /// RequestWithdrawal : compagnie de premiere ligne
    /// choisie et destination adjacente explicite.
    ///
    /// Reorganize : accuse de decision seulement pour
    /// cette premiere version, sans mouvement physique.
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
                "Reaction request already decided.");

        if (decision == TacticalReactionDecision.Reject)
        {
            if (company is not null || destination is not null)
                throw new ArgumentException(
                    "Rejected request cannot specify a movement.");

            _decided.Add(request);

            return new TacticalReactionDecisionResult(
                request, decision, null);
        }

        if (request.Type ==
            TacticalBattalionReactionType.Reorganize)
        {
            if (company is not null || destination is not null)
                throw new ArgumentException(
                    "Reorganization has no movement in this version.");

            _decided.Add(request);

            return new TacticalReactionDecisionResult(
                request, decision, null);
        }

        if (company is null)
            throw new ArgumentNullException(nameof(company));

        if (!_battle.ContainsDeployment(company))
            throw new InvalidOperationException(
                "Selected company belongs to another battle.");

        if (!ReferenceEquals(
            company.Company.ParentBattalion,
            request.Battalion))
        {
            throw new InvalidOperationException(
                "Selected company is not in the requesting battalion.");
        }

        if (_pending.ContainsKey(company))
            throw new InvalidOperationException(
                "Company already has a pending movement.");

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

            if (company.Company.Composition!.Manpower.Current <= 0)
                throw new InvalidOperationException(
                    "Reserve has no available personnel.");

            target = request.Sector;

            if (destination is not null
                && !ReferenceEquals(destination, target))
            {
                throw new InvalidOperationException(
                    "Reinforcement destination differs from request.");
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

            if (IsEnemyOccupied(target, company))
                throw new InvalidOperationException(
                    "Withdrawal destination contains an enemy.");
            
            arrivalRole = CompanyDeploymentRole.Reserve;
        }
        else
        {
            throw new InvalidOperationException(
                "Unsupported reaction request.");
        }

        if (!_battle.Battlefield.ContainsSector(target))
            throw new InvalidOperationException(
                "Destination is outside the battlefield.");

        if (ReferenceEquals(target, company.Sector))
            throw new InvalidOperationException(
                "Movement requires a different sector.");

        var connection = _battle.Battlefield.Connections
            .SingleOrDefault(c =>
                c.Connects(company.Sector, target));

        if (connection is null)
            throw new InvalidOperationException(
                "Movement requires a direct sector connection.");

        if (_geometry.GetPosition(company) is null
            || _geometry.GetSectorGeometry(target) is null)
        {
            throw new InvalidOperationException(
                "Movement requires defined source and destination geometry.");
        }

        double travelHours = Math.Max(
            1.0,
            Math.Ceiling(
                connection.DistanceMeters / _profile.MetersPerHour));

        if (!double.IsFinite(travelHours)
            || travelHours > long.MaxValue / 2.0)
        {
            throw new InvalidOperationException(
                "Movement duration exceeds supported range.");
        }

        long arrivalHour = checked(
            request.Hour + (long)travelHours);

        var move = new TacticalSectorMovementOrder(
            company,
            company.Sector,
            target,
            arrivalRole,
            request.Type,
            request.Hour,
            arrivalHour);

        _pending.Add(company, move);
        _decided.Add(request);

        return new TacticalReactionDecisionResult(
            request,
            decision,
            move);
    }

    /// <summary>
    /// Controle les mouvements avant l'heure de combat.
    /// Ne modifie aucune position.
    /// </summary>
    internal void EnsureCanAdvance(long hour)
    {
        EnsureActive();

        if (hour < _battle.Engagement.StartHour)
            throw new ArgumentOutOfRangeException(nameof(hour));

        if (_lastAdvancedHour is long previous
            && hour <= previous)
        {
            throw new InvalidOperationException(
                "Movement hour already processed.");
        }

        foreach (var move in _pending.Values)
        {
            if (hour <= move.IssuedHour
                || !ReferenceEquals(
                    move.Company.Sector, move.Origin)
                || _geometry.GetPosition(move.Company) is null
                || _geometry.GetSectorGeometry(
                    move.Destination) is null)
            {
                throw new InvalidOperationException(
                    "Pending movement is inconsistent.");
            }
        }
    }

    /// <summary>
    /// Termine les transferts dont la duree est echue.
    /// Aucun deplacement n'est instantane a l'emission.
    /// </summary>
    internal TacticalMovementPhaseResult AdvanceHour(long hour)
    {
        EnsureCanAdvance(hour);

        var due = _pending.Values
            .Where(m => m.ArrivalHour <= hour)
            .ToArray();

        var completed = new List<TacticalMovementCompletion>();

        foreach (var move in due)
        {
            TacticalMovementOutcome outcome;

            if (move.Company.Company.Composition!
                    .Manpower.Current <= 0)
            {
                outcome = TacticalMovementOutcome.UnitUnavailable;
            }
            else if (move.Reason ==
                    TacticalBattalionReactionType.RequestWithdrawal
                && IsEnemyOccupied(
                    move.Destination, move.Company))
            {
                outcome = TacticalMovementOutcome.BlockedByEnemy;
            }
            else
            {
            // Lire la repartition actuelle avant la mutation.
            var currentExposure =
                _battle.PersonnelExposure.Get(move.Company);

            int available =
                move.Company.Company.Composition!.Manpower.Current;

            int exposed = move.ArrivalRole ==
                CompanyDeploymentRole.FrontLine
                    ? Math.Clamp(
                        (int)Math.Ceiling(
                            available * _profile.FrontLineExposureFraction),
                        1,
                        available)
                    : 0;

            var newExposure = new TacticalPersonnelExposure(
                exposed: exposed,
                sheltered: 0,
                reserve: available - exposed,
                exposedDepthMeters:
                    currentExposure.ExposedDepthMeters);

            // Tous les objets restent ceux de la compagnie
            // deja presente dans le moteur.
                _geometry.RelocateCompany(
                    move.Company, move.Destination);

                move.Company.ApplyMovementArrival(
                    move.Destination, move.ArrivalRole);

                _battle.NotifyDeploymentChanged(move.Company);

                // Ne pas transporter artificiellement
                // le couvert de l'ancien secteur.
                _battle.DefensiveState.Clear(move.Company);
                _battle.PersonnelExposure.Set(move.Company, newExposure);

                outcome = TacticalMovementOutcome.Arrived;
            }

            _pending.Remove(move.Company);

            completed.Add(new TacticalMovementCompletion(
                move, outcome));
        }

        _lastAdvancedHour = hour;

        var result = new TacticalMovementPhaseResult(
            hour,
            Array.AsReadOnly(completed.ToArray()));

        LastMovementPhase = result;
        return result;
    }

    private bool IsEnemyOccupied(
        BattleSector sector,
        CompanyDeployment company)
    {
        var country = company.Company.ParentBattalion!
            .ParentDivision!.Country;

        if (country is null)
            return true;

        return _battle.Deployments.Any(other =>
        {
            if (ReferenceEquals(other, company)
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
