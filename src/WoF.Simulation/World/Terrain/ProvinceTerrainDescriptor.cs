using WoF.Simulation.Core;

namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Description permanente et légère du terrain d'une province.
/// Suffit, avec la graine du monde, à régénérer la grille à l'identique.
/// </summary>
public sealed class ProvinceTerrainDescriptor
{
    private const long ProvinceSeedDomain = 0x50524F56; // « PROV »
    private const long BorderSeedDomain = 0x424F5244;   // « BORD »

    private readonly ProvinceNeighborBorder[] _neighbors;

    public int ProvinceId { get; }

    /// <summary>Graine partagée par toutes les provinces d'une campagne.</summary>
    public ulong WorldSeed { get; }

    public TerrainType DominantTerrain { get; }

    public IReadOnlyList<ProvinceNeighborBorder> Neighbors { get; }

    /// <summary>Graine de la province, dérivée de la graine du monde et de l'identifiant.</summary>
    public ulong ProvinceSeed =>
        DeterministicRandom.Hash(
            WorldSeed, ProvinceSeedDomain, ProvinceId);

    public ProvinceTerrainDescriptor(
        int provinceId,
        ulong worldSeed,
        TerrainType dominantTerrain,
        IEnumerable<ProvinceNeighborBorder> neighbors)
    {
        ArgumentNullException.ThrowIfNull(neighbors);

        if (!Enum.IsDefined(dominantTerrain))
            throw new ArgumentOutOfRangeException(
                nameof(dominantTerrain));

        var copy = neighbors.ToArray();

        var ids = new HashSet<int>();
        var bearings = new HashSet<double>();

        foreach (var neighbor in copy)
        {
            if (neighbor is null)
                throw new ArgumentException(
                    "Neighbor border cannot be null.",
                    nameof(neighbors));

            if (neighbor.NeighborProvinceId == provinceId)
                throw new ArgumentException(
                    "A province cannot border itself.",
                    nameof(neighbors));

            if (!ids.Add(neighbor.NeighborProvinceId))
                throw new ArgumentException(
                    "Neighbor provinces must be distinct.",
                    nameof(neighbors));

            if (!bearings.Add(neighbor.BearingDegrees))
                throw new ArgumentException(
                    "Neighbor bearings must be distinct.",
                    nameof(neighbors));
        }

        ProvinceId = provinceId;
        WorldSeed = worldSeed;
        DominantTerrain = dominantTerrain;

        _neighbors = copy;
        Neighbors = Array.AsReadOnly(_neighbors);
    }

    public static ProvinceTerrainDescriptor FromProvince(
        Province province,
        ulong worldSeed,
        IEnumerable<ProvinceNeighborBorder> neighbors)
    {
        ArgumentNullException.ThrowIfNull(province);

        return new ProvinceTerrainDescriptor(
            province.Id,
            worldSeed,
            province.Terrain,
            neighbors);
    }

    /// <summary>
    /// Graine d'une frontière : identique vue des deux côtés,
    /// car les identifiants sont pris dans un ordre fixe.
    /// </summary>
    public static ulong ComputeBorderSeed(
        ulong worldSeed,
        int firstProvinceId,
        int secondProvinceId)
    {
        int low = Math.Min(firstProvinceId, secondProvinceId);
        int high = Math.Max(firstProvinceId, secondProvinceId);

        return DeterministicRandom.Hash(
            DeterministicRandom.Hash(worldSeed, BorderSeedDomain),
            low,
            high);
    }
}
