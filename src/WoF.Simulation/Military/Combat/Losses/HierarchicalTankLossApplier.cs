
using WoF.Simulation.Military;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Losses;

public sealed record CompanyTankLossAllocation(
    int BattalionId,
    int CompanyId,
    TankLossResult Losses
);

public sealed class HierarchicalTankLossApplier
{
    private sealed record Target(
        int BattalionId,
        int CompanyId,
        TankPool Pool
    );

    private readonly Division _division;

    // Restes fractionnaires de la division.
    private double _pendingDestroyed;
    private double _pendingDamaged;

    public HierarchicalTankLossApplier(Division division)
    {
        _division = division
            ?? throw new ArgumentNullException(
                nameof(division));
    }

    public IReadOnlyList<CompanyTankLossAllocation> Apply(
        TankLossExpectation expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        if (!double.IsFinite(expected.Damaged) ||
            !double.IsFinite(expected.Destroyed) ||
            expected.Damaged < 0 ||
            expected.Destroyed < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expected));
        }

        // PHASE 1 : recenser les stocks physiques.
        var targets = GetTargets();

        long totalOperational =
            targets.Sum(t => (long)t.Pool.Operational);

        if (totalOperational > int.MaxValue)
        {
            throw new InvalidOperationException(
                "Too many tanks for this loss model.");
        }

        int operational = (int)totalOperational;

        if (operational == 0)
        {
            _pendingDestroyed = 0;
            _pendingDamaged = 0;

            return Array.Empty<CompanyTankLossAllocation>();
        }

        // PHASE 2 : résoudre les fractions au
        // niveau de la division, une seule fois.
        double rawDestroyed =
            _pendingDestroyed + expected.Destroyed;

        double rawDamaged =
            _pendingDamaged + expected.Damaged;

        if (!double.IsFinite(rawDestroyed) ||
            !double.IsFinite(rawDamaged))
        {
            throw new InvalidOperationException(
                "Tank loss accumulation overflow.");
        }

        // Priorité aux destructions, comme dans
        // le TankPool existant.
        int destroyed = (int)Math.Min(
            Math.Floor(rawDestroyed),
            operational
        );

        int damaged = (int)Math.Min(
            Math.Floor(rawDamaged),
            operational - destroyed
        );

        // PHASE 3 : répartir les pertes entières
        // selon les chars opérationnels.
        int[] capacities = targets
            .Select(t => t.Pool.Operational)
            .ToArray();

        int[] destroyedShares =
            Distribute(destroyed, capacities);

        int[] remainingCapacities = capacities
            .Select((capacity, i) =>
                capacity - destroyedShares[i])
            .ToArray();

        int[] damagedShares =
            Distribute(damaged, remainingCapacities);

        // PHASE 4 : appliquer les pertes.
        var results =
            new List<CompanyTankLossAllocation>();

        for (int i = 0; i < targets.Count; i++)
        {
            var loss = new TankLossResult(
                Damaged: damagedShares[i],
                Destroyed: destroyedShares[i]
            );

            if (loss.Total == 0)
                continue;

            targets[i].Pool.ApplyResolvedLosses(loss);

            results.Add(new CompanyTankLossAllocation(
                targets[i].BattalionId,
                targets[i].CompanyId,
                loss
            ));
        }

        // PHASE 5 : conserver les fractions restantes.
        // Comme dans TankPool, les restes sont perdus
        // lorsque tous les chars sont indisponibles.
        if (operational - destroyed - damaged == 0)
        {
            _pendingDestroyed = 0;
            _pendingDamaged = 0;
        }
        else
        {
            _pendingDestroyed =
                Math.Max(0, rawDestroyed - destroyed);

            _pendingDamaged =
                Math.Max(0, rawDamaged - damaged);
        }

        return results.AsReadOnly();
    }

    private List<Target> GetTargets()
    {
        if (_division.Battalions.Count == 0)
        {
            throw new InvalidOperationException(
                "Division has no battalions.");
        }

        var targets = new List<Target>();

        foreach (var battalion in _division.Battalions
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
                        "Company has no composition.");

                targets.Add(new Target(
                    battalion.Id,
                    company.Id,
                    composition.Tanks
                ));
            }
        }

        return targets;
    }

    private static int[] Distribute(
        int amount,
        int[] capacities)
    {
        var shares = new int[capacities.Length];

        if (amount == 0)
            return shares;

        long total = capacities.Sum(x => (long)x);

        if (amount > total)
        {
            throw new InvalidOperationException(
                "Losses exceed available tanks.");
        }

        var remainders = new long[capacities.Length];
        int allocated = 0;

        for (int i = 0; i < capacities.Length; i++)
        {
            long numerator =
                (long)amount * capacities[i];

            shares[i] = (int)(numerator / total);
            remainders[i] = numerator % total;

            allocated += shares[i];
        }

        int remaining = amount - allocated;

        var priority = Enumerable
            .Range(0, capacities.Length)
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
