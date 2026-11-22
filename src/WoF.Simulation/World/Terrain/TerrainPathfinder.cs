namespace WoF.Simulation.World.Terrain;

/// <summary>Graphe parcouru par le générateur (cases pour les routes, coins pour les rivières).</summary>
internal interface ITerrainGraph
{
    int NodeCount { get; }

    /// <summary>Remplit le tampon avec les arêtes sortantes, dans un ordre fixe.</summary>
    void GetEdges(int node, List<(int Node, double Cost)> buffer);

    /// <summary>Minorant du coût restant : doit rester admissible.</summary>
    double EstimateCost(int from, int to);
}

/// <summary>
/// A* déterministe : à coût égal, le nœud d'indice le plus petit sort en premier.
/// </summary>
internal static class TerrainPathfinder
{
    internal static List<int>? FindPath(
        ITerrainGraph graph,
        int start,
        int goal)
    {
        int count = graph.NodeCount;

        var cost = new double[count];
        var previous = new int[count];
        var closed = new bool[count];

        Array.Fill(cost, double.PositiveInfinity);
        Array.Fill(previous, -1);

        var open = new PriorityQueue<int, (double Priority, int Node)>();
        var edges = new List<(int Node, double Cost)>(8);

        cost[start] = 0;
        open.Enqueue(start, (graph.EstimateCost(start, goal), start));

        while (open.TryDequeue(out int node, out _))
        {
            if (closed[node])
                continue;

            if (node == goal)
                return Rebuild(previous, goal);

            closed[node] = true;

            edges.Clear();
            graph.GetEdges(node, edges);

            foreach (var (next, edgeCost) in edges)
            {
                if (closed[next])
                    continue;

                double candidate = cost[node] + edgeCost;

                if (candidate < cost[next])
                {
                    cost[next] = candidate;
                    previous[next] = node;

                    open.Enqueue(
                        next,
                        (candidate + graph.EstimateCost(next, goal), next));
                }
            }
        }

        return null;
    }

    private static List<int> Rebuild(int[] previous, int goal)
    {
        var path = new List<int>();

        for (int node = goal; node >= 0; node = previous[node])
            path.Add(node);

        path.Reverse();

        return path;
    }
}
