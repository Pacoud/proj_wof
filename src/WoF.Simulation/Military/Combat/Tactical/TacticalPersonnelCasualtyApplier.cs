
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Pertes effectivement appliquees a une compagnie.
/// </summary>
internal sealed record TacticalCompanyCasualtyApplication(
    CompanyDeployment Target,
    PersonnelCasualtyReport Casualties,
    int AvailableBefore,
    int AvailableAfter);

/// <summary>
/// Compte rendu d'une phase de pertes appliquees.
/// </summary>
internal sealed record TacticalCasualtyPhaseResult(
    long Hour,
    IReadOnlyList<TacticalCompanyCasualtyApplication> Companies);

/// <summary>
/// Applique les pertes tactiques directement aux
/// PersonnelPool des compagnies.
///
/// Toutes les validations de ressources sont
/// effectuees avant la premiere mutation.
/// </summary>
internal static class TacticalPersonnelCasualtyApplier
{
    private readonly record struct PreparedApplication(
        PersonnelPool Pool,
        TacticalCompanyCasualtyApplication Report);

    internal static TacticalCasualtyPhaseResult Apply(
        TacticalBattle battle,
        TacticalPersonnelEffectsPhaseResult effects,
        IReadOnlyDictionary<CompanyDeployment, int>
            availableAtFire)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(availableAtFire);
        ArgumentNullException.ThrowIfNull(effects.Targets);

        if (effects.Hour < battle.Engagement.StartHour)
        {
            throw new InvalidOperationException(
                "Casualty phase precedes the engagement.");
        }

        var seen = new HashSet<CompanyDeployment>(
            ReferenceEqualityComparer.Instance);

        var prepared = new List<PreparedApplication>(
            effects.Targets.Count);

        // PASSE 1 : valider la phase entiere.
        // Aucune ressource militaire n'est modifiee.
        foreach (var effect in effects.Targets)
        {
            var target = effect.Target;

            if (target is null
                || !battle.ContainsDeployment(target))
            {
                throw new InvalidOperationException(
                    "Casualty target belongs to another battle.");
            }

            if (!seen.Add(target))
            {
                throw new InvalidOperationException(
                    "Duplicate company casualty report.");
            }

            if (!availableAtFire.TryGetValue(
                target, out int originalManpower))
            {
                throw new InvalidOperationException(
                    "No firing snapshot for casualty target.");
            }

            ValidateEffect(effect);

            if (effect.ExposedPersonnel > originalManpower)
            {
                throw new InvalidOperationException(
                    "Exposed personnel exceed original manpower.");
            }

            PersonnelPool pool =
                target.Company.Composition!.Manpower;

            // Si une autre source a modifie les hommes
            // depuis le tir, nous refusons d'appliquer
            // aveuglement des pertes supplementaires.
            if (pool.Current != originalManpower)
            {
                throw new InvalidOperationException(
                    "Company manpower changed since the fire phase.");
            }

            var casualties = new PersonnelCasualtyReport(
                KilledInAction: effect.Killed,
                WoundedInAction: effect.WoundedUnavailable,
                MissingOrCaptured: 0);

            int total = checked(
                effect.Killed + effect.WoundedUnavailable);

            if (total > pool.Current)
            {
                throw new InvalidOperationException(
                    "Casualties exceed available manpower.");
            }

            prepared.Add(new PreparedApplication(
                pool,
                new TacticalCompanyCasualtyApplication(
                    Target: target,
                    Casualties: casualties,
                    AvailableBefore: pool.Current,
                    AvailableAfter: pool.Current - total)));
        }

        // Preparer un resultat immuable avant toute
        // modification physique.
        var reports = Array.AsReadOnly(
            prepared.Select(p => p.Report).ToArray());

        var result = new TacticalCasualtyPhaseResult(
            Hour: effects.Hour,
            Companies: reports);

        // PASSE 2 : appliquer aux stocks existants.
        foreach (var item in prepared)
        {
            if (item.Report.Casualties.Total > 0)
            {
                item.Pool.ApplyCasualties(
                    item.Report.Casualties);
            }
        }

        return result;
    }

    private static void ValidateEffect(
        TacticalPersonnelEffectResult effect)
    {
        if (effect.ExposedPersonnel < 0
            || effect.ContactEvents < 0
            || effect.DistinctStruck < 0
            || effect.Killed < 0
            || effect.WoundedUnavailable < 0
            || effect.NoLastingIncapacity < 0)
        {
            throw new InvalidOperationException(
                "Negative personnel effect value.");
        }

        if (effect.DistinctStruck > effect.ExposedPersonnel
            || effect.DistinctStruck > effect.ContactEvents)
        {
            throw new InvalidOperationException(
                "Distinct struck exceed contacts or exposure.");
        }

        long categorized =
            (long)effect.Killed
            + effect.WoundedUnavailable
            + effect.NoLastingIncapacity;

        if (categorized != effect.DistinctStruck)
        {
            throw new InvalidOperationException(
                "Personnel effect categories do not balance.");
        }
    }
}
