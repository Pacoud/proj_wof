
namespace WoF.Simulation.Military.Combat.Tactical;

public sealed record TacticalCommunicationRoute(
    IReadOnlyList<TacticalCommunicationLink> Hops,
    long TotalMinutes);

/// <summary>
/// Routage par cout temporel minimal (Dijkstra).
/// Cache des recherches invalide lors d'un
/// changement de version du reseau.
/// </summary>
public sealed class TacticalMessageRouter
{
    private readonly TacticalCommunicationNetwork _network;

    internal TacticalCommunicationNetwork Network => _network;

    private readonly Dictionary<
        (TacticalCommunicationNode From,
         TacticalCommunicationNode To),
        TacticalCommunicationRoute?>
        _cache = new();

    private long _cachedVersion = -1;
    private long _sequence;

    public long SearchCount { get; private set; }
    public long CacheHitCount { get; private set; }

    public TacticalMessageRouter(
        TacticalCommunicationNetwork network)
    {
        _network = network
            ?? throw new ArgumentNullException(nameof(network));
    }

    public TacticalCommunicationRoute? FindRoute(
        TacticalCommunicationNode from,
        TacticalCommunicationNode to)
    {
        _network.ValidateNode(from);
        _network.ValidateNode(to);

        if (_cachedVersion != _network.Version)
        {
            _cache.Clear();
            _cachedVersion = _network.Version;
        }

        var key = (From: from, To: to);

        if (_cache.TryGetValue(key, out var cached))
        {
            CacheHitCount++;
            return cached;
        }

        SearchCount++;

        var route = Search(from, to);
        _cache.Add(key, route);

        return route;
    }

    private TacticalCommunicationRoute? Search(
        TacticalCommunicationNode start,
        TacticalCommunicationNode goal)
    {
        if (ReferenceEquals(start, goal))
        {
            return new TacticalCommunicationRoute(
                Array.Empty<TacticalCommunicationLink>(),
                0);
        }

        var distances =
            new Dictionary<TacticalCommunicationNode, long>();

        var previous = new Dictionary<
            TacticalCommunicationNode,
            TacticalCommunicationLink>();

        var queue = new PriorityQueue<
            TacticalCommunicationNode,
            (long Cost, long Sequence)>();

        distances[start] = 0;
        queue.Enqueue(start, (0, _sequence++));

        while (queue.TryDequeue(
            out var current,
            out var priority))
        {
            if (distances[current] != priority.Cost)
                continue;

            if (ReferenceEquals(current, goal))
                break;

            foreach (var link in _network.Outgoing(current))
            {
                if (!_network.IsAvailable(link))
                    continue;

                long proposed = checked(
                    priority.Cost + link.DelayMinutes);

                if (distances.TryGetValue(
                    link.To,
                    out long known)
                    && proposed >= known)
                {
                    continue;
                }

                distances[link.To] = proposed;
                previous[link.To] = link;

                queue.Enqueue(
                    link.To,
                    (proposed, _sequence++));
            }
        }

        if (!distances.TryGetValue(goal, out long total))
            return null;

        var reversed = new List<TacticalCommunicationLink>();
        var node = goal;

        while (!ReferenceEquals(node, start))
        {
            var link = previous[node];

            reversed.Add(link);
            node = link.From;
        }

        reversed.Reverse();

        return new TacticalCommunicationRoute(
            reversed.AsReadOnly(),
            total);
    }
}
