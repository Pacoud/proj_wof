using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

//Ici on utilise l'algorithme de Dijkstra pour dessiner un chemin automatique vers une province éloignée

namespace WoF.Simulation.Military;

public static class MilitaryPathfinder
{
    public static IReadOnlyList<Province>? FindFastestPath(
        Province start,
        Province destination,
        IEnumerable<InfrastructureLink> links,
        DivisionType divisionType)
    {
        if (ReferenceEquals(start, destination))
        {
            return new[] { start };
        }

        var distances =
            new Dictionary<Province, int>();

        var previous =
            new Dictionary<Province, Province>();

        var queue =
            new PriorityQueue<Province, int>();

        distances[start] = 0;

        queue.Enqueue(start, 0);

        while (queue.TryDequeue(
                   out Province? current,
                   out int currentDistance))
        {
            if (currentDistance >
                distances[current])
            {
                continue;
            }

            if (ReferenceEquals(
                    current,
                    destination))
            {
                break;
            }

            foreach (var neighbour
                     in current.Neighbours)
            {
                int movementCost =
                    MovementSystem
                        .CalculateMovementDuration(
                            current,
                            neighbour,
                            links,
                            divisionType
                        );

                int candidateDistance =
                    currentDistance
                    + movementCost;

                if (!distances.TryGetValue(
                        neighbour,
                        out int knownDistance)
                    ||
                    candidateDistance
                    < knownDistance)
                {
                    distances[neighbour] =
                        candidateDistance;

                    previous[neighbour] =
                        current;

                    queue.Enqueue(
                        neighbour,
                        candidateDistance
                    );
                }
            }
        }

        if (!distances.ContainsKey(destination))
            return null;

        var path =
            new List<Province>();

        Province step = destination;

        path.Add(step);

        while (!ReferenceEquals(step, start))
        {
            step = previous[step];

            path.Add(step);
        }

        path.Reverse();

        return path;
    }
}