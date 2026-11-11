
namespace WoF.Simulation.Military.Combat.Tactical;

internal readonly record struct TacticalSpatialIndexStatistics(
    int IndexedCompanies,
    int OccupiedCells,
    int Queries,
    long ReturnedCandidates);

/// <summary>
/// Index dynamique des positions tactiques.
///
/// Chaque compagnie est stockee dans une seule cellule.
/// Les recherches de segment visitent uniquement
/// les cellules traversees et un voisinage de securite.
///
/// L'index est autoritaire, sans role de renseignement.
/// </summary>
internal sealed class TacticalCompanySpatialIndex
{
    private readonly record struct Cell(long X, long Y);

    private readonly TacticalBattle _battle;
    private readonly TacticalGeometryMap _geometry;
    private readonly double _cellSize;

    private readonly Dictionary<Cell, HashSet<CompanyDeployment>>
        _cells = new();

    private readonly Dictionary<CompanyDeployment, Cell>
        _locations = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<CompanyDeployment, int>
        _order = new(ReferenceEqualityComparer.Instance);

    private readonly int _deploymentCount;
    private readonly int _positionCount;

    private long _geometryRevision;
    private long _deploymentRevision;

    private int _queries;
    private long _returnedCandidates;

    internal TacticalSpatialIndexStatistics Statistics =>
        new(
            _locations.Count,
            _cells.Count,
            _queries,
            _returnedCandidates);

    internal TacticalCompanySpatialIndex(
        TacticalBattle battle,
        TacticalGeometryMap geometry,
        double cellSizeMeters)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));

        _geometry = geometry
            ?? throw new ArgumentNullException(nameof(geometry));

        if (!ReferenceEquals(geometry.Battle, battle))
        {
            throw new InvalidOperationException(
                "Spatial index belongs to another battle.");
        }

        if (!double.IsFinite(cellSizeMeters)
            || cellSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellSizeMeters));
        }

        _cellSize = cellSizeMeters;

        _deploymentCount = battle.Deployments.Count;
        _positionCount = geometry.CompanyPositions.Count;

        _geometryRevision = geometry.Revision;
        _deploymentRevision = battle.DeploymentRevision;

        for (int i = 0; i < battle.Deployments.Count; i++)
        {
            var deployment = battle.Deployments[i];
            _order.Add(deployment, i);

            var position = geometry.GetPosition(deployment);

            if (position is not null)
                Insert(deployment, ToCell(position.Point));
        }
    }

    /// <summary>
    /// Met a jour la cellule d'une compagnie apres
    /// une modification de sa position geometrique.
    /// </summary>
    internal void RefreshPosition(CompanyDeployment company)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (!_battle.ContainsDeployment(company))
            throw new InvalidOperationException(
                "Foreign company deployment.");

        // Une seule mutation geometrique et,
        // eventuellement, une notification de
        // deploiement depuis le dernier rafraichissement.
        if (_geometry.Revision < _geometryRevision
            || _geometry.Revision - _geometryRevision > 1
            || _battle.DeploymentRevision < _deploymentRevision
            || _battle.DeploymentRevision - _deploymentRevision > 1
            || _battle.Deployments.Count != _deploymentCount
            || _geometry.CompanyPositions.Count != _positionCount)
        {
            throw new InvalidOperationException(
                "Spatial index missed a geometry or deployment change.");
        }

        var position = _geometry.GetPosition(company)
            ?? throw new InvalidOperationException(
                "Company has no tactical position.");

        Cell next = ToCell(position.Point);

        if (_locations.TryGetValue(company, out Cell previous)
            && !previous.Equals(next))
        {
            Remove(company, previous);
        }

        if (!_locations.ContainsKey(company))
            Insert(company, next);

        _geometryRevision = _geometry.Revision;
        _deploymentRevision = _battle.DeploymentRevision;
    }

    /// <summary>
    /// Recherche conservative autour d'un segment.
    /// Le resultat contient des CANDIDATS, pas
    /// necessairement des contacts reels.
    /// </summary>
    internal IReadOnlyList<CompanyDeployment> QueryNearSegment(
        TacticalPoint start,
        TacticalPoint end,
        double radiusMeters)
    {
        EnsureCurrent();

        if (!double.IsFinite(radiusMeters)
            || radiusMeters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusMeters));
        }

        // Une cellule supplementaire protege les
        // passages proches des coins de la grille.
        double ringValue =
            Math.Ceiling(radiusMeters / _cellSize) + 1;

        if (ringValue > 16)
        {
            throw new InvalidOperationException(
                "Search radius is too large for this grid size.");
        }

        int ring = (int)ringValue;

        var visitedCells = new HashSet<Cell>();

        var candidates = new HashSet<CompanyDeployment>(
            ReferenceEqualityComparer.Instance);

        foreach (var crossed in TraverseSegment(start, end))
        {
            for (int dx = -ring; dx <= ring; dx++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                {
                    var cell = new Cell(
                        checked(crossed.X + dx),
                        checked(crossed.Y + dy));

                    if (!visitedCells.Add(cell))
                        continue;

                    if (_cells.TryGetValue(cell, out var occupants))
                    {
                        candidates.UnionWith(occupants);
                    }
                }
            }
        }

        // Conserver l'ordre historique des deploiements
        // afin de ne pas changer le choix du premier
        // contact si plusieurs candidats sont presents.
        var ordered = candidates
            .OrderBy(company => _order[company])
            .ToArray();

        _queries++;
        _returnedCandidates += ordered.Length;

        return Array.AsReadOnly(ordered);
    }

    private void Insert(
        CompanyDeployment company,
        Cell cell)
    {
        if (!_cells.TryGetValue(cell, out var occupants))
        {
            occupants = new HashSet<CompanyDeployment>(
                ReferenceEqualityComparer.Instance);

            _cells.Add(cell, occupants);
        }

        occupants.Add(company);
        _locations[company] = cell;
    }

    private void Remove(
        CompanyDeployment company,
        Cell cell)
    {
        if (!_cells.TryGetValue(cell, out var occupants)
            || !occupants.Remove(company))
        {
            throw new InvalidOperationException(
                "Spatial index is internally inconsistent.");
        }

        if (occupants.Count == 0)
            _cells.Remove(cell);

        _locations.Remove(company);
    }

    private Cell ToCell(TacticalPoint point) =>
        new(
            CellCoordinate(point.XMeters),
            CellCoordinate(point.YMeters));

    private long CellCoordinate(double meters)
    {
        double coordinate = Math.Floor(meters / _cellSize);

        // Limite explicite pour eviter des indices
        // numeriquement instables ou des debordements.
        if (!double.IsFinite(coordinate)
            || Math.Abs(coordinate) > 1_000_000_000_000.0)
        {
            throw new InvalidOperationException(
                "Spatial coordinate is outside supported bounds.");
        }

        return (long)coordinate;
    }

    /// <summary>
    /// Parcourt la ligne cellule par cellule.
    ///
    /// Contrairement au balayage de toute une AABB,
    /// le cout suit la longueur du segment.
    /// </summary>
    private IEnumerable<Cell> TraverseSegment(
        TacticalPoint start,
        TacticalPoint end)
    {
        Cell first = ToCell(start);
        Cell last = ToCell(end);

        long x = first.X;
        long y = first.Y;

        double dx = end.XMeters - start.XMeters;
        double dy = end.YMeters - start.YMeters;

        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);

        double nextBoundaryX = stepX > 0
            ? (x + 1) * _cellSize
            : x * _cellSize;

        double nextBoundaryY = stepY > 0
            ? (y + 1) * _cellSize
            : y * _cellSize;

        double tMaxX = stepX == 0
            ? double.PositiveInfinity
            : (nextBoundaryX - start.XMeters) / dx;

        double tMaxY = stepY == 0
            ? double.PositiveInfinity
            : (nextBoundaryY - start.YMeters) / dy;

        double tDeltaX = stepX == 0
            ? double.PositiveInfinity
            : _cellSize / Math.Abs(dx);

        double tDeltaY = stepY == 0
            ? double.PositiveInfinity
            : _cellSize / Math.Abs(dy);

        int visited = 0;

        while (true)
        {
            if (++visited > 100_000)
            {
                throw new InvalidOperationException(
                    "Segment crosses too many grid cells.");
            }

            yield return new Cell(x, y);

            if (x == last.X && y == last.Y)
                yield break;

            if (tMaxX < tMaxY)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY < tMaxX)
            {
                y += stepY;
                tMaxY += tDeltaY;
            }
            else
            {
                x += stepX;
                y += stepY;

                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
        }
    }

    private void EnsureCurrent()
    {
        if (_battle.Deployments.Count != _deploymentCount
            || _geometry.CompanyPositions.Count != _positionCount
            || _geometry.Revision != _geometryRevision
            || _battle.DeploymentRevision != _deploymentRevision)
        {
            throw new InvalidOperationException(
                "Spatial index is outdated. Refresh or rebuild it.");
        }
    }
}
