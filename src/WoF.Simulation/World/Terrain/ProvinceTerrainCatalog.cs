namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Frontières des provinces d'une campagne, fournies par le scénario,
/// et graine du monde. Produit le descripteur qui régénère une grille.
///
/// Les provinces sont identifiées par référence : deux provinces de même
/// identifiant restent distinctes.
/// Une frontière n'est acceptée que si l'autre côté la décrit aussi :
/// même nombre de routes et orientations opposées, à une petite tolérance près.
/// </summary>
public sealed class ProvinceTerrainCatalog
{
    /// <summary>Écart toléré, en degrés, entre deux orientations censées être opposées.</summary>
    public const double BearingToleranceDegrees = 1.0;

    private readonly Dictionary<Province, ProvinceNeighborBorder[]> _borders =
        new(ReferenceEqualityComparer.Instance);

    public ulong WorldSeed { get; }

    public ProvinceTerrainCatalog(ulong worldSeed)
    {
        WorldSeed = worldSeed;
    }

    /// <summary>
    /// Enregistre une fois pour toutes les frontières d'une province.
    /// Une redéfinition est refusée : une grille déjà générée ne doit pas
    /// devenir différente de sa régénération.
    /// </summary>
    public void SetBorders(Province province, IEnumerable<ProvinceNeighborBorder> borders)
    {
        ArgumentNullException.ThrowIfNull(province);
        ArgumentNullException.ThrowIfNull(borders);

        var copy = borders.ToArray();

        if (copy.Any(border => border is null))
            throw new ArgumentException("Border cannot be null.", nameof(borders));

        if (!_borders.TryAdd(province, copy))
            throw new InvalidOperationException(
                $"Borders of province {province.Id} are already defined.");
    }

    /// <summary>
    /// Construit le descripteur de la province, après avoir vérifié chacune
    /// de ses frontières et sa réciproque chez la voisine.
    /// </summary>
    public ProvinceTerrainDescriptor GetDescriptor(Province province)
    {
        ArgumentNullException.ThrowIfNull(province);

        var borders = GetRegisteredBorders(province);

        // Chaque frontière désigne une seule voisine réelle.
        foreach (var border in borders)
            FindNeighbor(province, border.NeighborProvinceId);

        // Chaque voisine réelle a sa frontière, décrite de la même façon des deux côtés.
        foreach (var neighbor in province.Neighbours)
        {
            var border = borders.SingleOrDefault(b => b.NeighborProvinceId == neighbor.Id)
                ?? throw new InvalidOperationException(
                    $"Province {province.Id} has no border with neighbor {neighbor.Id}.");

            var reverse = GetRegisteredBorders(neighbor)
                .SingleOrDefault(b => b.NeighborProvinceId == province.Id)
                ?? throw new InvalidOperationException(
                    $"Neighbor {neighbor.Id} has no border with province {province.Id}.");

            if (reverse.RoadLevel != border.RoadLevel)
                throw new InvalidOperationException(
                    $"Border {province.Id}-{neighbor.Id} has different road levels on each side.");

            if (!AreOpposite(border.BearingDegrees, reverse.BearingDegrees))
                throw new InvalidOperationException(
                    $"Border {province.Id}-{neighbor.Id} has bearings that are not opposite.");
        }

        // Le descripteur vérifie en plus l'unicité des voisines et des orientations.
        return ProvinceTerrainDescriptor.FromProvince(province, WorldSeed, borders);
    }

    /// <summary>
    /// Vrai si les deux orientations diffèrent de 180°, à la tolérance près (incluse),
    /// en tenant compte du passage par 0° / 360°.
    /// </summary>
    public static bool AreOpposite(double firstBearingDegrees, double secondBearingDegrees)
    {
        double gap = (firstBearingDegrees - secondBearingDegrees - 180) % 360;

        if (gap < -180)
            gap += 360;
        else if (gap >= 180)
            gap -= 360;

        return Math.Abs(gap) <= BearingToleranceDegrees;
    }

    private ProvinceNeighborBorder[] GetRegisteredBorders(Province province) =>
        _borders.TryGetValue(province, out var borders)
            ? borders
            : throw new InvalidOperationException(
                $"Borders of province {province.Id} are not defined.");

    private static Province FindNeighbor(Province province, int neighborId)
    {
        var matches = province.Neighbours.Where(n => n.Id == neighborId).ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Border {province.Id}-{neighborId} does not designate exactly one neighbor.");
    }
}
