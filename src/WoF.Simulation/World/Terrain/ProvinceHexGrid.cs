namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Grille hexagonale générée d'une province (index de simulation, invisible au joueur).
///
/// Cases pointe en haut, 200 m entre deux centres voisins, stockées en
/// rangées décalées (« odd-r ») : indice = rangée × Columns + colonne,
/// rangées numérotées du sud au nord, rangées impaires décalées de 100 m vers l'est.
/// La case (colonne 0, rangée 0) est centrée sur l'origine du repère local.
///
/// Les données sont des tableaux parallèles indexés par case, et non un objet par case.
/// La grille est en lecture seule hors du générateur.
/// </summary>
public sealed class ProvinceHexGrid
{
    public const double SpacingMeters = 200;

    /// <summary>Rayon circonscrit d'une case : 200 / √3.</summary>
    public const double CircumradiusMeters =
        SpacingMeters / 1.7320508075688772935;

    public const int StandardColumns = 125;
    public const int StandardRows = 144;

    private readonly float[] _elevation;
    private readonly byte[] _terrain;
    private readonly byte[] _features;
    private readonly byte[] _riverEdges;
    private readonly byte[] _bridgeEdges;
    private readonly int[] _borderNeighborSlot;

    private readonly List<BorderCrossing> _crossings = new();
    private readonly List<TerrainLocality> _localities = new();
    private readonly Dictionary<int, int[]> _borderCellsByNeighbor = new();

    public ProvinceTerrainDescriptor Descriptor { get; }

    public int Columns { get; }
    public int Rows { get; }
    public int CellCount { get; }

    public double FrameCenterXMeters { get; }
    public double FrameCenterYMeters { get; }

    public IReadOnlyList<BorderCrossing> BorderCrossings { get; }

    public IReadOnlyList<TerrainLocality> Localities { get; }

    internal ProvinceHexGrid(
        ProvinceTerrainDescriptor descriptor,
        int columns,
        int rows)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (columns < 2)
            throw new ArgumentOutOfRangeException(nameof(columns));

        if (rows < 2)
            throw new ArgumentOutOfRangeException(nameof(rows));

        Descriptor = descriptor;
        Columns = columns;
        Rows = rows;
        CellCount = columns * rows;

        _elevation = new float[CellCount];
        _terrain = new byte[CellCount];
        _features = new byte[CellCount];
        _riverEdges = new byte[CellCount];
        _bridgeEdges = new byte[CellCount];
        _borderNeighborSlot = new int[CellCount];
        Array.Fill(_borderNeighborSlot, -1);

        FrameCenterXMeters =
            ((columns - 1) * SpacingMeters + SpacingMeters / 2) / 2;

        FrameCenterYMeters =
            (rows - 1) * SpacingMeters * HexCoord.RowSpacingFactor / 2;

        BorderCrossings = _crossings.AsReadOnly();
        Localities = _localities.AsReadOnly();
    }

    // ---- Coordonnées ----

    public HexCoord GetCoord(int cell)
    {
        ValidateCell(cell);

        int row = cell / Columns;
        int column = cell % Columns;

        return new HexCoord(column - (row - (row & 1)) / 2, row);
    }

    public int GetColumn(int cell)
    {
        ValidateCell(cell);
        return cell % Columns;
    }

    public int GetRow(int cell)
    {
        ValidateCell(cell);
        return cell / Columns;
    }

    public bool TryGetIndex(HexCoord coord, out int cell)
    {
        int row = coord.R;
        int column = coord.Q + (row - (row & 1)) / 2;

        if (row < 0 || row >= Rows
            || column < 0 || column >= Columns)
        {
            cell = -1;
            return false;
        }

        cell = row * Columns + column;
        return true;
    }

    /// <summary>Indice de la case voisine, ou -1 hors du cadre.</summary>
    public int GetNeighborIndex(int cell, int direction)
    {
        return TryGetIndex(
            GetCoord(cell).Neighbor(direction),
            out int neighbor)
                ? neighbor
                : -1;
    }

    public double GetCenterXMeters(int cell)
    {
        return GetCoord(cell).CenterXMeters(SpacingMeters);
    }

    public double GetCenterYMeters(int cell)
    {
        return GetCoord(cell).CenterYMeters(SpacingMeters);
    }

    /// <summary>Une case du bord a au moins un voisin hors du cadre.</summary>
    public bool IsFrameBoundary(int cell)
    {
        for (int d = 0; d < HexCoord.DirectionCount; d++)
        {
            if (GetNeighborIndex(cell, d) < 0)
                return true;
        }

        return false;
    }

    // ---- Contenu des cases ----

    public float GetElevation(int cell)
    {
        ValidateCell(cell);
        return _elevation[cell];
    }

    public TerrainType GetTerrain(int cell)
    {
        ValidateCell(cell);
        return (TerrainType)_terrain[cell];
    }

    public TerrainCellFeature GetFeatures(int cell)
    {
        ValidateCell(cell);
        return (TerrainCellFeature)_features[cell];
    }

    public bool IsRiverEdge(int cell, int direction)
    {
        ValidateCell(cell);
        return (_riverEdges[cell] & DirectionBit(direction)) != 0;
    }

    public bool HasBridge(int cell, int direction)
    {
        ValidateCell(cell);
        return (_bridgeEdges[cell] & DirectionBit(direction)) != 0;
    }

    public bool HasAnyRiverEdge(int cell)
    {
        ValidateCell(cell);
        return _riverEdges[cell] != 0;
    }

    /// <summary>
    /// Vrai si la case voisine existe et qu'aucune rivière ne les sépare,
    /// ou qu'un pont la franchit.
    /// </summary>
    public bool CanCross(int cell, int direction)
    {
        if (GetNeighborIndex(cell, direction) < 0)
            return false;

        return !IsRiverEdge(cell, direction)
            || HasBridge(cell, direction);
    }

    // ---- Frontières ----

    /// <summary>Cases du bord du cadre attribuées à une province voisine.</summary>
    public IReadOnlyList<int> GetBorderCells(int neighborProvinceId)
    {
        return _borderCellsByNeighbor.TryGetValue(
            neighborProvinceId,
            out var cells)
                ? cells
                : Array.Empty<int>();
    }

    public bool TryGetBorderNeighbor(int cell, out int neighborProvinceId)
    {
        ValidateCell(cell);

        int slot = _borderNeighborSlot[cell];

        if (slot < 0)
        {
            neighborProvinceId = 0;
            return false;
        }

        neighborProvinceId =
            Descriptor.Neighbors[slot].NeighborProvinceId;

        return true;
    }

    // ---- Accès bruts, pour les comparaisons de déterminisme ----

    internal ReadOnlySpan<float> ElevationData => _elevation;
    internal ReadOnlySpan<byte> TerrainData => _terrain;
    internal ReadOnlySpan<byte> FeatureData => _features;
    internal ReadOnlySpan<byte> RiverEdgeData => _riverEdges;
    internal ReadOnlySpan<byte> BridgeEdgeData => _bridgeEdges;

    // ---- Écriture, réservée au générateur ----

    internal void SetElevation(int cell, float elevation)
    {
        _elevation[cell] = elevation;
    }

    internal void SetTerrain(int cell, TerrainType terrain)
    {
        _terrain[cell] = (byte)terrain;
    }

    internal void AddFeature(int cell, TerrainCellFeature feature)
    {
        _features[cell] |= (byte)feature;
    }

    internal void RemoveFeature(int cell, TerrainCellFeature feature)
    {
        _features[cell] &= (byte)~feature;
    }

    /// <summary>
    /// Marque le bord entre une case (éventuellement hors du cadre)
    /// et sa voisine dans une direction, des deux côtés.
    /// </summary>
    internal void SetRiverEdge(HexCoord coord, int direction)
    {
        int opposite = HexCoord.Opposite(direction);

        if (TryGetIndex(coord, out int cell))
            _riverEdges[cell] |= DirectionBit(direction);

        if (TryGetIndex(coord.Neighbor(direction), out int neighbor))
            _riverEdges[neighbor] |= DirectionBit(opposite);
    }

    /// <summary>Pose un pont sur un bord de rivière, des deux côtés.</summary>
    internal void SetBridge(int cell, int direction)
    {
        int neighbor = GetNeighborIndex(cell, direction);

        if (neighbor < 0 || !IsRiverEdge(cell, direction))
            throw new InvalidOperationException(
                "A bridge must span a river edge between two cells.");

        _bridgeEdges[cell] |= DirectionBit(direction);
        _bridgeEdges[neighbor] |=
            DirectionBit(HexCoord.Opposite(direction));
    }

    internal void AddCrossing(BorderCrossing crossing)
    {
        _crossings.Add(crossing);
    }

    internal void AddLocality(TerrainLocality locality)
    {
        _localities.Add(locality);
    }

    internal void SetBorderCells(int neighborSlot, int[] cells)
    {
        int neighborId =
            Descriptor.Neighbors[neighborSlot].NeighborProvinceId;

        _borderCellsByNeighbor[neighborId] = cells;

        foreach (int cell in cells)
            _borderNeighborSlot[cell] = neighborSlot;
    }

    private static byte DirectionBit(int direction)
    {
        if (direction < 0 || direction >= HexCoord.DirectionCount)
            throw new ArgumentOutOfRangeException(nameof(direction));

        return (byte)(1 << direction);
    }

    private void ValidateCell(int cell)
    {
        if (cell < 0 || cell >= CellCount)
            throw new ArgumentOutOfRangeException(nameof(cell));
    }
}
