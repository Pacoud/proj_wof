using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Sol et couvert d'une province, tels que les lit le moteur tactique.
///
/// Le sol est interpolé entre les centres des cases
/// (ProvinceHexGrid.TryGetGroundElevation) : positions, itinéraires
/// et ligne de vue lisent tous cette même altitude.
/// Le couvert (bois, bâtiments) appartient à la case qui contient le point.
/// </summary>
internal sealed class TacticalTerrainSurface
{
    /// <summary>Hauteur des bois au-dessus du sol, pour la ligne de vue.</summary>
    internal const double WoodsHeightMeters = 15;

    /// <summary>Hauteur des bâtiments au-dessus du sol, pour la ligne de vue.</summary>
    internal const double BuildingsHeightMeters = 8;

    /// <summary>Écart maximal entre deux points testés le long d'une ligne de vue.</summary>
    internal const double SightSampleSpacingMeters = 25;

    internal ProvinceHexGrid Grid { get; }

    // Secteur du champ de bataille de chaque case incluse ; null quand
    // la surface est créée sans champ de bataille.
    private readonly IReadOnlyDictionary<int, BattleSector>? _sectorsByCell;

    internal TacticalTerrainSurface(
        ProvinceHexGrid grid,
        IReadOnlyDictionary<int, BattleSector>? sectorsByCell = null)
    {
        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _sectorsByCell = sectorsByCell;
    }

    /// <summary>
    /// Secteur du champ de bataille dont la case contient le point.
    /// Faux hors du cadre, dans une case qui n'appartient pas au champ
    /// de bataille, ou sans correspondance entre cases et secteurs.
    /// </summary>
    internal bool TryGetSectorAt(
        double xMeters,
        double yMeters,
        out BattleSector sector)
    {
        sector = null!;

        return _sectorsByCell is not null
            && Grid.TryGetCellAt(xMeters, yMeters, out int cell)
            && _sectorsByCell.TryGetValue(cell, out sector!);
    }

    /// <summary>
    /// Altitude du sol en un point de la province.
    /// Un point sans aucun centre de case autour de lui est refusé.
    /// </summary>
    internal double GetGroundElevation(double xMeters, double yMeters)
    {
        if (!Grid.TryGetGroundElevation(xMeters, yMeters, out double elevation))
            throw new InvalidOperationException(
                "Point is outside the province grid.");

        return elevation;
    }

    internal bool TryGetGroundElevation(
        double xMeters,
        double yMeters,
        out double elevationMeters)
    {
        return Grid.TryGetGroundElevation(xMeters, yMeters, out elevationMeters);
    }

    /// <summary>Indice de la case qui contient le point, ou -1 hors du cadre.</summary>
    internal int FindCell(double xMeters, double yMeters)
    {
        return Grid.TryGetCellAt(xMeters, yMeters, out int cell) ? cell : -1;
    }

    /// <summary>
    /// Hauteur du couvert d'une case au-dessus du sol :
    /// la plus haute de ses bois et de ses bâtiments, 0 sinon.
    /// </summary>
    internal double GetCoverHeight(int cell)
    {
        var features = Grid.GetFeatures(cell);
        double height = 0;

        if ((features & TerrainCellFeature.Woods) != 0)
            height = WoodsHeightMeters;

        if ((features & TerrainCellFeature.Buildings) != 0)
            height = Math.Max(height, BuildingsHeightMeters);

        return height;
    }
}
