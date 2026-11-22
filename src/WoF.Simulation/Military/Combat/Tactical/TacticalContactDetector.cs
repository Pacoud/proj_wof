
using WoF.Simulation.Diplomacy;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Relation géographique entre deux compagnies.
/// Elle ne garantit ni visibilité ni portée de tir.
/// </summary>
public enum TacticalContactTopology
{
    SameSector,
    ConnectedSectors,
    MobileEncounter,

    /// <summary>
    /// Secteurs distincts et non reliés, mais compagnies à portée de
    /// détection : leurs centres sont à moins de la portée maximale,
    /// augmentée de la demi-largeur du front de chacune.
    /// </summary>
    WithinDetectionRange
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
        DiplomacySystem diplomacy,
        long? currentHour = null)
    {
        var contacts = new List<TacticalContactCandidate>();
        VisitCandidates(battle, diplomacy, currentHour,
            (first, second, topology, distance) =>
                contacts.Add(new TacticalContactCandidate(first, second, topology, distance)));

        return battle.Engagement.IsActive
            ? contacts.AsReadOnly()
            : Array.Empty<TacticalContactCandidate>();
    }

    /// <summary>
    /// Variante qui ajoute aux paires topologiques les paires à portée
    /// de détection (voir VisitCandidates avec géométrie).
    /// Opération sans mutation de l'état militaire.
    /// </summary>
    internal static IReadOnlyList<TacticalContactCandidate> Detect(
        TacticalBattle battle,
        DiplomacySystem diplomacy,
        TacticalGeometryMap geometry,
        double detectionRangeMeters,
        long? currentHour = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var contacts = new List<TacticalContactCandidate>();
        VisitCandidates(battle, diplomacy, currentHour, geometry, detectionRangeMeters,
            (first, second, topology, distance) =>
                contacts.Add(new TacticalContactCandidate(first, second, topology, distance)));

        return battle.Engagement.IsActive
            ? contacts.AsReadOnly()
            : Array.Empty<TacticalContactCandidate>();
    }

    /// <summary>
    /// Paires topologiques seules : même secteur, secteurs reliés,
    /// contacts mobiles.
    /// </summary>
    internal static void VisitCandidates(
        TacticalBattle battle,
        DiplomacySystem diplomacy,
        long? currentHour,
        Action<CompanyDeployment, CompanyDeployment, TacticalContactTopology, double?> visitor)
    {
        VisitCandidates(
            battle,
            diplomacy,
            currentHour,
            geometry: null,
            detectionRangeMeters: 0,
            visitor);
    }

    /// <summary>
    /// Visite les paires candidates dans un ordre déterministe :
    /// 1. même secteur ;
    /// 2. secteurs reliés ;
    /// 3. si la géométrie est fournie, secteurs distincts et non reliés
    ///    dont les compagnies sont à portée de détection ;
    /// 4. contacts mobiles de l'heure.
    ///
    /// Cet ordre fixe la numérotation des pistes, donc la cible retenue
    /// au tir : les passages 1 et 2 reproduisent le parcours historique,
    /// et le passage 3 ne dépend que des identifiants de secteur et de
    /// l'ordre des déploiements.
    /// </summary>
    internal static void VisitCandidates(
        TacticalBattle battle,
        DiplomacySystem diplomacy,
        long? currentHour,
        TacticalGeometryMap? geometry,
        double detectionRangeMeters,
        Action<CompanyDeployment, CompanyDeployment, TacticalContactTopology, double?> visitor)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(visitor);

        if (geometry is not null)
        {
            if (!ReferenceEquals(geometry.Battle, battle))
            {
                throw new InvalidOperationException(
                    "Geometry belongs to another tactical battle.");
            }

            if (!double.IsFinite(detectionRangeMeters)
                || detectionRangeMeters <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(detectionRangeMeters));
            }
        }

        if (!battle.Engagement.IsActive)
            return;

        // Index local limité aux secteurs occupés :
        // une seule traversée des déploiements, aucun
        // parcours de tous les secteurs ni de toutes les liaisons.
        var bySector =
            new Dictionary<BattleSector, List<CompanyDeployment>>(
                ReferenceEqualityComparer.Instance);

        foreach (var deployment in battle.Deployments)
        {
            if (!CanParticipate(deployment))
                continue;

            if (!bySector.TryGetValue(deployment.Sector, out var units))
            {
                units = new List<CompanyDeployment>();
                bySector.Add(deployment.Sector, units);
            }

            units.Add(deployment);
        }

        // L'ordre de visite fixe l'ordre des observations, donc les
        // numéros de piste et la cible retenue par chaque tireur :
        // il reproduit exactement le parcours historique.
        var occupied = new BattleSector[bySector.Count];
        bySector.Keys.CopyTo(occupied, 0);
        Array.Sort(occupied, static (x, y) => x.Id.CompareTo(y.Id));

        // 1. Candidats présents dans un même secteur,
        // par identifiant de secteur croissant.
        foreach (var sector in occupied)
        {
            var units = bySector[sector];

            // j > i empêche les doublons A-B / B-A.
            for (int i = 0; i < units.Count; i++)
            {
                for (int j = i + 1; j < units.Count; j++)
                {
                    TryVisit(
                        units[i],
                        units[j],
                        TacticalContactTopology.SameSector,
                        connectionDistance: null,
                        diplomacy,
                        visitor);
                }
            }
        }

        // 2. Candidats répartis de part et d'autre
        // d'une liaison dont les deux secteurs sont occupés.
        // Chaque liaison est retenue une fois, depuis son extrémité A,
        // puis triée par identifiants (min, max) ; son orientation
        // A/B est conservée.
        var connections = new List<SectorConnection>();

        foreach (var sector in occupied)
        {
            foreach (var connection in battle.Battlefield.GetConnections(sector))
            {
                if (ReferenceEquals(connection.A, sector)
                    && bySector.ContainsKey(connection.B))
                {
                    connections.Add(connection);
                }
            }
        }

        connections.Sort(static (x, y) =>
        {
            int result = Math.Min(x.A.Id, x.B.Id)
                .CompareTo(Math.Min(y.A.Id, y.B.Id));

            return result != 0
                ? result
                : Math.Max(x.A.Id, x.B.Id)
                    .CompareTo(Math.Max(y.A.Id, y.B.Id));
        });

        foreach (var connection in connections)
        {
            var firstSector = bySector[connection.A];
            var secondSector = bySector[connection.B];

            foreach (var first in firstSector)
            {
                foreach (var second in secondSector)
                {
                    TryVisit(
                        first,
                        second,
                        TacticalContactTopology.ConnectedSectors,
                        connection.DistanceMeters,
                        diplomacy,
                        visitor);
                }
            }
        }

        // 3. Candidats à portée de détection, dans des secteurs
        // distincts et non reliés.
        if (geometry is not null)
        {
            VisitWithinRange(
                battle,
                geometry,
                detectionRangeMeters,
                diplomacy,
                visitor);
        }

        if (currentHour is long hour)
        {
            foreach (var mobile in battle.MobileEngagements.GetContacts(hour))
            {
                TryVisit(
                    mobile.First,
                    mobile.Second,
                    TacticalContactTopology.MobileEncounter,
                    connectionDistance: null,
                    diplomacy,
                    visitor);
            }
        }
    }

    private readonly record struct RangePair(
        CompanyDeployment First,
        int FirstOrder,
        CompanyDeployment Second,
        int SecondOrder);

    /// <summary>
    /// Paires de compagnies de première ligne, ennemies, dans des secteurs
    /// distincts et non reliés, dont les centres sont à moins de la portée
    /// de détection augmentée de la demi-largeur du front de chacune.
    ///
    /// L'index spatial ne fait que proposer des paires proches ; elles
    /// sont ensuite triées par identifiant de secteur (le plus petit, puis
    /// le plus grand), puis par ordre de déploiement. La première compagnie
    /// est celle du secteur d'identifiant le plus petit.
    /// </summary>
    private static void VisitWithinRange(
        TacticalBattle battle,
        TacticalGeometryMap geometry,
        double detectionRangeMeters,
        DiplomacySystem diplomacy,
        Action<CompanyDeployment, CompanyDeployment, TacticalContactTopology, double?> visitor)
    {
        // Rang de chaque compagnie admise dans battle.Deployments.
        var orders = new Dictionary<CompanyDeployment, int>(
            ReferenceEqualityComparer.Instance);

        double maxFrontage = 0;

        for (int i = 0; i < battle.Deployments.Count; i++)
        {
            var deployment = battle.Deployments[i];

            if (!CanParticipate(deployment))
                continue;

            var position = geometry.GetPosition(deployment);

            if (position is null)
                continue;

            orders.Add(deployment, i);
            maxFrontage = Math.Max(maxFrontage, position.FrontageMeters);
        }

        if (orders.Count < 2)
            return;

        // Aucune paire retenue ne dépasse ce rayon. La cellule est un peu
        // plus grande que lui : deux compagnies à portée sont alors dans
        // la même cellule ou dans deux cellules voisines, même quand les
        // arrondis tombent sur une limite de cellule.
        double searchRadius = detectionRangeMeters + maxFrontage;

        var index = new TacticalCompanySpatialIndex(
            battle,
            geometry,
            searchRadius * (1 + 1e-6));

        var battlefield = battle.Battlefield;
        var pairs = new List<RangePair>();

        index.VisitNearbyPairs(searchRadius, (a, b) =>
        {
            // Filtres du moins cher au plus cher : la plupart des paires
            // proposées sont dans un même secteur (déjà vues au passage 1)
            // ou dans un même camp, et ne coûtent aucune recherche.
            if (ReferenceEquals(a.Sector, b.Sector)
                || !CanParticipate(a)
                || !CanParticipate(b)
                || !AreHostile(a, b, diplomacy))
            {
                return;
            }

            var aPosition = geometry.GetPosition(a)!;
            var bPosition = geometry.GetPosition(b)!;

            double limit = detectionRangeMeters
                + aPosition.FrontageMeters / 2.0
                + bPosition.FrontageMeters / 2.0;

            if (aPosition.Point.HorizontalDistanceTo(bPosition.Point) > limit)
                return;

            // Le passage 2 a déjà examiné les secteurs reliés.
            if (battlefield.AreConnected(a.Sector, b.Sector))
                return;

            int aOrder = orders[a];
            int bOrder = orders[b];

            pairs.Add(a.Sector.Id < b.Sector.Id
                ? new RangePair(a, aOrder, b, bOrder)
                : new RangePair(b, bOrder, a, aOrder));
        });

        pairs.Sort(static (x, y) =>
        {
            int result = x.First.Sector.Id.CompareTo(y.First.Sector.Id);

            if (result == 0)
                result = x.Second.Sector.Id.CompareTo(y.Second.Sector.Id);

            if (result == 0)
                result = x.FirstOrder.CompareTo(y.FirstOrder);

            return result != 0
                ? result
                : x.SecondOrder.CompareTo(y.SecondOrder);
        });

        foreach (var pair in pairs)
        {
            visitor(
                pair.First,
                pair.Second,
                TacticalContactTopology.WithinDetectionRange,
                null);
        }
    }

    private static bool CanParticipate(
        CompanyDeployment deployment)
    {

        if (deployment.IsInTransit)
        return false;
        
        if (deployment.Role != CompanyDeploymentRole.FrontLine)
            return false;

        var composition = deployment.Company.Composition;

        return composition is not null
            && composition.Manpower.Current > 0;
    }

    private static void TryVisit(
        CompanyDeployment first,
        CompanyDeployment second,
        TacticalContactTopology topology,
        double? connectionDistance,
        DiplomacySystem diplomacy,
        Action<CompanyDeployment, CompanyDeployment, TacticalContactTopology, double?> visitor)
    {
        if (!AreHostile(first, second, diplomacy))
            return;

        visitor(first, second, topology, connectionDistance);
    }

    private static bool AreHostile(
        CompanyDeployment first,
        CompanyDeployment second,
        DiplomacySystem diplomacy)
    {
        var firstCountry =
            first.Company.ParentBattalion?.ParentDivision?.Country;

        var secondCountry =
            second.Company.ParentBattalion?.ParentDivision?.Country;

        return firstCountry is not null
            && secondCountry is not null
            && diplomacy.AreAtWar(firstCountry, secondCountry);
    }
}
