
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Index spatial d'une bataille pour un etat
/// donne des deploiements et de la geometrie.
///
/// L'index est un instantane : reconstruire apres
/// modification des positions ou des secteurs.
///
/// Seuls les secteurs occupes ou couverts sont stockes :
/// la construction depend du nombre de compagnies,
/// pas du nombre de secteurs de la bataille.
/// </summary>
public sealed class TacticalSectorPresenceIndex
{
    private readonly TacticalBattle _battle;
    private readonly TacticalGeometryMap _geometry;

    private readonly Dictionary<
        BattleSector,
        IReadOnlyList<CompanyDeployment>> _assigned =
            new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<
        BattleSector,
        IReadOnlyList<CompanyDeployment>> _covering =
            new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<
        CompanyDeployment,
        TacticalCompanyFootprint> _footprints =
            new(ReferenceEqualityComparer.Instance);

    private static readonly IReadOnlyList<CompanyDeployment> Empty =
        Array.AsReadOnly(Array.Empty<CompanyDeployment>());

    private readonly int _deploymentCount;
    private readonly int _positionCount;
    private readonly int _sectorCount;
    private readonly int _geometryCount;
    private readonly int _connectionCount;
    private readonly long _deploymentRevision;
    private readonly long _geometryRevision;

    public TacticalSectorPresenceIndex(
        TacticalBattle battle,
        TacticalGeometryMap geometry)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));

        _geometry = geometry
            ?? throw new ArgumentNullException(nameof(geometry));

        
        _deploymentRevision = battle.DeploymentRevision;
        _geometryRevision = geometry.Revision;

        if (!ReferenceEquals(_geometry.Battle, _battle))
        {
            throw new InvalidOperationException(
                "Geometry belongs to another tactical battle.");
        }

        var battlefield = _battle.Battlefield;

        _deploymentCount = _battle.Deployments.Count;
        _positionCount = _geometry.CompanyPositions.Count;
        _sectorCount = battlefield.Sectors.Count;
        _geometryCount = _geometry.SectorGeometries.Count;
        _connectionCount = battlefield.Connections.Count;

        // Tables temporaires utilisees uniquement
        // pendant la construction de l'index, limitees
        // aux secteurs occupes ou couverts.
        var assigned = new Dictionary<
            BattleSector,
            List<CompanyDeployment>>(
                ReferenceEqualityComparer.Instance);

        var covering = new Dictionary<
            BattleSector,
            List<CompanyDeployment>>(
                ReferenceEqualityComparer.Instance);

        foreach (var deployment in _battle.Deployments)
        {
            BattleSector primary = deployment.Sector;

            // Une affectation existe meme si la
            // compagnie n'a pas encore de position.
            Add(assigned, primary, deployment);

            var position = _geometry.GetPosition(deployment);

            if (position is null)
                continue;

            var footprint = new TacticalCompanyFootprint(position);

            _footprints.Add(deployment, footprint);

            var primaryGeometry =
                _geometry.GetSectorGeometry(primary);

            if (!deployment.IsInTransit
                || (primaryGeometry is not null
                    && primaryGeometry.Contains(position.Point)))
            {
                Add(covering, primary, deployment);
            }

            // Ne tester que les voisins directs, lus dans
            // l'index du champ de bataille.
            foreach (var connection in battlefield.GetConnections(primary))
            {
                var neighbor = connection.GetOther(primary);

                var sectorGeometry =
                    _geometry.GetSectorGeometry(neighbor);

                if (sectorGeometry is not null
                    && footprint.Intersects(sectorGeometry))
                {
                    Add(covering, neighbor, deployment);
                }
            }
        }

        // Exposer des vues non modifiables.
        foreach (var (sector, list) in assigned)
            _assigned.Add(sector, Array.AsReadOnly(list.ToArray()));

        foreach (var (sector, list) in covering)
            _covering.Add(sector, Array.AsReadOnly(list.ToArray()));
    }

    private static void Add(
        Dictionary<BattleSector, List<CompanyDeployment>> table,
        BattleSector sector,
        CompanyDeployment deployment)
    {
        if (!table.TryGetValue(sector, out var list))
        {
            list = new List<CompanyDeployment>();
            table.Add(sector, list);
        }

        list.Add(deployment);
    }

    /// <summary>
    /// Compagnies affectees principalement au secteur.
    /// Peut inclure des compagnies non positionnees.
    /// </summary>
    public IReadOnlyList<CompanyDeployment> GetAssigned(
        BattleSector sector)
    {
        EnsureCurrent();
        ValidateSector(sector);

        return _assigned.TryGetValue(sector, out var list)
            ? list
            : Empty;
    }

    /// <summary>
    /// Compagnies positionnees dont l'emprise
    /// simplifiee couvre ce secteur.
    /// </summary>
    public IReadOnlyList<CompanyDeployment> GetCovering(
        BattleSector sector)
    {
        EnsureCurrent();
        ValidateSector(sector);

        return _covering.TryGetValue(sector, out var list)
            ? list
            : Empty;
    }

    /// <summary>
    /// Indicateur brut de concentration.
    /// Ce n'est pas une capacite maximale du secteur.
    /// </summary>
    public int GetCoverageCount(BattleSector sector)
    {
        return GetCovering(sector).Count;
    }

    public TacticalCompanyFootprint? GetFootprint(
        CompanyDeployment deployment)
    {
        EnsureCurrent();

        if (!_battle.ContainsDeployment(deployment))
        {
            throw new InvalidOperationException(
                "Deployment does not belong to this battle.");
        }

        return _footprints.TryGetValue(
            deployment,
            out var footprint)
                ? footprint
                : null;
    }

    private void ValidateSector(BattleSector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);

        if (!_battle.Battlefield.ContainsSector(sector))
        {
            throw new InvalidOperationException(
                "Sector does not belong to this battle.");
        }
    }

    private void EnsureCurrent()
    {
        var battlefield = _battle.Battlefield;

        if (_battle.Deployments.Count != _deploymentCount
            || _geometry.CompanyPositions.Count != _positionCount
            || battlefield.Sectors.Count != _sectorCount
            || _geometry.SectorGeometries.Count != _geometryCount
            || battlefield.Connections.Count != _connectionCount
            || _battle.DeploymentRevision != _deploymentRevision
            || _geometry.Revision != _geometryRevision)
        {
            throw new InvalidOperationException(
                "Tactical sector index is outdated. Rebuild it.");
        }
    }
}
