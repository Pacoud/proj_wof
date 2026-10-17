
using WoF.Simulation.Military;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

public enum TacticalAssignmentReason
{
    PriorityObjective,
    SecondaryObjective,
    DivisionReserve
}

public sealed record TacticalCompanyAssignment(
    Company Company,
    BattleSector Sector,
    CompanyDeploymentRole Role,
    TacticalAssignmentReason Reason);

public sealed class TacticalDeploymentPlan
{
    public TacticalBattle Battle { get; }

    public Division Division { get; }

    public DivisionTacticalOrder Order { get; }

    public IReadOnlyList<TacticalCompanyAssignment> Assignments
    {
        get;
    }

    internal TacticalDeploymentPlan(
        TacticalBattle battle,
        DivisionTacticalOrder order,
        IEnumerable<TacticalCompanyAssignment> assignments)
    {
        Battle = battle;
        Order = order;
        Division = order.Division;

        Assignments = assignments.ToList().AsReadOnly();
    }
}

public static class TacticalCommandSystem
{
    /// <summary>
    /// Prépare le déploiement d'une division sans
    /// modifier le champ de bataille.
    /// </summary>
    public static TacticalDeploymentPlan CreatePlan(
        TacticalBattle battle,
        DivisionTacticalOrder order)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(order);

        ValidateDivision(battle, order.Division);

        var sectors = new List<BattleSector>
        {
            order.PrioritySector
        };

        sectors.AddRange(order.SecondarySectors);

        var uniqueSectors = new HashSet<BattleSector>(
            ReferenceEqualityComparer.Instance);

        foreach (var sector in sectors)
        {
            if (!battle.Battlefield.ContainsSector(sector))
                throw new InvalidOperationException(
                    "An objective is outside this battlefield.");

            if (!uniqueSectors.Add(sector))
                throw new InvalidOperationException(
                    "A combat sector appears more than once.");
        }

        if (!battle.Battlefield.ContainsSector(
            order.ReserveSector))
        {
            throw new InvalidOperationException(
                "Reserve sector is outside this battlefield.");
        }

        var battalions = order.Division.Battalions
            .OrderBy(b => b.Id)
            .ToArray();

        if (battalions.Length == 0)
            throw new InvalidOperationException(
                "Division has no battalions.");

        if (order.ReserveBattalions >= battalions.Length)
            throw new InvalidOperationException(
                "At least one battalion must be deployed.");

        int combatBattalions =
            battalions.Length - order.ReserveBattalions;

        var assignments =
            new List<TacticalCompanyAssignment>();

        for (int i = 0; i < battalions.Length; i++)
        {
            bool wholeBattalionInReserve =
                i >= combatBattalions;

            BattleSector assignedSector =
                wholeBattalionInReserve
                    ? order.ReserveSector
                    : ChooseCombatSector(sectors, i);

            var companies = battalions[i].Companies
                .OrderBy(c => c.Id)
                .ToArray();

            if (companies.Length == 0)
                throw new InvalidOperationException(
                    "Battalion has no companies.");

            for (int j = 0; j < companies.Length; j++)
            {
                Company company = companies[j];

                if (company.Composition is null)
                    throw new InvalidOperationException(
                        "Company has no physical composition.");

                if (battle.FindDeployment(company) != null)
                    throw new InvalidOperationException(
                        "Company is already deployed.");

                CompanyDeploymentRole role =
                    wholeBattalionInReserve
                        ? CompanyDeploymentRole.Reserve
                        : ChooseCompanyRole(
                            order.Posture,
                            j);

                TacticalAssignmentReason reason =
                    wholeBattalionInReserve
                        ? TacticalAssignmentReason.DivisionReserve
                        : ReferenceEquals(assignedSector, order.PrioritySector)
                            ? TacticalAssignmentReason.PriorityObjective
                            : TacticalAssignmentReason.SecondaryObjective;

                assignments.Add(
                    new TacticalCompanyAssignment(
                        company,
                        assignedSector,
                        role,
                        reason));
            }
        }

        return new TacticalDeploymentPlan(
            battle,
            order,
            assignments);
    }

    /// <summary>
    /// Répartition simple et déterministe :
    /// chaque objectif reçoit un bataillon
    /// avant que le secteur prioritaire ne
    /// reçoive une affectation supplémentaire.
    /// </summary>
    private static BattleSector ChooseCombatSector(
        IReadOnlyList<BattleSector> sectors,
        int battalionIndex)
    {
        // Exemple avec trois secteurs :
        // A, B, C, A, A, B, C, A...
        //
        // A est le secteur prioritaire.
        int cycleLength = sectors.Count + 1;
        int position = battalionIndex % cycleLength;

        if (position == sectors.Count)
            position = 0;

        return sectors[position];
    }

    /// <summary>
    /// Doctrine provisoire de déploiement.
    /// La résolution réelle du combat viendra
    /// lors des itérations suivantes.
    /// </summary>
    private static CompanyDeploymentRole ChooseCompanyRole(
        TacticalPosture posture,
        int companyIndex)
    {
        if (posture == TacticalPosture.Attack)
        {
            if (companyIndex < 2)
                return CompanyDeploymentRole.FrontLine;

            if (companyIndex == 2)
                return CompanyDeploymentRole.Support;

            return CompanyDeploymentRole.Reserve;
        }

        if (companyIndex == 0)
            return CompanyDeploymentRole.FrontLine;

        if (companyIndex == 1)
            return CompanyDeploymentRole.Support;

        return CompanyDeploymentRole.Reserve;
    }

    /// <summary>
    /// Applique un plan préparé. Toutes les
    /// conditions connues sont vérifiées avant
    /// la première modification.
    ///
    /// Le moteur WoF est actuellement séquentiel.
    /// </summary>
    public static IReadOnlyList<CompanyDeployment> ApplyPlan(
        TacticalDeploymentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        TacticalBattle battle = plan.Battle;

        ValidateDivision(battle, plan.Division);

        if (battle.HasAppliedPlan(plan.Division))
            throw new InvalidOperationException(
                "An applied order already exists for this division.");

        var seen = new HashSet<Company>(
            ReferenceEqualityComparer.Instance);

        foreach (var assignment in plan.Assignments)
        {
            if (!seen.Add(assignment.Company))
                throw new InvalidOperationException(
                    "Duplicate company in deployment plan.");

            if (!battle.Battlefield.ContainsSector(
                assignment.Sector))
            {
                throw new InvalidOperationException(
                    "Invalid sector in deployment plan.");
            }

            if (!Enum.IsDefined(assignment.Role))
                throw new InvalidOperationException(
                    "Invalid company deployment role.");

            if (!ReferenceEquals(
                assignment.Company.ParentBattalion
                    ?.ParentDivision,
                plan.Division))
            {
                throw new InvalidOperationException(
                    "Company belongs to another division.");
            }

            if (assignment.Company.Composition is null)
                throw new InvalidOperationException(
                    "Company has no physical composition.");

            if (battle.FindDeployment(
                assignment.Company) != null)
            {
                throw new InvalidOperationException(
                    "A company was deployed after planning.");
            }
        }

        var created = new List<CompanyDeployment>(
            plan.Assignments.Count);

        foreach (var assignment in plan.Assignments)
        {
            created.Add(
                battle.DeployCompany(
                    assignment.Company,
                    assignment.Sector,
                    assignment.Role));
        }

        // The provenance is recorded only after the full plan is applied.
        battle.RecordAppliedPlan(plan);

        return created.AsReadOnly();
    }

    private static void ValidateDivision(
        TacticalBattle battle,
        Division division)
    {
        if (!battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "Engagement has already ended.");

        if (division.ResourceMode
            != DivisionResourceMode.Hierarchical)
        {
            throw new InvalidOperationException(
                "Hierarchical resources are required.");
        }

        if (!ReferenceEquals(
            division.CurrentEngagement,
            battle.Engagement))
        {
            throw new InvalidOperationException(
                "Division is not engaged in this battle.");
        }

        if (!battle.Engagement.Participants.Any(
            participant =>
                ReferenceEquals(participant, division)))
        {
            throw new InvalidOperationException(
                "Division is not a battle participant.");
        }
    }
}
