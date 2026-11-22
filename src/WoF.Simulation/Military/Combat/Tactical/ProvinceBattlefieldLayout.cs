using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Champ de bataille construit sur une partie de la grille d'une province,
/// avec la correspondance entre cases et BattleSector.
///
/// La géométrie ne peut être posée qu'après la création de la TacticalBattle,
/// car TacticalGeometryMap exige une bataille active : voir DefineGeometry.
/// </summary>
public sealed class ProvinceBattlefieldLayout
{
    private readonly Dictionary<int, BattleSector> _sectorByCell;
    private readonly Dictionary<BattleSector, int> _cellBySector;

    public ProvinceHexGrid Grid { get; }

    public GrandSectorPartition Partition { get; }

    public TacticalBattlefield Battlefield { get; }

    /// <summary>Grands secteurs demandés, triés.</summary>
    public IReadOnlyList<int> RequestedGrandSectorIds { get; }

    /// <summary>Grands secteurs inclus (demandés et leurs voisins), triés.</summary>
    public IReadOnlyList<int> IncludedGrandSectorIds { get; }

    /// <summary>Cases incluses, triées.</summary>
    public IReadOnlyList<int> CellIndices { get; }

    internal ProvinceBattlefieldLayout(
        ProvinceHexGrid grid,
        GrandSectorPartition partition,
        TacticalBattlefield battlefield,
        int[] requested,
        int[] included,
        int[] cells,
        Dictionary<int, BattleSector> sectorByCell)
    {
        Grid = grid;
        Partition = partition;
        Battlefield = battlefield;
        RequestedGrandSectorIds = Array.AsReadOnly(requested);
        IncludedGrandSectorIds = Array.AsReadOnly(included);
        CellIndices = Array.AsReadOnly(cells);

        _sectorByCell = sectorByCell;
        _cellBySector = sectorByCell.ToDictionary(p => p.Value, p => p.Key);
    }

    public bool TryGetSector(int cell, out BattleSector sector)
    {
        return _sectorByCell.TryGetValue(cell, out sector!);
    }

    public BattleSector GetSector(int cell)
    {
        if (!_sectorByCell.TryGetValue(cell, out var sector))
            throw new InvalidOperationException(
                "Cell is not part of this battlefield.");

        return sector;
    }

    /// <summary>Indice de la case d'un secteur, contrôlé par référence.</summary>
    public int GetCellIndex(BattleSector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);

        if (!_cellBySector.TryGetValue(sector, out int cell))
            throw new InvalidOperationException(
                "Sector does not belong to this battlefield.");

        return cell;
    }

    /// <summary>
    /// Centre de la case d'un secteur, à l'altitude du sol.
    ///
    /// Même lecture que le moteur (TacticalGeometryMap.GetSectorCenterPoint,
    /// itinéraires, ligne de vue) : l'altitude de la grille au centre de la
    /// case, et non l'altitude entière BattleSector.ElevationMeters, qui
    /// reste un résumé pour l'affichage.
    /// </summary>
    public TacticalPoint GetCellCenter(BattleSector sector)
    {
        int cell = GetCellIndex(sector);

        double x = Grid.GetCenterXMeters(cell);
        double y = Grid.GetCenterYMeters(cell);

        if (!Grid.TryGetGroundElevation(x, y, out double elevation))
            throw new InvalidOperationException(
                "Cell center is outside the province grid.");

        return new TacticalPoint(x, y, elevation);
    }

    /// <summary>
    /// Définit la géométrie de chaque case : un cercle centré sur la case,
    /// de rayon égal au rayon circonscrit de l'hexagone. Relie ensuite la
    /// carte au sol de la province : positions, itinéraires et ligne de vue
    /// liront l'altitude et le couvert de la grille, et la détection la case
    /// d'un point vu.
    ///
    /// Tout est vérifié avant la première écriture : en cas de refus,
    /// la carte reste inchangée. Après ces contrôles, DefineSector et
    /// AttachTerrain ne peuvent plus échouer (secteurs de ce champ de
    /// bataille, centres finis, rayon positif, carte sans terrain ni
    /// compagnie placée).
    /// </summary>
    public void DefineGeometry(TacticalGeometryMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (!ReferenceEquals(map.Battle.Battlefield, Battlefield))
            throw new InvalidOperationException(
                "Geometry map does not belong to this battlefield.");

        if (!map.Battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "Cannot define geometry for an ended battle.");

        if (map.Terrain is not null)
            throw new InvalidOperationException(
                "Geometry map already has a terrain.");

        if (map.CompanyPositions.Count > 0)
            throw new InvalidOperationException(
                "Geometry must be defined before placing companies.");

        foreach (int cell in CellIndices)
        {
            if (map.GetSectorGeometry(_sectorByCell[cell]) is not null)
                throw new InvalidOperationException(
                    "A cell of this battlefield already has a sector geometry.");
        }

        foreach (int cell in CellIndices)
        {
            map.DefineSector(
                _sectorByCell[cell],
                Grid.GetCenterXMeters(cell),
                Grid.GetCenterYMeters(cell),
                ProvinceHexGrid.CircumradiusMeters);
        }

        map.AttachTerrain(new TacticalTerrainSurface(Grid, _sectorByCell));
    }
}
