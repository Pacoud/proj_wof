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
    /// Centre de la case d'un secteur, à l'altitude déclarée du secteur.
    ///
    /// Même convention que le moteur (RelocateCompany, centres des itinéraires) :
    /// une compagnie au centre d'un secteur est à BattleSector.ElevationMeters,
    /// l'altitude entière, et non à l'altitude flottante de la grille.
    /// </summary>
    public TacticalPoint GetCellCenter(BattleSector sector)
    {
        int cell = GetCellIndex(sector);

        return new TacticalPoint(
            Grid.GetCenterXMeters(cell),
            Grid.GetCenterYMeters(cell),
            sector.ElevationMeters);
    }

    /// <summary>
    /// Définit la géométrie de chaque case : un cercle centré sur la case,
    /// de rayon égal au rayon circonscrit de l'hexagone.
    ///
    /// Tout est vérifié avant la première écriture : en cas de refus,
    /// la carte reste inchangée. Après ces contrôles, DefineSector ne peut
    /// plus échouer (secteurs de ce champ de bataille, centres finis, rayon positif).
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
    }
}
