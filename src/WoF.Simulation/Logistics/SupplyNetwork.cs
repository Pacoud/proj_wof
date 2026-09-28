using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Logistics;

public sealed class SupplyNetwork
{
    private readonly List<InfrastructureLink> _links = new();

    public IReadOnlyList<InfrastructureLink> Links => _links;

    public void AddLink(InfrastructureLink link)
    {
        if (!_links.Contains(link))
        {
            _links.Add(link);
        }
    }

    public void ProcessFuelSupply(
    IEnumerable<SupplyDepot> depots,
    IEnumerable<Division> divisions)
    {
        // Capacité encore disponible sur chaque liaison pour ce tick.
        var remainingLinkCapacity =
            _links.ToDictionary(
                link => link,
                link => link.TransportCapacityPerHour
            );

        foreach (var depot in depots)
        {
            double remainingDepotCapacity =
                depot.FuelTransferPerHour;

            foreach (var division in divisions)
            {
                if (remainingDepotCapacity <= 0)
                    break;

                if (depot.FuelStock <= 0)
                    break;

                if (division.IsInTransit)
                    continue;

                if (division.CurrentProvince == null)
                    continue;

                // Cherche maintenant un véritable chemin.
                SupplyPath? path = FindWidestPath(
                    depot.Position,
                    division.CurrentProvince,
                    remainingLinkCapacity
                );

                if (path == null)
                    continue;

                double pathCapacity;

                // Même province :
                // aucune route n'est utilisée.
                if (path.Links.Count == 0)
                {
                    pathCapacity =
                        double.PositiveInfinity;
                }
                else
                {
                    // Le débit possible est celui du maillon
                    // ayant le moins de capacité restante.
                    pathCapacity =
                        path.Links.Min(
                            link =>
                                remainingLinkCapacity[link]
                        );
                }

                if (pathCapacity <= 0)
                    continue;

                double requestedFuel = Math.Min(
                    division.MissingFuel,
                    remainingDepotCapacity
                );

                requestedFuel = Math.Min(
                    requestedFuel,
                    depot.FuelStock
                );

                requestedFuel = Math.Min(
                    requestedFuel,
                    pathCapacity
                );

                if (requestedFuel <= 0)
                    continue;

                double withdrawn =
                    depot.WithdrawFuel(requestedFuel);

                double received =
                    division.ReceiveFuel(withdrawn);

                remainingDepotCapacity -= received;

                // Toute liaison traversée perd cette capacité.
                foreach (var link in path.Links)
                {
                    remainingLinkCapacity[link]
                        -= received;
                }
            }
        }
    }
    

    public SupplyPath? FindPath(
        Province start,
        Province destination)
    {
        if (ReferenceEquals(start, destination))
        {
            return new SupplyPath(
                Array.Empty<InfrastructureLink>()
            );
        }

        var visited = new HashSet<Province>();

        var queue = new Queue<(
            Province Province,
            List<InfrastructureLink> Path
        )>();

        visited.Add(start);

        queue.Enqueue((
            start,
            new List<InfrastructureLink>()
        ));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var link in _links)
            {
                var nextProvince =
                    link.GetOtherProvince(
                        current.Province
                    );

                if (nextProvince == null)
                    continue;

                if (visited.Contains(nextProvince))
                    continue;

                var newPath =
                    new List<InfrastructureLink>(
                        current.Path
                    )
                    {
                        link
                    };

                if (ReferenceEquals(
                        nextProvince,
                        destination))
                {
                    return new SupplyPath(newPath);
                }

                visited.Add(nextProvince);

                queue.Enqueue((
                    nextProvince,
                    newPath
                ));
            }
        }

        return null;
    }

    public SupplyPath? FindWidestPath(  //Nouvelle fonction de recherche du chemin optimisé
    Province start,
    Province destination)
    {
        var capacities = _links.ToDictionary(
            link => link,
            link => link.TransportCapacityPerHour
        );

        return FindWidestPath(
            start,
            destination,
            capacities
        );
    }


    private SupplyPath? FindWidestPath(
    Province start,
    Province destination,
    IReadOnlyDictionary<InfrastructureLink, double> capacities)
    {
        if (ReferenceEquals(start, destination))
        {
            return new SupplyPath(
                Array.Empty<InfrastructureLink>()
            );
        }

        var bestCapacity =
            new Dictionary<Province, double>();

        var previous =
            new Dictionary<
                Province,
                (Province Previous, InfrastructureLink Link)
            >();

        var queue =
            new PriorityQueue<Province, double>();

        bestCapacity[start] =
            double.PositiveInfinity;

        queue.Enqueue(
            start,
            double.NegativeInfinity
        );

        var visited =
            new HashSet<Province>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!visited.Add(current))
                continue;

            if (ReferenceEquals(
                    current,
                    destination))
            {
                break;
            }

            foreach (var link in _links)
            {
                var next =
                    link.GetOtherProvince(current);

                if (next == null)
                    continue;

                if (visited.Contains(next))
                    continue;

                double linkCapacity =
                    capacities[link];

                double candidateCapacity =
                    Math.Min(
                        bestCapacity[current],
                        linkCapacity
                    );

                if (!bestCapacity.TryGetValue(
                        next,
                        out double knownCapacity)
                    ||
                    candidateCapacity > knownCapacity)
                {
                    bestCapacity[next] =
                        candidateCapacity;

                    previous[next] =
                        (current, link);

                    // PriorityQueue est un min-heap :
                    // valeur négative = grande capacité prioritaire.
                    queue.Enqueue(
                        next,
                        -candidateCapacity
                    );
                }
            }
        }

        if (!bestCapacity.ContainsKey(destination))
            return null;

        var path =
            new List<InfrastructureLink>();

        var currentProvince = destination;

        while (!ReferenceEquals(
                currentProvince,
                start))
        {
            var step =
                previous[currentProvince];

            path.Add(step.Link);

            currentProvince =
                step.Previous;
        }

        path.Reverse();

        return new SupplyPath(path);
    }
}
