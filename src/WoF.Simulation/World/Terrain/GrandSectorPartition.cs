namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Grand secteur : hexagone régulier de rayon 3 (37 cases), environ 1,4 km de large.
/// Un grand secteur coupé par le bord du cadre est partiel.
/// </summary>
public sealed class GrandSector
{
    public const int Radius = 3;
    public const int FullCellCount = 3 * Radius * Radius + 3 * Radius + 1;

    private readonly int[] _cells;
    private int[] _neighborIds = Array.Empty<int>();

    /// <summary>Identifiant, à partir de 1, dans l'ordre de la première case rencontrée.</summary>
    public int Id { get; }

    /// <summary>Centre du pavage ; peut être hors du cadre pour un secteur partiel.</summary>
    public HexCoord Center { get; }

    /// <summary>Indices des cases, triés par ordre croissant.</summary>
    public IReadOnlyList<int> Cells { get; }

    public IReadOnlyList<int> NeighborIds { get; private set; } = Array.Empty<int>();

    public bool IsPartial => _cells.Length < FullCellCount;

    /// <summary>Terrain le plus fréquent ; à égalité, la première valeur de l'énumération.</summary>
    public TerrainType DominantTerrain { get; }

    public double MeanElevationMeters { get; }

    /// <summary>Union des éléments de ses cases.</summary>
    public TerrainCellFeature Features { get; }

    public bool HasRiver { get; }

    internal GrandSector(int id, HexCoord center, int[] cells, ProvinceHexGrid grid)
    {
        Id = id;
        Center = center;
        _cells = cells;
        Cells = Array.AsReadOnly(_cells);

        var counts = new int[Enum.GetValues<TerrainType>().Length];
        double elevation = 0;
        var features = TerrainCellFeature.None;
        bool river = false;

        foreach (int cell in cells)
        {
            counts[(int)grid.GetTerrain(cell)]++;
            elevation += grid.GetElevation(cell);
            features |= grid.GetFeatures(cell);
            river |= grid.HasAnyRiverEdge(cell);
        }

        int dominant = 0;

        for (int t = 1; t < counts.Length; t++)
        {
            if (counts[t] > counts[dominant])
                dominant = t;
        }

        DominantTerrain = (TerrainType)dominant;
        MeanElevationMeters = elevation / cells.Length;
        Features = features;
        HasRiver = river;
    }

    internal void SetNeighbors(int[] neighborIds)
    {
        _neighborIds = neighborIds;
        NeighborIds = Array.AsReadOnly(_neighborIds);
    }
}

/// <summary>
/// Pavage régulier de la grille en grands secteurs hexagonaux de rayon 3.
///
/// Les centres forment le réseau engendré par a = (7, −3) et b = (3, 4)
/// en coordonnées axiales, à partir de la case centrale du cadre.
/// Chaque case est à distance ≤ 3 d'un seul centre : le pavage est exact.
/// </summary>
public sealed class GrandSectorPartition
{
    private const int LatticeDeterminant = 37;

    private readonly GrandSector[] _sectors;
    private readonly int[] _sectorIndexByCell;

    public ProvinceHexGrid Grid { get; }

    public IReadOnlyList<GrandSector> Sectors { get; }

    private GrandSectorPartition(
        ProvinceHexGrid grid,
        GrandSector[] sectors,
        int[] sectorIndexByCell)
    {
        Grid = grid;
        _sectors = sectors;
        _sectorIndexByCell = sectorIndexByCell;
        Sectors = Array.AsReadOnly(_sectors);
    }

    public static GrandSectorPartition Build(ProvinceHexGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var origin = grid.GetCoord(
            (grid.Rows / 2) * grid.Columns + grid.Columns / 2);

        var indexByLattice = new Dictionary<(int I, int J), int>();
        var centers = new List<HexCoord>();
        var cellsBySector = new List<List<int>>();
        var sectorIndexByCell = new int[grid.CellCount];

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            var coord = grid.GetCoord(cell);
            var (i, j, center) = FindLatticePoint(coord, origin);

            if (!indexByLattice.TryGetValue((i, j), out int index))
            {
                index = centers.Count;
                indexByLattice.Add((i, j), index);
                centers.Add(center);
                cellsBySector.Add(new List<int>());
            }

            cellsBySector[index].Add(cell);
            sectorIndexByCell[cell] = index;
        }

        var sectors = new GrandSector[centers.Count];

        for (int s = 0; s < sectors.Length; s++)
        {
            sectors[s] = new GrandSector(
                s + 1, centers[s], cellsBySector[s].ToArray(), grid);
        }

        var neighbors = new SortedSet<int>[sectors.Length];

        for (int s = 0; s < sectors.Length; s++)
            neighbors[s] = new SortedSet<int>();

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            int own = sectorIndexByCell[cell];

            for (int d = 0; d < HexCoord.DirectionCount; d++)
            {
                int next = grid.GetNeighborIndex(cell, d);

                if (next < 0)
                    continue;

                int other = sectorIndexByCell[next];

                if (other != own)
                    neighbors[own].Add(other + 1);
            }
        }

        for (int s = 0; s < sectors.Length; s++)
            sectors[s].SetNeighbors(neighbors[s].ToArray());

        return new GrandSectorPartition(grid, sectors, sectorIndexByCell);
    }

    public GrandSector GetSector(int id)
    {
        if (id < 1 || id > _sectors.Length)
            throw new ArgumentOutOfRangeException(nameof(id));

        return _sectors[id - 1];
    }

    public bool ContainsSectorId(int id)
    {
        return id >= 1 && id <= _sectors.Length;
    }

    public GrandSector GetSectorOfCell(int cell)
    {
        if (cell < 0 || cell >= _sectorIndexByCell.Length)
            throw new ArgumentOutOfRangeException(nameof(cell));

        return _sectors[_sectorIndexByCell[cell]];
    }

    /// <summary>
    /// Grands secteurs qui touchent l'arc du cadre attribué à une province voisine,
    /// triés par identifiant. Le marquage « arrière » d'un camp viendra à l'étape 3.
    /// </summary>
    public IReadOnlyList<GrandSector> GetBorderSectors(int neighborProvinceId)
    {
        return Grid.GetBorderCells(neighborProvinceId)
            .Select(c => _sectorIndexByCell[c])
            .Distinct()
            .OrderBy(i => i)
            .Select(i => _sectors[i])
            .ToArray();
    }

    /// <summary>
    /// Résout cell − origin = i·a + j·b, puis examine les points entiers voisins
    /// de la solution réelle : un seul est à distance ≤ 3.
    /// </summary>
    private static (int I, int J, HexCoord Center) FindLatticePoint(
        HexCoord cell,
        HexCoord origin)
    {
        int q = cell.Q - origin.Q;
        int r = cell.R - origin.R;

        int i0 = (int)Math.Floor((4.0 * q - 3.0 * r) / LatticeDeterminant);
        int j0 = (int)Math.Floor((3.0 * q + 7.0 * r) / LatticeDeterminant);

        for (int i = i0 - 1; i <= i0 + 2; i++)
        {
            for (int j = j0 - 1; j <= j0 + 2; j++)
            {
                var center = new HexCoord(
                    origin.Q + 7 * i + 3 * j,
                    origin.R - 3 * i + 4 * j);

                if (center.DistanceTo(cell) <= GrandSector.Radius)
                    return (i, j, center);
            }
        }

        throw new InvalidOperationException(
            "Grand sector lattice point not found.");
    }
}
