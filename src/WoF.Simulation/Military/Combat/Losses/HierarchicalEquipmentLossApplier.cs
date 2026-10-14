
using WoF.Simulation.Military;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Losses;

public sealed record CompanyEquipmentLossAllocation(
    int BattalionId,
    int CompanyId,
    EquipmentLossReport Losses
);

public static class HierarchicalEquipmentLossApplier
{
    private sealed record Target(
        int BattalionId,
        int CompanyId,
        CompanyComposition Composition
    );

    public static IReadOnlyList<CompanyEquipmentLossAllocation> Apply(
        Division division,
        EquipmentLossReport losses)
    {
        ArgumentNullException.ThrowIfNull(division);

        if (losses.InfantryEquipment < 0 ||
            losses.Artillery < 0 ||
            losses.Trucks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(losses));
        }

        // PHASE 1 :
        // Recenser les compagnies.
        var targets = GetTargets(division);

        // PHASE 2 :
        // Préparer toutes les répartitions.
        // Aucune ressource n'est modifiée.
        int[] infantryShares = Distribute(
            losses.InfantryEquipment,
            targets,
            t => t.Composition.InfantryEquipment.Current
        );

        int[] artilleryShares = Distribute(
            losses.Artillery,
            targets,
            t => t.Composition.Artillery.Current
        );

        int[] truckShares = Distribute(
            losses.Trucks,
            targets,
            t => t.Composition.Trucks.Current
        );

        // PHASE 3 :
        // Toutes les demandes sont valides.
        // Nous pouvons maintenant appliquer les pertes.
        var results =
            new List<CompanyEquipmentLossAllocation>();

        for (int i = 0; i < targets.Count; i++)
        {
            var composition = targets[i].Composition;

            var allocation = new EquipmentLossReport(
                infantryShares[i],
                artilleryShares[i],
                truckShares[i]
            );

            if (allocation == default)
                continue;

            composition.InfantryEquipment.ApplyLoss(
                allocation.InfantryEquipment);

            composition.Artillery.ApplyLoss(
                allocation.Artillery);

            composition.Trucks.ApplyLoss(
                allocation.Trucks);

            results.Add(new CompanyEquipmentLossAllocation(
                targets[i].BattalionId,
                targets[i].CompanyId,
                allocation
            ));
        }

        return results.AsReadOnly();
    }

    private static List<Target> GetTargets(
        Division division)
    {
        if (division.Battalions.Count == 0)
        {
            throw new InvalidOperationException(
                "Division has no battalions.");
        }

        var targets = new List<Target>();

        foreach (var battalion in division.Battalions
                     .OrderBy(b => b.Id))
        {
            if (battalion.Companies.Count == 0)
            {
                throw new InvalidOperationException(
                    "Battalion has no companies.");
            }

            foreach (var company in battalion.Companies
                         .OrderBy(c => c.Id))
            {
                var composition = company.Composition
                    ?? throw new InvalidOperationException(
                        $"Company {company.Name} has no composition.");

                targets.Add(new Target(
                    battalion.Id,
                    company.Id,
                    composition
                ));
            }
        }

        return targets;
    }

    private static int[] Distribute(
        int requested,
        IReadOnlyList<Target> targets,
        Func<Target, int> getAvailable)
    {
        var shares = new int[targets.Count];

        if (requested == 0)
            return shares;

        long available = targets.Sum(
            t => (long)getAvailable(t));

        // Refuser les pertes impossibles.
        // Ne pas silencieusement les réduire.
        if (requested > available)
        {
            throw new InvalidOperationException(
                "Equipment losses exceed available stock.");
        }

        var remainders = new long[targets.Count];

        int allocated = 0;

        // Répartition proportionnelle.
        for (int i = 0; i < targets.Count; i++)
        {
            long numerator =
                (long)requested * getAvailable(targets[i]);

            shares[i] = (int)(numerator / available);

            remainders[i] = numerator % available;

            allocated += shares[i];
        }

        // Répartition des unités restantes
        // selon les plus grands restes.
        int remaining = requested - allocated;

        var priority = Enumerable
            .Range(0, targets.Count)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i)
            .Take(remaining);

        foreach (int index in priority)
        {
            shares[index]++;
        }

        return shares;
    }
}
