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

    // ---- Positions continues ----

    /// <summary>
    /// Case dont l'hexagone contient le point, ou false si le point
    /// tombe hors du cadre.
    /// </summary>
    public bool TryGetCellAt(double xMeters, double yMeters, out int cell)
    {
        if (!IsNearFrame(xMeters, yMeters))
        {
            cell = -1;
            return false;
        }

        return TryGetIndex(
            HexCoord.FromMeters(xMeters, yMeters, SpacingMeters),
            out cell);
    }

    /// <summary>
    /// Altitude du sol en un point quelconque.
    ///
    /// Les centres des cases forment un réseau de triangles équilatéraux :
    /// l'altitude est interpolée linéairement entre les trois centres du
    /// triangle qui contient le point. Elle vaut exactement l'altitude de
    /// la case en son centre et suit exactement une pente régulière.
    ///
    /// Au bord du cadre, seuls les centres existants comptent, avec des
    /// poids renormalisés. Faux si aucun des trois centres n'existe.
    /// </summary>
    public bool TryGetGroundElevation(
        double xMeters,
        double yMeters,
        out double elevationMeters)
    {
        elevationMeters = 0;

        if (!IsNearFrame(xMeters, yMeters))
            return false;

        var (q, r) = HexCoord.FractionalFromMeters(
            xMeters, yMeters, SpacingMeters);

        double baseQ = Math.Floor(q);
        double baseR = Math.Floor(r);
        double fq = q - baseQ;
        double fr = r - baseR;

        int q0 = (int)baseQ;
        int r0 = (int)baseR;

        // Le losange (q0, r0) – (q0 + 1, r0 + 1) est coupé par sa petite
        // diagonale, (q0 + 1, r0) – (q0, r0 + 1), qui relie deux voisines.
        Span<(int Q, int R, double Weight)> vertices = stackalloc (int, int, double)[3];

        if (fq + fr <= 1)
        {
            vertices[0] = (q0, r0, 1 - fq - fr);
            vertices[1] = (q0 + 1, r0, fq);
            vertices[2] = (q0, r0 + 1, fr);
        }
        else
        {
            vertices[0] = (q0 + 1, r0 + 1, fq + fr - 1);
            vertices[1] = (q0, r0 + 1, 1 - fq);
            vertices[2] = (q0 + 1, r0, 1 - fr);
        }

        // Au centre d'une case, sa propre altitude, sans erreur d'arrondi :
        // toutes les lectures d'un même centre donnent la même valeur.
        foreach (var vertex in vertices)
        {
            if (vertex.Weight >= 1 - CenterWeightTolerance
                && TryGetIndex(new HexCoord(vertex.Q, vertex.R), out int center))
            {
                elevationMeters = _elevation[center];
                return true;
            }
        }

        double sum = 0;
        double weight = 0;

        foreach (var vertex in vertices)
        {
            if (vertex.Weight > 0
                && TryGetIndex(new HexCoord(vertex.Q, vertex.R), out int cell))
            {
                sum += vertex.Weight * _elevation[cell];
                weight += vertex.Weight;
            }
        }

        if (weight <= 0)
            return false;

        elevationMeters = sum / weight;
        return true;
    }

    private const double CenterWeightTolerance = 1e-9;

    /// <summary>
    /// Filtre grossier : rejette les points à plus d'une case du cadre,
    /// avant toute conversion en coordonnées entières.
    /// </summary>
    private bool IsNearFrame(double xMeters, double yMeters)
    {
        if (!double.IsFinite(xMeters))
            throw new ArgumentOutOfRangeException(nameof(xMeters));

        if (!double.IsFinite(yMeters))
            throw new ArgumentOutOfRangeException(nameof(yMeters));

        double maxX = Columns * SpacingMeters + SpacingMeters;
        double maxY = Rows * SpacingMeters * HexCoord.RowSpacingFactor + SpacingMeters;

        return xMeters >= -2 * SpacingMeters
            && xMeters <= maxX
            && yMeters >= -2 * SpacingMeters
            && yMeters <= maxY;
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
