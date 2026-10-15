
using WoF.Simulation.Military;

namespace WoF.Simulation.Military.Combat.Losses;

public sealed record CompanyCasualtyAllocation(
    int BattalionId,
    int CompanyId,
    PersonnelCasualtyReport Casualties
);

public static class HierarchicalPersonnelLossApplier
{
    // API existante : retourne le détail par compagnie.
    public static IReadOnlyList<CompanyCasualtyAllocation> Apply(
        Division division,
        PersonnelCasualtyReport casualties)
    {
        return ApplyCore(
            division,
            casualties,
            includeReport: true);
    }

    // Nouveau chemin rapide : application sans
    // création de la liste des allocations détaillées.
    internal static void ApplyInPlace(
        Division division,
        PersonnelCasualtyReport casualties)
    {
        ApplyCore(
            division,
            casualties,
            includeReport: false);
    }

    private static IReadOnlyList<CompanyCasualtyAllocation>
        ApplyCore(
            Division division,
            PersonnelCasualtyReport casualties,
            bool includeReport)
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

        // En mode hiérarchique, l'index est réutilisé.
        // Avant activation, il est reconstruit pour
        // conserver les validations existantes.
        var targets = division.GetLossTargets();
        int count = targets.Length;

        long requested =
            (long)casualties.KilledInAction +
            casualties.WoundedInAction +
            casualties.MissingOrCaptured;

        long available = 0;

        foreach (var target in targets)
            available += target.Personnel.Current;

        if (requested > available)
        {
            throw new InvalidOperationException(
                "Casualties exceed available personnel.");
        }

        if (requested == 0)
            return Array.Empty<CompanyCasualtyAllocation>();

        // Un stockage sur la pile pour les organisations
        // ordinaires ; allocation managée de secours
        // pour les formations exceptionnellement grandes.
        const int StackLimit = 128;

        Span<int> remaining = count <= StackLimit
            ? stackalloc int[count]
            : new int[count];

        Span<int> killed = count <= StackLimit
            ? stackalloc int[count]
            : new int[count];

        Span<int> wounded = count <= StackLimit
            ? stackalloc int[count]
            : new int[count];

        Span<int> missing = count <= StackLimit
            ? stackalloc int[count]
            : new int[count];

        Span<int> shares = count <= StackLimit
            ? stackalloc int[count]
            : new int[count];

        Span<long> remainders = count <= StackLimit
            ? stackalloc long[count]
            : new long[count];

        // stackalloc ne garantit pas l'initialisation
        // à zéro : elle doit être explicite.
        killed.Clear();
        wounded.Clear();
        missing.Clear();

        for (int i = 0; i < count; i++)
            remaining[i] = targets[i].Personnel.Current;

        // Ordre historique des allocations :
        // KIA, puis WIA, puis MIA/POW.
        Allocate(
            casualties.KilledInAction,
            remaining, killed, shares, remainders);

        Allocate(
            casualties.WoundedInAction,
            remaining, wounded, shares, remainders);

        Allocate(
            casualties.MissingOrCaptured,
            remaining, missing, shares, remainders);

        // La liste n'est créée que pour l'API publique.
        List<CompanyCasualtyAllocation>? reports =
            includeReport
                ? new List<CompanyCasualtyAllocation>()
                : null;

        // Mutation des stocks seulement après avoir
        // terminé toutes les répartitions.
        for (int i = 0; i < count; i++)
        {
            int total = killed[i] + wounded[i] + missing[i];

            if (total == 0)
                continue;

            var report = new PersonnelCasualtyReport(
                KilledInAction: killed[i],
                WoundedInAction: wounded[i],
                MissingOrCaptured: missing[i]
            );

            targets[i].Personnel.ApplyCasualties(report);

            if (reports is not null)
            {
                reports.Add(new CompanyCasualtyAllocation(
                    targets[i].BattalionId,
                    targets[i].CompanyId,
                    report
                ));
            }
        }

        if (reports is not null)
            return reports.AsReadOnly();

        return Array.Empty<CompanyCasualtyAllocation>();
    }

    private static void Allocate(
        int amount,
        Span<int> remaining,
        Span<int> allocation,
        Span<int> shares,
        Span<long> remainders)
    {
        if (amount == 0)
            return;

        long total = 0;

        for (int i = 0; i < remaining.Length; i++)
            total += remaining[i];

        if (amount > total)
        {
            throw new InvalidOperationException(
                "Losses exceed remaining personnel.");
        }

        int allocated = 0;

        for (int i = 0; i < remaining.Length; i++)
        {
            long numerator =
                (long)amount * remaining[i];

            shares[i] = (int)(numerator / total);
            remainders[i] = numerator % total;

            allocated += shares[i];
        }

        int toDistribute = amount - allocated;

        // Plus grands restes, avec priorité
        // aux indices les plus petits en cas d'égalité.
        // Chaque indice reçoit au plus une unité.
        for (int r = 0; r < toDistribute; r++)
        {
            int bestIndex = -1;
            long bestRemainder = -1;

            for (int i = 0; i < remaining.Length; i++)
            {
                if (remainders[i] > bestRemainder)
                {
                    bestRemainder = remainders[i];
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                throw new InvalidOperationException(
                    "Invalid remainder allocation.");

            shares[bestIndex]++;
            remainders[bestIndex] = -1;
        }

        for (int i = 0; i < remaining.Length; i++)
        {
            allocation[i] = shares[i];
            remaining[i] -= shares[i];
        }
    }
}
