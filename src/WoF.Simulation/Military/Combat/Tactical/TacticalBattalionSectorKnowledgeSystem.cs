
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Rapport de contact affecte au secteur signale.
/// Ne represente pas une unite ennemie unique.
/// </summary>
internal sealed record TacticalBattalionSectorContactEntry(
    int ReportedSectorId,
    TacticalBattalionContactReport Source);

/// <summary>
/// Bilan des rapports disponibles pour un secteur.
/// Les compteurs concernent les pistes, pas les ennemis.
/// </summary>
internal sealed record TacticalBattalionSectorKnowledgeReport(
    int SectorId,
    int CurrentLocalizedReports,
    int CurrentSuspectedReports,
    int StaleReports,
    IReadOnlyList<TacticalBattalionSectorContactEntry> Contacts);

/// <summary>
/// Photographie sectorielle sans reference
/// aux objets militaires vivants.
/// </summary>
internal sealed record TacticalBattalionSectorKnowledgeSnapshot(
    int BattalionId,
    string BattalionName,
    string FriendlyDivisionName,
    long Hour,
    IReadOnlyList<TacticalBattalionSectorKnowledgeReport> Sectors,
    IReadOnlyList<TacticalBattalionSectorContactEntry>
        OutsideSelectedSectors);

/// <summary>
/// Repartit les renseignements du QG par secteur
/// a partir d'un snapshot deja constitue.
///
/// Aucun acces a l'etat reel de l'ennemi.
/// Aucun nouveau calcul de detection.
/// Aucun effet sur le combat ou le commandement.
/// </summary>
internal static class TacticalBattalionSectorKnowledgeSystem
{
    internal static TacticalBattalionSectorKnowledgeSnapshot
        Evaluate(
            TacticalBattalionIntelligenceSnapshot intelligence,
            IReadOnlyList<int> selectedSectorIds)
    {
        ArgumentNullException.ThrowIfNull(intelligence);
        ArgumentNullException.ThrowIfNull(selectedSectorIds);
        ArgumentNullException.ThrowIfNull(intelligence.Contacts);

        var groups = new Dictionary<
            int,
            List<TacticalBattalionSectorContactEntry>>();

        // Garder exactement l'ordre des secteurs
        // fourni par le commandement appelant.
        foreach (int sectorId in selectedSectorIds)
        {
            if (sectorId <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(selectedSectorIds));

            if (!groups.TryAdd(
                sectorId,
                new List<TacticalBattalionSectorContactEntry>()))
            {
                throw new InvalidOperationException(
                    "Duplicated selected sector.");
            }
        }

        var outside =
            new List<TacticalBattalionSectorContactEntry>();

        // Une piste est identifiee par l'observateur
        // et son TrackId, jamais par une cible reelle.
        var seenTracks = new HashSet<(int Observer, int Track)>();

        foreach (var source in intelligence.Contacts)
        {
            if (source is null || source.Contact is null)
                throw new InvalidOperationException(
                    "Invalid intelligence contact.");

            var contact = source.Contact;

            if (source.ObserverCompanyId <= 0
                || contact.TrackId <= 0
                || contact.LastObservedHour > intelligence.Hour
                || source.AgeHours < 0
                || source.AgeHours !=
                    intelligence.Hour - contact.LastObservedHour
                || !seenTracks.Add((
                    source.ObserverCompanyId,
                    contact.TrackId)))
            {
                throw new InvalidOperationException(
                    "Inconsistent intelligence track.");
            }

            int reportedSectorId = ResolveReportedSector(
                source, intelligence.Hour);

            var entry =
                new TacticalBattalionSectorContactEntry(
                    reportedSectorId,
                    source);

            if (groups.TryGetValue(
                reportedSectorId, out var reports))
            {
                reports.Add(entry);
            }
            else
            {
                // Les observations hors du perimetre
                // selectionne ne doivent pas disparaitre.
                outside.Add(entry);
            }
        }

        var sectors =
            new List<TacticalBattalionSectorKnowledgeReport>(
                selectedSectorIds.Count);

        foreach (int sectorId in selectedSectorIds)
        {
            var reports = groups[sectorId];

            sectors.Add(
                new TacticalBattalionSectorKnowledgeReport(
                    SectorId: sectorId,
                    CurrentLocalizedReports: reports.Count(r =>
                        r.Source.Status ==
                        TacticalBattalionContactStatus
                            .CurrentLocalized),
                    CurrentSuspectedReports: reports.Count(r =>
                        r.Source.Status ==
                        TacticalBattalionContactStatus
                            .CurrentSuspected),
                    StaleReports: reports.Count(r =>
                        r.Source.Status ==
                        TacticalBattalionContactStatus.Stale),
                    Contacts:
                        Array.AsReadOnly(reports.ToArray())));
        }

        return new TacticalBattalionSectorKnowledgeSnapshot(
            intelligence.BattalionId,
            intelligence.BattalionName,
            intelligence.FriendlyDivisionName,
            intelligence.Hour,
            Array.AsReadOnly(sectors.ToArray()),
            Array.AsReadOnly(outside.ToArray()));
    }

    /// <summary>
    /// Secteur rapporte selon la nature de la piste.
    ///
    /// Une suspicion actuelle utilise le secteur
    /// actuellement signale, pas une ancienne
    /// position localisee.
    ///
    /// Une piste perimee utilise son ancien secteur
    /// memorise, uniquement comme reference historique.
    /// </summary>
 
    private static int ResolveReportedSector(
        TacticalBattalionContactReport source,
        long hour)
    {
        var contact = source.Contact;

        // Une localisation ne peut pas avoir ete
        // obtenue apres la derniere observation.
        if (contact.LastLocatedHour is long locatedHour
            && locatedHour > contact.LastObservedHour)
        {
            throw new InvalidOperationException(
                "Localization cannot be newer than " +
                "the last observation.");
        }

        switch (source.Status)
        {
            case TacticalBattalionContactStatus.CurrentLocalized:
            {
                if (!contact.IsCurrentlyObserved
                    || contact.LastObservedHour != hour

                    // Exiger explicitement Located ou
                    // Identified : null est interdit.
                    || contact.CurrentObservationLevel is not (
                        TacticalKnowledgeLevel.Located or
                        TacticalKnowledgeLevel.Identified)

                    || contact.LastLocatedHour != hour
                    || contact.LastKnownPosition is null
                    || contact.CurrentObservedSectorId
                        is not int sectorId
                    || sectorId <= 0
                    || contact.LastKnownSectorId != sectorId)
                {
                    throw new InvalidOperationException(
                        "Invalid current localization.");
                }

                return sectorId;
            }

            case TacticalBattalionContactStatus.CurrentSuspected:
            {
                if (!contact.IsCurrentlyObserved
                    || contact.LastObservedHour != hour
                    || contact.CurrentObservationLevel
                        != TacticalKnowledgeLevel.Suspected
                    || contact.CurrentObservedSectorId
                        is not int sectorId
                    || sectorId <= 0)
                {
                    throw new InvalidOperationException(
                        "Invalid current suspicion.");
                }

                return sectorId;
            }

            case TacticalBattalionContactStatus.Stale:
            {
                if (contact.IsCurrentlyObserved

                    // Une piste perdue ne doit plus
                    // porter d'observation actuelle.
                    || contact.CurrentObservationLevel is not null
                    || contact.CurrentObservedSectorId is not null

                    || contact.LastKnownSectorId <= 0)
                {
                    throw new InvalidOperationException(
                        "Invalid stale contact.");
                }

                // Reference historique uniquement.
                return contact.LastKnownSectorId;
            }

            default:
                throw new InvalidOperationException(
                    "Unknown intelligence contact status.");
        }
    }

}
