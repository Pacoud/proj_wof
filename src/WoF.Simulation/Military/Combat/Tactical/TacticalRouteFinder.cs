
namespace WoF.Simulation.Military.Combat.Tactical;

internal sealed record TacticalSectorRoute(
    IReadOnlyList<BattleSector> Sectors,
    double TotalDistanceMeters);

/// <summary>
/// Recherche de route sur le graphe tactique (Dijkstra).
/// Les voisins sont lus dans l'index de TacticalBattlefield.
/// N'utilise ni les positions ennemies visibles par
/// le joueur ni les renseignements des compagnies.
/// C'est un outil de validation autoritaire du moteur.
/// </summary>
internal static class TacticalRouteFinder
{
    internal static TacticalSectorRoute Find(
        TacticalBattlefield battlefield,
        BattleSector origin,
        BattleSector destination,
        Func<BattleSector, bool> canEnter)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(canEnter);

        if (!battlefield.ContainsSector(origin)
            || !battlefield.ContainsSector(destination))
        {
            throw new InvalidOperationException(
                "Route sector belongs to another battlefield.");
        }

        if (ReferenceEquals(origin, destination))
            throw new InvalidOperationException(
                "Route requires distinct sectors.");

        var distances = new Dictionary<BattleSector, double>(
            ReferenceEqualityComparer.Instance);

        var parents = new Dictionary<BattleSector, BattleSector>(
            ReferenceEqualityComparer.Instance);

        var queue = new PriorityQueue<
            BattleSector, (double Distance, int SectorId)>();

        // Voisins du secteur courant, triés par identifiant :
        // même départage des égalités que le parcours historique.
        var neighbors = new List<(int Id, SectorConnection Connection)>(6);

        distances[origin] = 0;
        queue.Enqueue(origin, (0, origin.Id));

        while (queue.TryDequeue(
            out var current, out var priority))
        {
            if (priority.Distance > distances[current])
                continue;

            if (ReferenceEquals(current, destination))
                break;

            neighbors.Clear();

            foreach (var connection in battlefield.GetConnections(current))
                neighbors.Add((connection.GetOther(current).Id, connection));

            neighbors.Sort(static (x, y) => x.Id.CompareTo(y.Id));

            foreach (var (_, connection) in neighbors)
            {
                var next = connection.GetOther(current);

                if (!canEnter(next))
                    continue;

                double tentative =
                    distances[current] + connection.DistanceMeters;

                if (!double.IsFinite(tentative))
                    throw new InvalidOperationException(
                        "Tactical route distance overflow.");

                if (distances.TryGetValue(next, out double known)
                    && tentative >= known)
                {
                    continue;
                }

                distances[next] = tentative;
                parents[next] = current;

                queue.Enqueue(
                    next,
                    (tentative, next.Id));
            }
        }

        if (!distances.TryGetValue(destination, out double total))
            throw new InvalidOperationException(
                "No traversable tactical route.");

        var reversed = new List<BattleSector>
        {
            destination
        };

        var cursor = destination;

        while (!ReferenceEquals(cursor, origin))
        {
            cursor = parents[cursor];
            reversed.Add(cursor);
        }

        reversed.Reverse();

        return new TacticalSectorRoute(
            Array.AsReadOnly(reversed.ToArray()),
            total);
    }
}
