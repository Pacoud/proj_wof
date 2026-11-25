using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>Détenteurs des grands secteurs d'une bataille, entre deux heures de contrôle.</summary>
internal sealed class TacticalGrandSectorControl
{
    private readonly Country? _initial;
    private readonly Dictionary<GrandSector, Country> _changes = new(ReferenceEqualityComparer.Instance);

    internal TacticalGrandSectorControl(Country? initial) => _initial = initial;

    internal Country? GetController(ProvinceBattlefieldLayout layout, GrandSector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);
        // Les identifiants recommencent dans chaque partition. Le constructeur
        // de champ inclut un grand secteur entier, même au bord de la province.
        if (!layout.Partition.ContainsSectorId(sector.Id)
            || !ReferenceEquals(layout.Partition.GetSector(sector.Id), sector)
            || !layout.TryGetSector(sector.Cells[0], out _))
            throw new InvalidOperationException("Grand sector does not belong to this battlefield.");

        return _changes.TryGetValue(sector, out var owner) ? owner : _initial;
    }

    internal void Update(ProvinceBattlefieldLayout layout, Func<BattleSector, Country?> controllerOf)
    {
        // Un secteur voisin inclus compte au même titre qu'un secteur demandé.
        foreach (int id in layout.IncludedGrandSectorIds)
        {
            var sector = layout.Partition.GetSector(id);
            var cells = sector.Cells;
            var first = controllerOf(layout.GetSector(cells[0]));
            if (first is null)
                continue;

            // La liste contient les cases réelles du secteur, y compris au bord.
            int i = 1;
            while (i < cells.Count && controllerOf(layout.GetSector(cells[i]))?.Id == first.Id)
                i++;

            if (i != cells.Count)
                continue; // Le détenteur précédent reste en place.

            if (first.Id == _initial?.Id)
                _changes.Remove(sector);
            else
                _changes[sector] = first;
        }
    }
}
