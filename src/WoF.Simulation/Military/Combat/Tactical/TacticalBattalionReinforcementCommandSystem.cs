
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Selectionne une reserve pour une demande de
/// renforcement deja emise par un bataillon.
///
/// Ne consulte ni les positions reelles ennemies,
/// ni les diagnostics omniscients 9e/9g.
///
/// Les mouvements sont confies au systeme existant.
/// </summary>
internal sealed class TacticalBattalionReinforcementCommandSystem
{
    private readonly TacticalBattle _battle;
    private readonly TacticalGeometryMap _geometry;
    private readonly TacticalReactionExecutionState _movements;

    private readonly HashSet<TacticalBattalionReactionRequest>
        _approved = new(ReferenceEqualityComparer.Instance);

    internal TacticalBattalionReinforcementCommandSystem(
        TacticalHourlyCombatOrchestrator orchestrator)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);

        _battle = orchestrator.Battle;

        _movements = orchestrator.ReactionExecution
            ?? throw new InvalidOperationException(
                "Orchestrator has no tactical movement service.");

        _geometry = _movements.Geometry;

        if (!ReferenceEquals(_movements.Battle, _battle)
            || !ReferenceEquals(_geometry.Battle, _battle))
        {
            throw new InvalidOperationException(
                "Inconsistent orchestrator services.");
        }
    }

    /// <summary>
    /// Tente d'approuver un renfort.
    ///
    /// Null signifie qu'aucun ordre n'a ete emis :
    /// demande non concernee ou reserve inaccessible.
    ///
    /// Les demandes perimees ou rejouees sont refusees.
    /// </summary>
    internal TacticalReactionDecisionResult? TryApprove(
        TacticalBattalionReactionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "Tactical battle has ended.");

        var phase = _battle.BattalionReactions.LastPhase;

        if (phase is null
            || phase.Hour != request.Hour
            || !phase.Requests.Any(r =>
                ReferenceEquals(r, request)))
        {
            throw new InvalidOperationException(
                "Request is not in the current reaction phase.");
        }

        if (_approved.Contains(request))
            throw new InvalidOperationException(
                "Reinforcement request already approved.");

        // Aucun retrait automatique dans cette iteration.
        if (request.Type != TacticalBattalionReactionType.Reinforce)
            return null;

        CompanyDeployment? selected = null;
        double shortestDistance = double.PositiveInfinity;

        // Determinisme : priorite a l'itineraire le plus
        // court, puis au plus petit identifiant de compagnie.
        foreach (var reserve in request.ReserveCandidates
            .OrderBy(r => r.Company.Id))
        {
            if (!_battle.ContainsDeployment(reserve)
                || !ReferenceEquals(
                    reserve.Company.ParentBattalion,
                    request.Battalion)
                || reserve.Role != CompanyDeploymentRole.Reserve
                || reserve.IsInTransit
                || _movements.IsMoving(reserve)
                || reserve.Company.Composition?.Manpower.Current <= 0
                || ReferenceEquals(reserve.Sector, request.Sector)
                || _geometry.GetPosition(reserve) is null)
            {
                continue;
            }

            // Ne consulter ici que le graphe tactique.
            // Aucune information sur l'ennemi.
            if (!IsReachable(
                reserve.Sector, request.Sector))
            {
                continue;
            }

            var route = TacticalRouteFinder.Find(
                _battle.Battlefield,
                reserve.Sector,
                request.Sector,
                sector => _geometry.GetSectorGeometry(sector) is not null);

            // La route doit etre exploitable par
            // le vrai systeme de mouvement.
            if (route.Sectors.Any(s =>
                _geometry.GetSectorGeometry(s) is null))
            {
                continue;
            }

            if (route.TotalDistanceMeters < shortestDistance)
            {
                shortestDistance = route.TotalDistanceMeters;
                selected = reserve;
            }
        }

        if (selected is null)
            return null;

        // L'approbation reelle demeure autoritaire :
        // Decide valide l'ordre et demarre le transit.
        var result = _movements.Decide(
            request,
            TacticalReactionDecision.Approve,
            company: selected);

        _approved.Add(request);

        return result;
    }

    /// <summary>
    /// Test de connectivite sans interrogation
    /// de l'occupation ennemie reelle.
    /// </summary>
    private bool IsReachable(
        BattleSector origin,
        BattleSector destination)
    {
        if (!_battle.Battlefield.ContainsSector(origin)
            || !_battle.Battlefield.ContainsSector(destination))
        {
            return false;
        }

        var visited = new HashSet<BattleSector>(
            ReferenceEqualityComparer.Instance);

        var queue = new Queue<BattleSector>();

        visited.Add(origin);
        queue.Enqueue(origin);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (ReferenceEquals(current, destination))
                return true;

            foreach (var neighbor in
                _battle.Battlefield.GetNeighbors(current))
            {
               if (_geometry.GetSectorGeometry(neighbor) is not null
                    && visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return false;
    }
}
