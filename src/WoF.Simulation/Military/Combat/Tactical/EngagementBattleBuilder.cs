using System.Diagnostics.CodeAnalysis;
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>État propre à une construction. Le layout conserve la grille de province partagée.</summary>
public sealed record EngagementBattleBuildResult(
    TacticalBattle Battle,
    ProvinceBattlefieldLayout Layout,
    TacticalGeometryMap Map,
    TacticalSignatureFrame Signatures);

/// <summary>Prépare le déploiement de tous les participants à partir d'une grille déjà obtenue.</summary>
public static class EngagementBattleBuilder
{
    // Réglages provisoires repris du benchmark de densité, pas de données historiques.
    // Le minimum d'un exposé à la construction étend la règle aux très petits effectifs.
    private const int InitialExposedDivisor = 4;
    private const int MinimumInitialExposed = 1;
    private const int InitialSheltered = 0;

    public static bool TryBuild(
        ProvinceBattlefieldGrid field,
        Engagement engagement,
        [NotNullWhen(true)] out EngagementBattlePreparation? preparation,
        out EngagementBattleRefusal refusal,
        DiplomacySystem? diplomacy = null)
    {
        ArgumentNullException.ThrowIfNull(engagement);
        ArgumentNullException.ThrowIfNull(field);
        preparation = null;

        // Préconditions de TacticalBattle et DeployCompany. Un engagement terminé ou une
        // appartenance incohérente signale un appel invalide du moteur.
        if (!engagement.IsActive)
            throw new InvalidOperationException("Cannot prepare an ended engagement.");

        if (engagement.Participants.Any(d => !ReferenceEquals(d.CurrentEngagement, engagement)))
            throw new InvalidOperationException("A participant belongs to another engagement.");

        // Le calcul de 3c vérifie aussi l'identité de la province. Ses refus gardent leur sens.
        if (!EngagementDeploymentZones.TryCompute(
            field, engagement, out var zones, out var zoneRefusal, diplomacy: diplomacy))
        {
            refusal = zoneRefusal switch
            {
                DeploymentZoneRefusal.MissingSide => EngagementBattleRefusal.MissingSide,
                DeploymentZoneRefusal.NoEntryBorder => EngagementBattleRefusal.NoEntryBorder,
                DeploymentZoneRefusal.BattlefieldTooShallow => EngagementBattleRefusal.BattlefieldTooShallow,
                DeploymentZoneRefusal.ParticipantWithoutSide => EngagementBattleRefusal.ParticipantWithoutSide,
                _ => throw new InvalidOperationException($"Unexpected zone refusal: {zoneRefusal}."),
            };
            return false;
        }

        // Vérification division par division, avant toute construction de bataille.
        foreach (var division in engagement.Participants)
        {
            if (division.ResourceMode != DivisionResourceMode.Hierarchical)
            {
                refusal = EngagementBattleRefusal.LegacyResources;
                return false;
            }

            if (!division.Battalions.SelectMany(b => b.Companies).Any(IsDeployable))
            {
                refusal = EngagementBattleRefusal.NoCompanies;
                return false;
            }
        }

        // Conserver le rôle, la case et l'ordre calculés en 3d. La place d'une compagnie
        // vide reste inoccupée ; personne ne change de rôle à cette étape.
        var plan = EngagementDeploymentPlan.Compute(zones!, engagement);
        var placement = EngagementCompanyPlacement.Compute(plan);
        preparation = new EngagementBattlePreparation(engagement, zones!,
            placement.Companies.Where(p => IsDeployable(p.Company)));
        refusal = EngagementBattleRefusal.None;
        return true;
    }

    /// <summary>Construit une bataille indépendante avec les décisions figées en 3e.1a.</summary>
    public static EngagementBattleBuildResult BuildPrepared(EngagementBattlePreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        // Les opérations tactiques sont séquentielles. Vérifier toute la préparation
        // avant de créer le premier secteur ou de déployer la première compagnie.
        preparation.EnsureCurrent();
        var field = preparation.Field;
        var layout = TacticalBattlefieldBuilder.Build(field.Grid, field.Partition, preparation.BattlefieldSectorIds);
        var battle = new TacticalBattle(preparation.Engagement, layout.Battlefield);
        var map = new TacticalGeometryMap(battle);
        layout.DefineGeometry(map);
        var signatures = new TacticalSignatureFrame(battle);

        foreach (var p in preparation.Companies)
        {
            var deployment = battle.DeployCompany(p.Company, layout.GetSector(p.Cell), p.Role);
            map.PlaceCompany(deployment, p.Point, p.FacingDegrees, p.FrontageMeters);
            signatures.Set(deployment, new(TacticalActivity.Stationary, TacticalConcealment.None));

            // La préparation retient les effectifs positifs, réserves comprises.
            int current = p.Company.Composition!.Manpower.Current;
            int exposed = Math.Max(MinimumInitialExposed, current / InitialExposedDivisor);
            battle.PersonnelExposure.Set(deployment, new TacticalPersonnelExposure(
                exposed, InitialSheltered, current - exposed - InitialSheltered, p.FrontageMeters));
        }

        return new EngagementBattleBuildResult(battle, layout, map, signatures);
    }

    private static bool IsDeployable(Company company) => company.Composition?.Manpower.Current > 0;
}