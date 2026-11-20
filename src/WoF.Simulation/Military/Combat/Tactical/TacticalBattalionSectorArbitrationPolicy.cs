
namespace WoF.Simulation.Military.Combat.Tactical;

internal enum TacticalSectorArbitrationStatus
{
    Recommended,
    NoWeakSector,
    DeferredNoPublication,
    DeferredCaptureFailure,
    DeferredNoCurrentContact,
    DeferredAmbiguous,
    DeferredInvalidSnapshot
}

internal enum TacticalSectorEvidence
{
    Unknown = 0,
    HistoricalOnly = 1,
    CurrentSuspected = 2,
    CurrentLocalized = 3
}

/// <summary>
/// Recommandation consultative du QG.
///
/// SectorId n'est renseigne que lorsqu'une
/// recommandation unique a ete obtenue.
/// Aucun ordre militaire n'est cree.
/// </summary>
internal sealed record TacticalSectorArbitrationResult(
    long Hour,
    TacticalSectorArbitrationStatus Status,
    int? SectorId,
    TacticalBattalionSectorStatus? DefenseStatus,
    TacticalSectorEvidence? Evidence,
    IReadOnlyList<int> CandidateSectorIds,
    string Explanation);

/// <summary>
/// Première doctrine expérimentale d'arbitrage.
///
/// Pas de score numerique de menace.
/// Pas de fusion des contacts ennemis.
/// Pas d'acces aux objets militaires vivants.
/// </summary>
internal sealed class TacticalBattalionSectorArbitrationPolicy
{
    private readonly TacticalCommandSituationReader _reader;

    internal TacticalBattalionSectorArbitrationPolicy(
        TacticalCommandSituationReader reader)
    {
        _reader = reader
            ?? throw new ArgumentNullException(nameof(reader));
    }

    internal TacticalSectorArbitrationResult Evaluate(
        long expectedHour)
    {
        // Le lecteur effectue les controles 9m.2 :
        // cle, bataille active et heure courante.
        var read = _reader.Read(expectedHour);

        if (read is null)
        {
            return Result(
                expectedHour,
                TacticalSectorArbitrationStatus
                    .DeferredNoPublication,
                "Aucune publication valide pour cette heure.");
        }

        if (read.Failure is not null)
        {
            if (read.Snapshot is not null)
            {
                return Result(
                    expectedHour,
                    TacticalSectorArbitrationStatus
                        .DeferredInvalidSnapshot,
                    "Publication contradictoire.");
            }

            return Result(
                expectedHour,
                TacticalSectorArbitrationStatus
                    .DeferredCaptureFailure,
                "La capture de la situation du QG a echoue.");
        }

        var snapshot = read.Snapshot;

        if (snapshot is null
            || snapshot.Hour != expectedHour
            || !IsValid(snapshot))
        {
            return Result(
                expectedHour,
                TacticalSectorArbitrationStatus
                    .DeferredInvalidSnapshot,
                "La photographie du QG est incoherente.");
        }

        // Un secteur Capable peut recevoir des tirs,
        // mais cette premiere politique n'y recommande
        // pas de renfort sur le seul renseignement.
        var weak = snapshot.Sectors
            .Where(s => Severity(s.DefenseStatus) > 0)
            .ToArray();

        if (weak.Length == 0)
        {
            return Result(
                expectedHour,
                TacticalSectorArbitrationStatus.NoWeakSector,
                "Aucun secteur defensivement affaibli " +
                "dans le perimetre examine.");
        }

        // Conserver les secteurs qui ne sont domines
        // par aucun autre selon les trois criteres :
        // faiblesse, renseignement et priorite.
        var maximal = weak
            .Where(candidate =>
                !weak.Any(other =>
                    !ReferenceEquals(other, candidate)
                    && Dominates(other, candidate)))
            .ToArray();

        if (maximal.Length != 1)
        {
            return Result(
                expectedHour,
                TacticalSectorArbitrationStatus
                    .DeferredAmbiguous,
                "Plusieurs secteurs ne peuvent pas etre " +
                "departages sans hypothese supplementaire.",
                candidates: maximal.Select(s => s.SectorId));
        }

        var selected = maximal[0];
        var evidence = GetEvidence(selected);

        // Une ancienne observation ou une absence
        // d'observation ne suffit pas a declencher
        // une recommandation dans cette doctrine.
        if (evidence < TacticalSectorEvidence.CurrentSuspected)
        {
            return Result(
                expectedHour,
                TacticalSectorArbitrationStatus
                    .DeferredNoCurrentContact,
                "Le secteur ressort de l'evaluation " +
                "defensive, mais aucun contact actuel " +
                "n'y est signale.",
                candidates: new[] { selected.SectorId });
        }

        return Result(
            expectedHour,
            TacticalSectorArbitrationStatus.Recommended,
            "Le secteur domine les autres positions " +
            "affaiblies selon la gravite defensive, " +
            "le niveau de renseignement et la mission. " +
            "Cela ne predit ni les effectifs ennemis " +
            "ni le resultat du renfort.",
            sectorId: selected.SectorId,
            defenseStatus: selected.DefenseStatus,
            evidence: evidence,
            candidates: new[] { selected.SectorId });
    }

    private static int Severity(
        TacticalBattalionSectorStatus status)
    {
        return status switch
        {
            TacticalBattalionSectorStatus.Uncovered => 2,
            TacticalBattalionSectorStatus.UnableToHold => 2,
            TacticalBattalionSectorStatus.AtRisk => 1,
            TacticalBattalionSectorStatus.Capable => 0,
            _ => throw new InvalidOperationException(
                "Unknown defensive sector status.")
        };
    }

    private static TacticalSectorEvidence GetEvidence(
        TacticalBattalionCommandSectorReport sector)
    {
        // Les quantites ne servent qu'a verifier
        // la presence d'au moins un rapport.
        // 5 rapports ne signifient PAS 5 ennemis.
        if (sector.CurrentLocalizedReports > 0)
            return TacticalSectorEvidence.CurrentLocalized;

        if (sector.CurrentSuspectedReports > 0)
            return TacticalSectorEvidence.CurrentSuspected;

        if (sector.StaleReports > 0)
            return TacticalSectorEvidence.HistoricalOnly;

        return TacticalSectorEvidence.Unknown;
    }

    /// <summary>
    /// A domine B seulement si A n'est inferieur
    /// sur aucun des trois criteres et est
    /// strictement superieur sur au moins un.
    ///
    /// Aucune addition ou ponderation arbitraire.
    /// </summary>
    private static bool Dominates(
        TacticalBattalionCommandSectorReport a,
        TacticalBattalionCommandSectorReport b)
    {
        int aSeverity = Severity(a.DefenseStatus);
        int bSeverity = Severity(b.DefenseStatus);

        int aEvidence = (int)GetEvidence(a);
        int bEvidence = (int)GetEvidence(b);

        int aPriority = a.IsPrioritySector ? 1 : 0;
        int bPriority = b.IsPrioritySector ? 1 : 0;

        return aSeverity >= bSeverity
            && aEvidence >= bEvidence
            && aPriority >= bPriority
            && (aSeverity > bSeverity
                || aEvidence > bEvidence
                || aPriority > bPriority);
    }

    private static bool IsValid(
        TacticalBattalionCommandSituationSnapshot snapshot)
    {
        if (snapshot.BattalionId <= 0
            || string.IsNullOrWhiteSpace(snapshot.BattalionName)
            || string.IsNullOrWhiteSpace(
                snapshot.FriendlyDivisionName)
            || snapshot.Sectors is null
            || snapshot.Sectors.Count == 0
            || snapshot.OutsideLocalizedReports < 0
            || snapshot.OutsideSuspectedReports < 0
            || snapshot.OutsideStaleReports < 0)
        {
            return false;
        }

        var sectors = new HashSet<int>();
        int priorityCount = 0;

        foreach (var sector in snapshot.Sectors)
        {
            if (sector is null
                || sector.SectorId <= 0
                || !sectors.Add(sector.SectorId)
                || string.IsNullOrWhiteSpace(sector.SectorName)
                || !Enum.IsDefined(sector.DefenseStatus)
                || sector.StationaryFrontLineCompanies < 0
                || sector.AuthorizedFrontLinePersonnel < 0
                || sector.AvailableFrontLinePersonnel < 0
                || sector.AvailableFrontLinePersonnel
                    > sector.AuthorizedFrontLinePersonnel
                || !double.IsFinite(sector.FrontLineReadiness)
                || sector.FrontLineReadiness < 0
                || sector.FrontLineReadiness > 100
                || sector.CurrentLocalizedReports < 0
                || sector.CurrentSuspectedReports < 0
                || sector.StaleReports < 0)
            {
                return false;
            }

            if (sector.IsPrioritySector)
                priorityCount++;

            if (sector.DefenseStatus ==
                    TacticalBattalionSectorStatus.Uncovered
                && (sector.StationaryFrontLineCompanies != 0
                    || sector.AvailableFrontLinePersonnel != 0))
            {
                return false;
            }
        }

        return priorityCount <= 1;
    }

    private static TacticalSectorArbitrationResult Result(
        long hour,
        TacticalSectorArbitrationStatus status,
        string explanation,
        int? sectorId = null,
        TacticalBattalionSectorStatus? defenseStatus = null,
        TacticalSectorEvidence? evidence = null,
        IEnumerable<int>? candidates = null)
    {
        return new TacticalSectorArbitrationResult(
            hour,
            status,
            sectorId,
            defenseStatus,
            evidence,
            Array.AsReadOnly(
                (candidates ?? Array.Empty<int>())
                    .OrderBy(id => id)
                    .ToArray()),
            explanation);
    }
}
