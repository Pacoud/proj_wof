
using WoF.Simulation.Military;

namespace WoF.Simulation.Military.Combat.Losses;

public sealed record CompanyTankLossAllocation(
    int BattalionId,
    int CompanyId,
    TankLossResult Losses
);

public sealed class HierarchicalTankLossApplier
{
    private readonly Division _division;

    private double _pendingDestroyed;
    private double _pendingDamaged;

    public HierarchicalTankLossApplier(Division division)
    {
        _division = division
            ?? throw new ArgumentNullException(nameof(division));
    }

    public IReadOnlyList<CompanyTankLossAllocation> Apply(
        TankLossExpectation expected)
    {
        return ApplyCore(expected, includeReport: true).Allocations;
    }

    internal TankLossResult ApplyToPools(
        TankLossExpectation expected)
    {
        return ApplyCore(expected, includeReport: false).Result;
    }

    private (
        TankLossResult Result,
        IReadOnlyList<CompanyTankLossAllocation> Allocations
    ) ApplyCore(
        TankLossExpectation expected,
        bool includeReport)
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

        // Les références sont réutilisées lorsque
        // la hiérarchie est activée.
        var targets = _division.GetLossTargets();

        long totalOperational = 0;

        foreach (var target in targets)
            totalOperational += target.Tanks.Operational;

        if (totalOperational > int.MaxValue)
        {
            throw new InvalidOperationException(
                "Too many tanks for this loss model.");
        }

        int operational = (int)totalOperational;

        // Chemin rapide : aucun char disponible.
        // On conserve la règle d'effacement
        // des fractions accumulées.
        if (operational == 0)
        {
            _pendingDestroyed = 0;
            _pendingDamaged = 0;

            return (
                new TankLossResult(0, 0),
                Array.Empty<CompanyTankLossAllocation>()
            );
        }

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

        // Les destructions sont prioritaires.
        int destroyed = (int)Math.Min(
            Math.Floor(rawDestroyed),
            operational
        );

        int damaged = (int)Math.Min(
            Math.Floor(rawDamaged),
            operational - destroyed
        );

        // Les fractions restent stockées au niveau
        // divisionnaire tant qu'il reste des chars.
        if (destroyed == 0 && damaged == 0)
        {
            _pendingDestroyed = rawDestroyed;
            _pendingDamaged = rawDamaged;

            return (
                new TankLossResult(0, 0),
                Array.Empty<CompanyTankLossAllocation>()
            );
        }

        int count = targets.Length;

        // Ces tableaux ne sont désormais créés
        // que si au moins un char est réellement touché.
        var capacities = new int[count];

        for (int i = 0; i < count; i++)
            capacities[i] = targets[i].Tanks.Operational;

        int[] destroyedShares =
            Distribute(destroyed, capacities);

        var remainingCapacities = new int[count];

        for (int i = 0; i < count; i++)
        {
            remainingCapacities[i] =
                capacities[i] - destroyedShares[i];
        }

        int[] damagedShares =
            Distribute(damaged, remainingCapacities);

        List<CompanyTankLossAllocation>? reports =
            includeReport
                ? new List<CompanyTankLossAllocation>()
                : null;

        for (int i = 0; i < count; i++)
        {
            int companyDamaged = damagedShares[i];
            int companyDestroyed = destroyedShares[i];

            if (companyDamaged == 0 && companyDestroyed == 0)
                continue;

            var loss = new TankLossResult(
                Damaged: companyDamaged,
                Destroyed: companyDestroyed
            );

            targets[i].Tanks.ApplyResolvedLosses(loss);

            if (reports is not null)
            {
                reports.Add(new CompanyTankLossAllocation(
                    targets[i].BattalionId,
                    targets[i].CompanyId,
                    loss
                ));
            }
        }

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

        var result = new TankLossResult(
            Damaged: damaged,
            Destroyed: destroyed
        );

        if (reports is not null)
            return (result, reports.AsReadOnly());

        return (
            result,
            Array.Empty<CompanyTankLossAllocation>()
        );
    }

    // Algorithme de répartition historique conservé.
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
            shares[index]++;

        return shares;
    }
}
