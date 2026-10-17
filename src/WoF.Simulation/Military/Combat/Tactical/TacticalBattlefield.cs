
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
}

public sealed class TacticalBattlefield
{
    private readonly List<BattleSector> _sectors = new();
    private readonly List<SectorConnection> _connections = new();

    private readonly Dictionary<int, BattleSector>
        _sectorsById = new();

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

        if (_connections.Any(c => c.Connects(a, b)))
            throw new InvalidOperationException(
                "Sector connection already exists.");

        _connections.Add(
            new SectorConnection(a, b, distanceMeters));
    }

    public bool AreConnected(BattleSector a, BattleSector b)
    {
        return _connections.Any(c => c.Connects(a, b));
    }

    public IReadOnlyList<BattleSector> GetNeighbors(
        BattleSector sector)
    {
        if (!ContainsSector(sector))
            throw new InvalidOperationException(
                "Sector does not belong to this battlefield.");

        return _connections
            .Where(c =>
                ReferenceEquals(c.A, sector)
                || ReferenceEquals(c.B, sector))
            .Select(c =>
                ReferenceEquals(c.A, sector) ? c.B : c.A)
            .ToArray();
    }
}
