
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Resolution des personnes distinctes atteintes
/// puis des consequences individuelles agregees.
///
/// Aucune creation d'objet Soldier.
/// Aucune mutation de PersonnelPool.
/// </summary>
internal static class TacticalPersonnelEffectResolver
{
    /// <summary>
    /// Tire le nombre de personnes distinctes
    /// atteintes par K contacts independants
    /// uniformement distribues parmi N personnes.
    ///
    /// Echantillonnage exact pour ce modele,
    /// sans parcourir tous les contacts.
    /// </summary>
    internal static int SampleDistinct(
        int exposed,
        long contacts,
        ulong seed)
    {
        if (exposed < 0)
            throw new ArgumentOutOfRangeException(
                nameof(exposed));

        if (contacts < 0)
            throw new ArgumentOutOfRangeException(
                nameof(contacts));

        if (exposed == 0)
        {
            if (contacts > 0)
            {
                throw new InvalidOperationException(
                    "Contacts cannot exist without exposed personnel.");
            }

            return 0;
        }

        if (contacts == 0)
            return 0;

        int distinct = 0;
        long remainingContacts = contacts;

        // Nombre d'iterations limite par N,
        // pas par le nombre total de projectiles.
        while (remainingContacts > 0 && distinct < exposed)
        {
            double probabilityNew =
                (double)(exposed - distinct) / exposed;

            double repeatedBeforeNew;

            if (probabilityNew >= 1.0)
            {
                repeatedBeforeNew = 0;
            }
            else
            {
                // Nombre de contacts deja connus
                // avant le prochain nouvel individu.
                double u = TacticalImpactResolver.NextUniform(
                    ref seed);

                repeatedBeforeNew = Math.Floor(
                    Math.Log(u)
                    / Math.Log(1.0 - probabilityNew));
            }

            if (repeatedBeforeNew >= remainingContacts)
                break;

            // Le tirage vient de trouver un nouveau
            // combattant distinct.
            remainingContacts -=
                (long)repeatedBeforeNew + 1;

            distinct++;
        }

        return distinct;
    }

    /// <summary>
    /// Produit le bilan de consequence d'une compagnie.
    /// Une personne distincte recoit une seule categorie
    /// de consequence pendant cette phase.
    /// </summary>
    internal static TacticalPersonnelEffectResult Resolve(
        TacticalPersonnelTargetSummary summary,
        TacticalPersonnelEffectProfile profile,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (summary.Target is null)
        {
            throw new ArgumentException(
                "Missing target.", nameof(summary));
        }

        if (summary.ExposedPersonnel < 0
            || summary.ContactEvents < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(summary));
        }

        int distinct = SampleDistinct(
            summary.ExposedPersonnel,
            summary.ContactEvents,
            seed ^ 0xA0761D6478BD642FUL);

        // Premiere categorie : tues.
        int killed = checked((int)
            TacticalImpactResolver.SampleBinomial(
                distinct,
                profile.KilledProbability,
                seed ^ 0xE7037ED1A0B428DBUL));

        int remaining = distinct - killed;

        // Probabilite conditionnelle de blessure
        // parmi ceux qui n'ont pas ete tues.
        double conditionalWounded = remaining == 0
            ? 0.0
            : Math.Clamp(
                profile.WoundedUnavailableProbability
                / (1.0 - profile.KilledProbability),
                0.0,
                1.0);

        int wounded = checked((int)
            TacticalImpactResolver.SampleBinomial(
                remaining,
                conditionalWounded,
                seed ^ 0x8EBC6AF09C88C6E3UL));

        int noLastingIncapacity =
            distinct - killed - wounded;

        return new TacticalPersonnelEffectResult(
            Target: summary.Target,
            ExposedPersonnel: summary.ExposedPersonnel,
            ContactEvents: summary.ContactEvents,
            DistinctStruck: distinct,
            Killed: killed,
            WoundedUnavailable: wounded,
            NoLastingIncapacity: noLastingIncapacity);
    }
}
