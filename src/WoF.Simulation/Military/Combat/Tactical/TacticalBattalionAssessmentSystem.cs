
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

internal enum TacticalBattalionMissionStatus
{
    Unassigned,
    Capable,
    AtRisk,
    UnableToHold
}

/// <summary>
/// Synthese calculee de l'etat d'un bataillon.
/// Les valeurs ne constituent pas un nouveau stock.
/// </summary>
internal sealed record TacticalBattalionAssessment(
    Battalion Battalion,
    int CompanyCount,
    int AuthorizedPersonnel,
    int AvailablePersonnel,
    double Cohesion,
    double CombatReadiness,
    BattleSector? PrioritySector,
    double? PrioritySectorReadiness,
    TacticalBattalionMissionStatus MissionStatus);

/// <summary>
/// Derive les capacites du bataillon a partir de ses
/// compagnies deployees, sans recalculer leurs pertes.
/// </summary>
internal sealed class TacticalBattalionAssessmentSystem
{
    private readonly TacticalBattle _battle;

    private readonly Dictionary<Battalion, BattleSector>
        _priorities = new(ReferenceEqualityComparer.Instance);

    internal TacticalBattalionAssessmentSystem(
        TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    /// <summary>
    /// Configure le secteur que le bataillon doit tenir.
    /// Ce choix pourra venir du commandement tactique.
    /// </summary>
    internal void SetPrioritySector(
        Battalion battalion,
        BattleSector sector)
    {
        ArgumentNullException.ThrowIfNull(battalion);
        ArgumentNullException.ThrowIfNull(sector);

        if (!_battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "Cannot assign a mission to an ended battle.");

        if (!_battle.Battlefield.ContainsSector(sector))
            throw new InvalidOperationException(
                "Priority sector is outside this battlefield.");

        if (_battle.GetBattalionDeployments(battalion).Count == 0)
            throw new InvalidOperationException(
                "Battalion has no deployment in this battle.");

        _priorities[battalion] = sector;
    }

    /// <summary>
    /// Recalcule l'etat du bataillon au moment de l'appel.
    /// Aucun cache de cohesion ou de ressources.
    /// </summary>
    internal TacticalBattalionAssessment Evaluate(
        Battalion battalion,
        TacticalBattalionAssessmentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(battalion);
        ArgumentNullException.ThrowIfNull(profile);

        var deployments =
            _battle.GetBattalionDeployments(battalion);

        if (deployments.Count == 0)
            throw new InvalidOperationException(
                "Battalion has no tactical deployment.");

        // Cette premiere version exige une couverture
        // complete du bataillon evalue. Une compagnie
        // non representee ne doit pas etre inventee
        // en tant que compagnie pleinement operationnelle.
        if (deployments.Count != battalion.Companies.Count)
            throw new InvalidOperationException(
                "Battalion is only partially deployed.");

        var byCompany = new Dictionary<
            Company,
            CompanyDeployment>(
                ReferenceEqualityComparer.Instance);

        foreach (var deployment in deployments)
        {
            if (!_battle.ContainsDeployment(deployment)
                || !ReferenceEquals(
                    deployment.Company.ParentBattalion,
                    battalion)
                || !byCompany.TryAdd(
                    deployment.Company, deployment))
            {
                throw new InvalidOperationException(
                    "Invalid battalion deployment.");
            }
        }

        _priorities.TryGetValue(
            battalion, out BattleSector? priority);

        double totalWeight = 0;
        double cohesionWeighted = 0;
        double readinessWeighted = 0;

        double priorityWeight = 0;
        double priorityReadinessWeighted = 0;

        foreach (var company in battalion.Companies)
        {
            if (!byCompany.TryGetValue(
                company, out var deployment))
            {
                throw new InvalidOperationException(
                    "A battalion company is not deployed.");
            }

            var pool = company.Composition?.Manpower
                ?? throw new InvalidOperationException(
                    "Company has no manpower composition.");

            double cohesion = _battle.Cohesion.Get(deployment);

            double availability = pool.Authorized == 0
                ? 0.0
                : (double)pool.Current / pool.Authorized;

            double companyReadiness =
                deployment.IsInTransit
                ? 0.0
                : cohesion * availability;

            double roleWeight = deployment.Role switch
            {
                CompanyDeploymentRole.FrontLine =>
                    profile.FrontLineWeight,

                CompanyDeploymentRole.Support =>
                    profile.SupportWeight,

                CompanyDeploymentRole.Reserve =>
                    profile.ReserveWeight,

                _ => throw new InvalidOperationException(
                    "Unknown company deployment role.")
            };

            bool holdsPriority =
                priority is not null
                && ReferenceEquals(
                    deployment.Sector, priority)
                && deployment.Role
                    == CompanyDeploymentRole.FrontLine
                && !deployment.IsInTransit;

            double weight =
                pool.Authorized * roleWeight
                * (holdsPriority
                    ? profile.PriorityFrontMultiplier
                    : 1.0);

            totalWeight += weight;
            cohesionWeighted += weight * cohesion;
            readinessWeighted += weight * companyReadiness;

            if (holdsPriority)
            {
                // Utiliser les effectifs autorises
                // pour ne pas faire disparaitre
                // les compagnies detruites du bilan.
                priorityWeight += pool.Authorized;

                priorityReadinessWeighted +=
                    pool.Authorized * companyReadiness;
            }
        }

        double battalionCohesion = totalWeight > 0
            ? cohesionWeighted / totalWeight
            : 0.0;

        double combatReadiness = totalWeight > 0
            ? readinessWeighted / totalWeight
            : 0.0;

        double? priorityReadiness = priority is null
            ? null
            : priorityWeight > 0
                ? priorityReadinessWeighted / priorityWeight
                : 0.0;

        TacticalBattalionMissionStatus status;

        if (priorityReadiness is null)
        {
            status = TacticalBattalionMissionStatus.Unassigned;
        }
        else if (priorityReadiness.Value
            >= profile.HoldThreshold)
        {
            status = TacticalBattalionMissionStatus.Capable;
        }
        else if (priorityReadiness.Value
            >= profile.RiskThreshold)
        {
            status = TacticalBattalionMissionStatus.AtRisk;
        }
        else
        {
            status = TacticalBattalionMissionStatus.UnableToHold;
        }

        var personnel = battalion.GetResourceTotals().Personnel;

        return new TacticalBattalionAssessment(
            Battalion: battalion,
            CompanyCount: battalion.Companies.Count,
            AuthorizedPersonnel: personnel.Authorized,
            AvailablePersonnel: personnel.Available,
            Cohesion: Math.Clamp(battalionCohesion, 0, 100),
            CombatReadiness: Math.Clamp(combatReadiness, 0, 100),
            PrioritySector: priority,
            PrioritySectorReadiness: priorityReadiness,
            MissionStatus: status);
    }

    /// <summary>
    /// Evalue une fois chaque bataillon represente
    /// dans cette bataille.
    ///
    /// Les bataillons totalement absents du champ
    /// tactique ne sont pas evalues ici.
    /// </summary>
    internal IReadOnlyList<TacticalBattalionAssessment>
        EvaluateAll(TacticalBattalionAssessmentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var seen = new HashSet<Battalion>(
            ReferenceEqualityComparer.Instance);

        var results = new List<TacticalBattalionAssessment>();

        foreach (var deployment in _battle.Deployments)
        {
            var battalion = deployment.Company.ParentBattalion
                ?? throw new InvalidOperationException(
                    "Deployment has no parent battalion.");

            if (!seen.Add(battalion))
                continue;

            results.Add(Evaluate(battalion, profile));
        }

        return Array.AsReadOnly(results.ToArray());
    }
}
