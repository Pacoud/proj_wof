
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Index spatial d'une bataille pour un etat
/// donne des deploiements et de la geometrie.
///
/// L'index est un instantane : reconstruire apres
/// modification des positions ou des secteurs.
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

    private readonly int _deploymentCount;
    private readonly int _positionCount;
    private readonly int _sectorCount;
    private readonly int _geometryCount;
    private readonly int _connectionCount;

    public TacticalSectorPresenceIndex(
        TacticalBattle battle,
        TacticalGeometryMap geometry)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));

        _geometry = geometry
            ?? throw new ArgumentNullException(nameof(geometry));

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
        // pendant la construction de l'index.
        var assigned = new Dictionary<
            BattleSector,
            List<CompanyDeployment>>(
                ReferenceEqualityComparer.Instance);

        var covering = new Dictionary<
            BattleSector,
            List<CompanyDeployment>>(
                ReferenceEqualityComparer.Instance);

        var neighbors = new Dictionary<
            BattleSector,
            List<BattleSector>>(
                ReferenceEqualityComparer.Instance);

        foreach (var sector in battlefield.Sectors)
        {
            assigned.Add(sector, new List<CompanyDeployment>());
            covering.Add(sector, new List<CompanyDeployment>());
            neighbors.Add(sector, new List<BattleSector>());
        }

        // Construire une fois les voisins directs.
        foreach (var connection in battlefield.Connections)
        {
            neighbors[connection.A].Add(connection.B);
            neighbors[connection.B].Add(connection.A);
        }

        foreach (var deployment in _battle.Deployments)
        {
            BattleSector primary = deployment.Sector;

            // Une affectation existe meme si la
            // compagnie n'a pas encore de position.
            assigned[primary].Add(deployment);

            var position = _geometry.GetPosition(deployment);

            if (position is null)
                continue;

            var footprint = new TacticalCompanyFootprint(position);

            _footprints.Add(deployment, footprint);

            // Le secteur principal reste toujours
            // couvert par sa compagnie positionnee.
            covering[primary].Add(deployment);

            // Ne tester que les voisins directs :
            // pas tous les secteurs de la bataille.
            foreach (var neighbor in neighbors[primary])
            {
                var sectorGeometry =
                    _geometry.GetSectorGeometry(neighbor);

                if (sectorGeometry is not null
                    && footprint.Intersects(sectorGeometry))
                {
                    covering[neighbor].Add(deployment);
                }
            }
        }

        // Exposer des vues non modifiables.
        foreach (var sector in battlefield.Sectors)
        {
            _assigned.Add(
                sector,
                Array.AsReadOnly(assigned[sector].ToArray()));

            _covering.Add(
                sector,
                Array.AsReadOnly(covering[sector].ToArray()));
        }
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

        return _assigned[sector];
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

        return _covering[sector];
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
            || battlefield.Connections.Count != _connectionCount)
        {
            throw new InvalidOperationException(
                "Tactical sector index is outdated. Rebuild it.");
        }
    }
}
