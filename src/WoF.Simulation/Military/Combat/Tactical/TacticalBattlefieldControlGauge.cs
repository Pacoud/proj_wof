using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>Jauge pondérée de la zone disputée, consultable entre deux passes horaires.</summary>
public sealed class TacticalBattlefieldControlGauge
{
    private Dictionary<int, int> _weights = new();

    public Country? Controller { get; private set; }
    // Les poids sont disponibles à partir de la première passe horaire.
    public int TotalWeight { get; private set; }

    internal TacticalBattlefieldControlGauge(Country? initial) => Controller = initial;

    public int GetWeight(Country country)
    {
        ArgumentNullException.ThrowIfNull(country);
        return _weights.GetValueOrDefault(country.Id);
    }

    internal void Update(ProvinceBattlefieldLayout layout, Func<GrandSector, Country?> controllerOf)
    {
        var weights = new Dictionary<int, int>();
        Country? leader = null;
        int best = 0;
        foreach (int id in layout.IncludedGrandSectorIds)
        {
            var sector = layout.Partition.GetSector(id);
            var country = controllerOf(sector);
            if (country is null)
                continue;

            // Toutes les cases du grand secteur pèsent pour son détenteur mémorisé.
            int weight = weights.GetValueOrDefault(country.Id) + sector.Cells.Count;
            weights[country.Id] = weight;
            if (leader is null || weight > best || (weight == best && country.Id < leader.Id))
            {
                leader = country;
                best = weight;
            }
        }

        // Le dénominateur comprend aussi les secteurs sans détenteur.
        // Comparer 5 * poids à 3 ou 2 fois le total évite tout arrondi aux seuils.
        int total = layout.CellIndices.Count;
        if (leader is null || 5L * best >= 3L * total)
            Controller = leader;
        else if (Controller is { } previous && 5L * weights.GetValueOrDefault(previous.Id) < 2L * total)
            Controller = leader;

        _weights = weights;
        TotalWeight = total;
    }
}