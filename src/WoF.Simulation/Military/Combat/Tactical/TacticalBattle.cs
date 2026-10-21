
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

public sealed class TacticalBattle
{
    private readonly List<CompanyDeployment> _deployments = new();

    private readonly Dictionary<Company, CompanyDeployment>
        _byCompany = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<Division, TacticalDeploymentPlan>
        _appliedPlans = new(ReferenceEqualityComparer.Instance);

    internal IEnumerable<TacticalDeploymentPlan> AppliedPlans =>
        _appliedPlans.Values;

    internal bool HasAppliedPlan(Division division) =>
        _appliedPlans.ContainsKey(division);

    internal void RecordAppliedPlan(TacticalDeploymentPlan plan)
    {
        if (!ReferenceEquals(plan.Battle, this))
            throw new InvalidOperationException(
                "The plan belongs to another tactical battle.");

        if (!_appliedPlans.TryAdd(plan.Division, plan))
            throw new InvalidOperationException(
                "An applied plan already exists for this division.");
    }

    public Engagement Engagement { get; }

    public TacticalBattlefield Battlefield { get; }

    public IReadOnlyList<CompanyDeployment> Deployments { get; }

    public TacticalBattle(
        Engagement engagement,
        TacticalBattlefield battlefield)
    {
        ArgumentNullException.ThrowIfNull(engagement);
        ArgumentNullException.ThrowIfNull(battlefield);

        if (!engagement.IsActive)
            throw new InvalidOperationException(
                "Cannot create tactical state for an ended battle.");

        if (battlefield.Sectors.Count == 0)
            throw new InvalidOperationException(
                "Tactical battlefield must contain sectors.");

        Engagement = engagement;
        Battlefield = battlefield;

        Deployments = _deployments.AsReadOnly();
    }

    public CompanyDeployment DeployCompany(
        Company company,
        BattleSector sector,
        CompanyDeploymentRole role)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(sector);

        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        if (!Engagement.IsActive)
            throw new InvalidOperationException(
                "The engagement has already ended.");

        if (!Battlefield.ContainsSector(sector))
            throw new InvalidOperationException(
                "Sector is outside the tactical battlefield.");

        Battalion battalion = company.ParentBattalion
            ?? throw new InvalidOperationException(
                "Company is not assigned to a battalion.");

        Division division = battalion.ParentDivision
            ?? throw new InvalidOperationException(
                "Battalion is not assigned to a division.");

        if (!Engagement.Participants.Contains(division)
            || !ReferenceEquals(
                division.CurrentEngagement,
                Engagement))
        {
            throw new InvalidOperationException(
                "Company does not belong to this engagement.");
        }

        if (division.ResourceMode
            != DivisionResourceMode.Hierarchical)
        {
            throw new InvalidOperationException(
                "Tactical combat requires hierarchical resources.");
        }

        if (company.Composition is null)
            throw new InvalidOperationException(
                "Company has no physical composition.");

        if (_byCompany.ContainsKey(company))
            throw new InvalidOperationException(
                "Company is already deployed in this battle.");

        var deployment = new CompanyDeployment(
            company,
            sector,
            role);

        _byCompany.Add(company, deployment);
        _deployments.Add(deployment);

        return deployment;
    }

    public CompanyDeployment? FindDeployment(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        return _byCompany.TryGetValue(company, out var deployment)
            ? deployment
            : null;
    }

        /// <summary>
    /// Verifie que le deploiement exact appartient
    /// a cette bataille, sans parcourir la liste.
    /// </summary>
    internal bool ContainsDeployment(
        CompanyDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return _byCompany.TryGetValue(
                deployment.Company,
                out var registered)
            && ReferenceEquals(registered, deployment);
    }

    public IReadOnlyList<CompanyDeployment>
        GetBattalionDeployments(Battalion battalion)
    {
        ArgumentNullException.ThrowIfNull(battalion);

        return _deployments
            .Where(d =>
                ReferenceEquals(
                    d.Company.ParentBattalion,
                    battalion))
            .ToArray();
    }
}
