
using WoF.Simulation.Military;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Losses;

// Références physiques stables : les stocks restent mutables.
internal readonly record struct HierarchicalLossTarget(
    int BattalionId,
    int CompanyId,
    PersonnelPool Personnel,
    TankPool Tanks
);

internal static class HierarchicalLossTargetIndex
{
    public static HierarchicalLossTarget[] Build(
        Division division)
    {
        ArgumentNullException.ThrowIfNull(division);

        if (division.Battalions.Count == 0)
            throw new InvalidOperationException(
                "Division has no battalions.");

        var targets = new List<HierarchicalLossTarget>();

        foreach (var battalion in division.Battalions
                     .OrderBy(b => b.Id))
        {
            if (battalion.Companies.Count == 0)
                throw new InvalidOperationException(
                    "Battalion has no companies.");

            foreach (var company in battalion.Companies
                         .OrderBy(c => c.Id))
            {
                var composition = company.Composition
                    ?? throw new InvalidOperationException(
                        "Company has no composition.");

                targets.Add(new HierarchicalLossTarget(
                    battalion.Id,
                    company.Id,
                    composition.Manpower,
                    composition.Tanks
                ));
            }
        }

        return targets.ToArray();
    }
}
