
using WoF.Simulation.Military;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Losses;

// Bilan des pertes attribuées à une compagnie.
// Les identifiants sont relatifs à la division.
public sealed record CompanyCasualtyAllocation(
    int BattalionId,
    int CompanyId,
    PersonnelCasualtyReport Casualties
);

public static class HierarchicalPersonnelLossApplier
{
    private sealed class Target
    {
        public Battalion Battalion { get; }
        public Company Company { get; }
        public PersonnelPool Pool { get; }

        public int Remaining { get; set; }
        public int Killed { get; set; }
        public int Wounded { get; set; }
        public int Missing { get; set; }

        public Target(
            Battalion battalion,
            Company company,
            PersonnelPool pool)
        {
            Battalion = battalion;
            Company = company;
            Pool = pool;
            Remaining = pool.Current;
        }
    }

    public static IReadOnlyList<CompanyCasualtyAllocation> Apply(
        Division division,
        PersonnelCasualtyReport casualties)
    {
        ArgumentNullException.ThrowIfNull(division);
        ArgumentNullException.ThrowIfNull(casualties);

        if (casualties.KilledInAction < 0 ||
            casualties.WoundedInAction < 0 ||
            casualties.MissingOrCaptured < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(casualties));
        }

        // PHASE 1 : recenser les compagnies.
        // Aucun stock n'est modifié ici.
        var targets = new List<Target>();

        if (division.Battalions.Count == 0)
        {
            throw new InvalidOperationException(
                "Division has no battalions.");
        }

        foreach (var battalion in division.Battalions
                     .OrderBy(b => b.Id))
        {
            if (battalion.Companies.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Battalion {battalion.Name} has no companies.");
            }

            foreach (var company in battalion.Companies
                         .OrderBy(c => c.Id))
            {
                var composition = company.Composition
                    ?? throw new InvalidOperationException(
                        $"Company {company.Name} has no composition.");

                targets.Add(new Target(
                    battalion,
                    company,
                    composition.Manpower
                ));
            }
        }

        // PHASE 2 : valider la capacité globale.
        long requested =
            (long)casualties.KilledInAction +
            casualties.WoundedInAction +
            casualties.MissingOrCaptured;

        long available =
            targets.Sum(t => (long)t.Remaining);

        if (requested > available)
        {
            throw new InvalidOperationException(
                "Casualties exceed available personnel.");
        }

        if (requested == 0)
        {
            return Array.Empty<CompanyCasualtyAllocation>();
        }

        // PHASE 3 : planifier les allocations.
        // Les ressources physiques restent inchangées.
        Allocate(
            casualties.KilledInAction,
            targets,
            (t, amount) => t.Killed = amount);

        Allocate(
            casualties.WoundedInAction,
            targets,
            (t, amount) => t.Wounded = amount);

        Allocate(
            casualties.MissingOrCaptured,
            targets,
            (t, amount) => t.Missing = amount);

        // PHASE 4 : appliquer les pertes validées.
        var results = new List<CompanyCasualtyAllocation>();

        foreach (var target in targets)
        {
            var report = new PersonnelCasualtyReport(
                KilledInAction: target.Killed,
                WoundedInAction: target.Wounded,
                MissingOrCaptured: target.Missing
            );

            if (report.Total == 0)
                continue;

            target.Pool.ApplyCasualties(report);

            results.Add(new CompanyCasualtyAllocation(
                target.Battalion.Id,
                target.Company.Id,
                report
            ));
        }

        return results.AsReadOnly();
    }

    private static void Allocate(
        int amount,
        IReadOnlyList<Target> targets,
        Action<Target, int> assign)
    {
        if (amount == 0)
            return;

        long total =
            targets.Sum(t => (long)t.Remaining);

        var shares = new int[targets.Count];
        var remainders = new long[targets.Count];

        int allocated = 0;

        // Répartition proportionnelle entière.
        for (int i = 0; i < targets.Count; i++)
        {
            long numerator =
                (long)amount * targets[i].Remaining;

            shares[i] = (int)(numerator / total);
            remainders[i] = numerator % total;

            allocated += shares[i];
        }

        // Distribuer les unités restantes selon
        // les plus grands restes.
        int remaining = amount - allocated;

        var priority = Enumerable
            .Range(0, targets.Count)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i)
            .Take(remaining);

        foreach (int index in priority)
        {
            shares[index]++;
        }

        // Mettre à jour uniquement le plan local.
        for (int i = 0; i < targets.Count; i++)
        {
            assign(targets[i], shares[i]);
            targets[i].Remaining -= shares[i];
        }
    }
}
