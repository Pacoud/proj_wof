
namespace WoF.Simulation.Military.Combat.Tactical;

public sealed record SectorConnection(
    BattleSector A,
    BattleSector B,
    double DistanceMeters)
{
    public bool Connects(BattleSector first, BattleSector second)
    {
        return
            (ReferenceEquals(A, first)
             && ReferenceEquals(B, second))
            ||
            (ReferenceEquals(A, second)
             && ReferenceEquals(B, first));
    }

    /// <summary>Autre extrémité d'une liaison, vue depuis l'une des deux.</summary>
    internal BattleSector GetOther(BattleSector sector)
    {
        return ReferenceEquals(A, sector) ? B : A;
    }
}

/// <summary>
/// Graphe des secteurs d'une bataille.
///
/// Chaque secteur garde la liste de ses liaisons, dans l'ordre de leur
/// création : voisins, liaisons et recherche d'une liaison ne parcourent
/// plus toutes les liaisons de la bataille. La liste plate Connections
/// reste disponible, pour l'affichage et les instantanés.
/// </summary>
public sealed class TacticalBattlefield
{
    private readonly List<BattleSector> _sectors = new();
    private readonly List<SectorConnection> _connections = new();

    private readonly Dictionary<int, BattleSector>
        _sectorsById = new();

    private readonly Dictionary<
        BattleSector,
        List<SectorConnection>> _connectionsBySector =
            new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<BattleSector> Sectors { get; }

    public IReadOnlyList<SectorConnection> Connections { get; }

    public TacticalBattlefield()
    {
        Sectors = _sectors.AsReadOnly();
        Connections = _connections.AsReadOnly();
    }

    public void AddSector(BattleSector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);

        if (!_sectorsById.TryAdd(sector.Id, sector))
            throw new InvalidOperationException(
                "Sector ID already exists in this battlefield.");

        _sectors.Add(sector);
        _connectionsBySector.Add(sector, new List<SectorConnection>());
    }

    public bool ContainsSector(BattleSector sector)
    {
        if (sector is null)
            return false;

        return _sectorsById.TryGetValue(
            sector.Id,
            out var existing)
            && ReferenceEquals(existing, sector);
    }

    /// <summary>
    /// Verifie l'existence d'un secteur par son identifiant
    /// sans parcourir la liste des secteurs.
    /// </summary>
    internal bool ContainsSectorId(int sectorId)
    {
        return _sectorsById.ContainsKey(sectorId);
    }

    public void Connect(
        BattleSector a,
        BattleSector b,
        double distanceMeters)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (!ContainsSector(a) || !ContainsSector(b))
            throw new InvalidOperationException(
                "Both sectors must belong to this battlefield.");

        if (ReferenceEquals(a, b))
            throw new InvalidOperationException(
                "A sector cannot connect to itself.");

        if (!double.IsFinite(distanceMeters)
            || distanceMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distanceMeters));
        }

        if (TryGetConnection(a, b, out _))
            throw new InvalidOperationException(
                "Sector connection already exists.");

        var connection = new SectorConnection(a, b, distanceMeters);

        _connections.Add(connection);
        _connectionsBySector[a].Add(connection);
        _connectionsBySector[b].Add(connection);
    }

    public bool AreConnected(BattleSector a, BattleSector b)
    {
        return TryGetConnection(a, b, out _);
    }

    /// <summary>
    /// Voisins d'un secteur, dans l'ordre de création de leurs liaisons :
    /// le même ordre que le parcours de la liste plate Connections.
    /// </summary>
    public IReadOnlyList<BattleSector> GetNeighbors(
        BattleSector sector)
    {
        var connections = GetConnections(sector);
        var neighbors = new BattleSector[connections.Count];

        for (int i = 0; i < neighbors.Length; i++)
            neighbors[i] = connections[i].GetOther(sector);

        return neighbors;
    }

    /// <summary>
    /// Liaisons d'un secteur, dans l'ordre de leur création.
    /// Vue interne en lecture : ne pas la conserver au-delà de l'appel.
    /// </summary>
    internal IReadOnlyList<SectorConnection> GetConnections(
        BattleSector sector)
    {
        if (sector is null
            || !_connectionsBySector.TryGetValue(sector, out var connections))
        {
            throw new InvalidOperationException(
                "Sector does not belong to this battlefield.");
        }

        return connections;
    }

    /// <summary>
    /// Liaison entre deux secteurs, dans un sens ou dans l'autre.
    /// Faux pour un secteur absent, étranger à la bataille ou nul.
    /// </summary>
    internal bool TryGetConnection(
        BattleSector a,
        BattleSector b,
        out SectorConnection connection)
    {
        connection = null!;

        if (a is null
            || b is null
            || !_connectionsBySector.TryGetValue(a, out var fromA)
            || !_connectionsBySector.TryGetValue(b, out var fromB))
        {
            return false;
        }

        // Parcourir la plus courte des deux listes.
        var shorter = fromA.Count <= fromB.Count ? fromA : fromB;

        foreach (var candidate in shorter)
        {
            if (candidate.Connects(a, b))
            {
                connection = candidate;
                return true;
            }
        }

        return false;
    }
}
