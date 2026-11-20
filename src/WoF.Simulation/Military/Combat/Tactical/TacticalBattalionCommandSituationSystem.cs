
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Situation connue dans un secteur confie au bataillon.
/// Donnees propres amies et comptes de rapports ennemis.
/// Aucune reference vers un objet militaire vivant.
/// </summary>
internal sealed record TacticalBattalionCommandSectorReport(
    int SectorId,
    string SectorName,
    bool IsPrioritySector,
    TacticalBattalionSectorStatus DefenseStatus,
    int StationaryFrontLineCompanies,
    int AuthorizedFrontLinePersonnel,
    int AvailableFrontLinePersonnel,
    double FrontLineReadiness,
    int CurrentLocalizedReports,
    int CurrentSuspectedReports,
    int StaleReports);

/// <summary>
/// Photographie de fin d'heure destinée au commandement.
///
/// Les identifiants sont locaux au contexte de bataille.
/// Ne pas les utiliser comme des identifiants globaux.
/// </summary>
internal sealed record TacticalBattalionCommandSituationSnapshot(
    int BattalionId,
    string BattalionName,
    string FriendlyDivisionName,
    long Hour,
    long DeploymentRevision,
    IReadOnlyList<TacticalBattalionCommandSectorReport> Sectors,
    int OutsideLocalizedReports,
    int OutsideSuspectedReports,
    int OutsideStaleReports);

/// <summary>
/// Construit une situation tactique a partir
/// de l'orchestrateur ayant resolu l'heure.
///
/// N'utilise pas 9e ni 9g, dont les donnees
/// ennemies sont autoritaires et omniscientes.
/// </summary>
internal sealed class TacticalBattalionCommandSituationSystem
{
    private readonly TacticalHourlyCombatOrchestrator
        _orchestrator;

    private readonly TacticalBattle _battle;

    internal TacticalBattalionCommandSituationSystem(
        TacticalHourlyCombatOrchestrator orchestrator)
    {
        _orchestrator = orchestrator
            ?? throw new ArgumentNullException(
                nameof(orchestrator));

        _battle = orchestrator.Battle;
    }

    internal TacticalBattalionCommandSituationSnapshot Capture(
        Battalion battalion,
        IReadOnlyList<BattleSector> responsibilitySectors,
        long maxAgeHours)
    {
        ArgumentNullException.ThrowIfNull(battalion);
        ArgumentNullException.ThrowIfNull(
            responsibilitySectors);

        if (maxAgeHours < 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxAgeHours));

        // Exiger une heure tactique achevee.
        if (_orchestrator.LastResult
            is not TacticalHourlyCombatResult completed)
        {
            throw new InvalidOperationException(
                "No completed tactical hour.");
        }

        long hour = completed.Hour;

        var profile = _orchestrator.BattalionAssessmentProfile
            ?? throw new InvalidOperationException(
                "Battalion assessment is disabled.");

        var suppression =
            _battle.DirectFire.LastSuppressionPhase;

        if (_orchestrator.LastCohesionPhase?.Hour != hour
            || _orchestrator.LastBattalionReactionPhase?.Hour
                != hour
            || _orchestrator.Perception.LastUpdateHour != hour
            || suppression is null
            || suppression.Hour != hour)
        {
            throw new InvalidOperationException(
                "Tactical command picture requires " +
                "completed coherent hourly phases.");
        }

        // Protection 9f.1 : les deploiements ne
        // doivent pas avoir change apres suppression.
        if (suppression.DeploymentRevision
            != _battle.DeploymentRevision)
        {
            throw new InvalidOperationException(
                "Deployments changed after " +
                "the completed suppression phase.");
        }

        long revision = _battle.DeploymentRevision;

        // Etat de nos propres secteurs, y compris
        // ceux qui sont entierement decouverts.
        var continuity =
            new TacticalBattalionFrontContinuitySystem(
                _battle).Evaluate(
                    battalion,
                    profile,
                    responsibilitySectors);

        // Renseignement issu des seuls observateurs
        // de ce bataillon.
        var intelligence =
            new TacticalBattalionIntelligenceSystem(
                _battle,
                _orchestrator.Perception).Capture(
                    battalion,
                    hour,
                    maxAgeHours);

        int[] sectorIds = continuity.Sectors
            .Select(s => s.Sector.Id)
            .ToArray();

        // 9k organise les rapports par secteur.
        // Les champs de position et d'incertitude
        // ne sont pas utilises pour les decisions.
        var knowledge =
            TacticalBattalionSectorKnowledgeSystem
                .Evaluate(intelligence, sectorIds);

        if (knowledge.Hour != hour
            || knowledge.Sectors.Count
                != continuity.Sectors.Count)
        {
            throw new InvalidOperationException(
                "Inconsistent command situation inputs.");
        }

        var result =
            new List<TacticalBattalionCommandSectorReport>(
                continuity.Sectors.Count);

        for (int i = 0; i < continuity.Sectors.Count; i++)
        {
            var own = continuity.Sectors[i];
            var known = knowledge.Sectors[i];

            if (own.Sector.Id != known.SectorId)
            {
                throw new InvalidOperationException(
                    "Defensive and intelligence sectors differ.");
            }

            result.Add(
                new TacticalBattalionCommandSectorReport(
                    SectorId: own.Sector.Id,
                    SectorName: own.Sector.Name,
                    IsPrioritySector: own.IsPrioritySector,
                    DefenseStatus: own.Status,
                    StationaryFrontLineCompanies:
                        own.StationaryFrontLineCompanies,
                    AuthorizedFrontLinePersonnel:
                        own.AuthorizedFrontLinePersonnel,
                    AvailableFrontLinePersonnel:
                        own.AvailableFrontLinePersonnel,
                    FrontLineReadiness:
                        own.FrontLineReadiness,
                    CurrentLocalizedReports:
                        known.CurrentLocalizedReports,
                    CurrentSuspectedReports:
                        known.CurrentSuspectedReports,
                    StaleReports:
                        known.StaleReports));
        }

        int outsideLocated = 0;
        int outsideSuspected = 0;
        int outsideStale = 0;

        // Les rapports hors du front selectionne
        // sont conserves dans le bilan global.
        foreach (var outside in
            knowledge.OutsideSelectedSectors)
        {
            switch (outside.Source.Status)
            {
                case TacticalBattalionContactStatus
                    .CurrentLocalized:
                    outsideLocated++;
                    break;

                case TacticalBattalionContactStatus
                    .CurrentSuspected:
                    outsideSuspected++;
                    break;

                case TacticalBattalionContactStatus.Stale:
                    outsideStale++;
                    break;

                default:
                    throw new InvalidOperationException(
                        "Unknown intelligence status.");
            }
        }

        if (_battle.DeploymentRevision != revision)
        {
            throw new InvalidOperationException(
                "Deployments changed while capturing " +
                "the command situation.");
        }

        return new TacticalBattalionCommandSituationSnapshot(
            BattalionId: intelligence.BattalionId,
            BattalionName: intelligence.BattalionName,
            FriendlyDivisionName:
                intelligence.FriendlyDivisionName,
            Hour: hour,
            DeploymentRevision: revision,
            Sectors: Array.AsReadOnly(result.ToArray()),
            OutsideLocalizedReports: outsideLocated,
            OutsideSuspectedReports: outsideSuspected,
            OutsideStaleReports: outsideStale);
    }
}
