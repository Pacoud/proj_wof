using System.Diagnostics.CodeAnalysis;
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>Prépare le déploiement de tous les participants à partir d'une grille déjà obtenue.</summary>
public static class EngagementBattleBuilder
{
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

    private static bool IsDeployable(Company company) => company.Composition?.Manpower.Current > 0;
}