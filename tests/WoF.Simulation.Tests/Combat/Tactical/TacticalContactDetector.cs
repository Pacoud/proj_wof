
using WoF.Simulation.Diplomacy;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Relation géographique entre deux compagnies.
/// Elle ne garantit ni visibilité ni portée de tir.
/// </summary>
public enum TacticalContactTopology
{
    SameSector,
    ConnectedSectors
}

/// <summary>
/// Candidat à un contact tactique.
/// Aucun tir ni dommage n'est calculé ici.
/// </summary>
public sealed record TacticalContactCandidate(
    CompanyDeployment First,
    CompanyDeployment Second,
    TacticalContactTopology Topology,
    double? ConnectionDistanceMeters);

public static class TacticalContactDetector
{
    /// <summary>
    /// Identifie les paires de compagnies ennemies
    /// potentiellement en contact.
    ///
    /// Opération sans mutation de l'état militaire.
    /// </summary>
    public static IReadOnlyList<TacticalContactCandidate> Detect(
        TacticalBattle battle,
        DiplomacySystem diplomacy)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(diplomacy);

        if (!battle.Engagement.IsActive)
            return Array.Empty<TacticalContactCandidate>();

        // Index local : une seule traversée des déploiements.
        var bySector =
            new Dictionary<BattleSector, List<CompanyDeployment>>(
                ReferenceEqualityComparer.Instance);

        foreach (var sector in battle.Battlefield.Sectors)
            bySector.Add(sector, new List<CompanyDeployment>());

        foreach (var deployment in battle.Deployments)
        {
            if (!CanParticipate(deployment))
                continue;

            bySector[deployment.Sector].Add(deployment);
        }

        var contacts = new List<TacticalContactCandidate>();

        // 1. Candidats présents dans un même secteur.
        foreach (var sector in battle.Battlefield.Sectors
                     .OrderBy(s => s.Id))
        {
            var units = bySector[sector];

            // j > i empêche les doublons A-B / B-A.
            for (int i = 0; i < units.Count; i++)
            {
                for (int j = i + 1; j < units.Count; j++)
                {
                    TryAdd(
                        units[i],
                        units[j],
                        TacticalContactTopology.SameSector,
                        connectionDistance: null,
                        diplomacy,
                        contacts);
                }
            }
        }

        // 2. Candidats répartis de part et d'autre
        // d'une liaison entre deux secteurs.
        foreach (var connection in battle.Battlefield.Connections
                     .OrderBy(c => Math.Min(c.A.Id, c.B.Id))
                     .ThenBy(c => Math.Max(c.A.Id, c.B.Id)))
        {
            var firstSector = bySector[connection.A];
            var secondSector = bySector[connection.B];

            foreach (var first in firstSector)
            {
                foreach (var second in secondSector)
                {
                    TryAdd(
                        first,
                        second,
                        TacticalContactTopology.ConnectedSectors,
                        connection.DistanceMeters,
                        diplomacy,
                        contacts);
                }
            }
        }

        return contacts.AsReadOnly();
    }

    private static bool CanParticipate(
        CompanyDeployment deployment)
    {
        if (deployment.Role != CompanyDeploymentRole.FrontLine)
            return false;

        var composition = deployment.Company.Composition;

        return composition is not null
            && composition.Manpower.Current > 0;
    }

    private static void TryAdd(
        CompanyDeployment first,
        CompanyDeployment second,
        TacticalContactTopology topology,
        double? connectionDistance,
        DiplomacySystem diplomacy,
        List<TacticalContactCandidate> contacts)
    {
        var firstCountry =
            first.Company.ParentBattalion?.ParentDivision?.Country;

        var secondCountry =
            second.Company.ParentBattalion?.ParentDivision?.Country;

        if (firstCountry is null || secondCountry is null)
            return;

        if (!diplomacy.AreAtWar(firstCountry, secondCountry))
            return;

        contacts.Add(new TacticalContactCandidate(
            first,
            second,
            topology,
            connectionDistance));
    }
}
