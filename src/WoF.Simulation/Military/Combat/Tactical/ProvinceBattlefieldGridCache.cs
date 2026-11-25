using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>Grille d'une province et son découpage en grands secteurs.</summary>
public sealed class ProvinceBattlefieldGrid
{
    public Province Province { get; }
    public ProvinceHexGrid Grid { get; }
    public GrandSectorPartition Partition { get; }

    internal ProvinceBattlefieldGrid(Province province, ProvinceHexGrid grid, GrandSectorPartition partition)
    {
        Province = province;
        Grid = grid;
        Partition = partition;
    }
}

/// <summary>
/// Génère la grille de la province d'un engagement à la première demande,
/// puis la reprend telle quelle tant qu'elle n'est pas libérée.
/// Après libération, une nouvelle demande la régénère à l'identique depuis le catalogue.
///
/// Les provinces sont identifiées par référence, jamais par identifiant.
/// La libération est explicite : rien ne la déclenche automatiquement à ce stade.
/// </summary>
public sealed class ProvinceBattlefieldGridCache
{
    private readonly ProvinceTerrainCatalog _catalog;

    private readonly Dictionary<Province, ProvinceBattlefieldGrid> _grids =
        new(ReferenceEqualityComparer.Instance);

    public ProvinceBattlefieldGridCache(ProvinceTerrainCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Nombre de grilles actuellement gardées.</summary>
    public int Count => _grids.Count;

    /// <summary>Grille de la province du champ, fixée à la création de l'engagement (3a).</summary>
    public ProvinceBattlefieldGrid GetOrGenerate(Engagement engagement)
    {
        ArgumentNullException.ThrowIfNull(engagement);
        return GetOrGenerate(engagement.BattlefieldProvince);
    }

    public ProvinceBattlefieldGrid GetOrGenerate(Province province)
    {
        ArgumentNullException.ThrowIfNull(province);

        if (_grids.TryGetValue(province, out var existing))
            return existing;

        // Le catalogue vérifie les frontières avant toute génération.
        var grid = ProvinceTerrainGenerator.Generate(_catalog.GetDescriptor(province));
        var entry = new ProvinceBattlefieldGrid(province, grid, GrandSectorPartition.Build(grid));
        _grids.Add(province, entry);
        return entry;
    }

    public bool Contains(Province province) =>
        province is not null && _grids.ContainsKey(province);

    /// <summary>Libère la grille gardée pour cette province. Faux si aucune ne l'était.</summary>
    public bool Release(Province province)
    {
        ArgumentNullException.ThrowIfNull(province);
        return _grids.Remove(province);
    }
}
