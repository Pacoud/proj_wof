using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Military.Combat.Tactical;

internal static class TacticalSectorEncirclement
{
    internal readonly record struct CellState(Country? Holder, bool Occupied);

    // cells contient toutes les cases du champ, dans leur état après occupation.
    internal static List<(int Cell, Country Winner)> FindTransfers(
        ProvinceBattlefieldLayout layout, IReadOnlyDictionary<int, CellState> cells,
        DiplomacySystem diplomacy)
    {
        var transfers = new List<(int Cell, Country Winner)>();
        var visited = new HashSet<int>();
        var pocket = new List<int>();
        var perimeter = new HashSet<int>();

        foreach (int start in layout.CellIndices)
        {
            var first = cells[start];
            if (first.Occupied || first.Holder is not Country holder || !visited.Add(start))
                continue;

            // La liste sert de file FIFO et conserve les membres de la poche.
            // head avance dans l'ordre d'arrivée. Chaque case est marquée à l'ajout.
            pocket.Clear();
            perimeter.Clear();
            pocket.Add(start);
            bool touchesEdge = false;
            for (int head = 0; head < pocket.Count; head++)
            {
                for (int direction = 0; direction < HexCoord.DirectionCount; direction++)
                {
                    int next = layout.Grid.GetNeighborIndex(pocket[head], direction);
                    if (!cells.TryGetValue(next, out var neighbor))
                    {
                        // Hors de la province ou de la partie construite du champ.
                        touchesEdge = true;
                        continue;
                    }

                    if (!neighbor.Occupied && neighbor.Holder?.Id == holder.Id)
                    {
                        if (visited.Add(next))
                            pocket.Add(next);
                    }
                    else
                    {
                        // Deux membres peuvent toucher la même case du pourtour.
                        perimeter.Add(next);
                    }
                }
            }

            // Même ouverte, la poche a été parcourue jusqu'au bout. Aucun
            // fragment ne sera repris plus tard comme une poche indépendante.
            if (touchesEdge)
                continue;

            var winner = GetWinner(perimeter, cells, holder, diplomacy);
            if (winner is not null)
                foreach (int cell in pocket)
                    transfers.Add((cell, winner));
        }

        return transfers;
    }

    private static Country? GetWinner(HashSet<int> perimeter,
        IReadOnlyDictionary<int, CellState> cells, Country holder, DiplomacySystem diplomacy)
    {
        var votes = new Dictionary<int, int>();
        Country? winner = null;
        int best = 0;
        foreach (int cell in perimeter)
        {
            var country = cells[cell].Holder;
            if (country is null || !diplomacy.AreAtWar(holder, country))
                return null;

            int count = votes.GetValueOrDefault(country.Id) + 1;
            votes[country.Id] = count;
            if (winner is null || count > best || (count == best && country.Id < winner.Id))
            {
                winner = country;
                best = count;
            }
        }

        return winner;
    }
}