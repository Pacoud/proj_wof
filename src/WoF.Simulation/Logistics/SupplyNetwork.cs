using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Simulation.Logistics;

public sealed class SupplyNetwork
{
    private readonly List<SupplyRoute> _routes = new();

    public IReadOnlyList<SupplyRoute> Routes => _routes;

    public void AddRoute(SupplyRoute route)
    {
        if (!_routes.Contains(route))
        {
            _routes.Add(route);
        }
    }

    public void ProcessFuelSupply(
    IEnumerable<SupplyDepot> depots,
    IEnumerable<Division> divisions)
    {
        // Capacité encore disponible sur chaque route pour ce tick.
        var remainingRouteCapacity =
            _routes.ToDictionary(
                route => route,
                route => route.FuelCapacityPerHour
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

                if (division.IsMoving)
                    continue;

                // Cherche maintenant un véritable chemin.
                SupplyPath? path = FindWidestPath(
                    depot.Position,
                    division.Position,
                    remainingRouteCapacity
                );

                if (path == null)
                    continue;

                double pathCapacity;

                // Même province :
                // aucune route n'est utilisée.
                if (path.Routes.Count == 0)
                {
                    pathCapacity =
                        double.PositiveInfinity;
                }
                else
                {
                    // Le débit possible est celui du maillon
                    // ayant le moins de capacité restante.
                    pathCapacity =
                        path.Routes.Min(
                            route =>
                                remainingRouteCapacity[route]
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

                // Toute route traversée perd cette capacité.
                foreach (var route in path.Routes)
                {
                    remainingRouteCapacity[route]
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
                Array.Empty<SupplyRoute>()
            );
        }

        var visited = new HashSet<Province>();

        var queue = new Queue<(
            Province Province,
            List<SupplyRoute> Path
        )>();

        visited.Add(start);

        queue.Enqueue((
            start,
            new List<SupplyRoute>()
        ));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var route in _routes)
            {
                var nextProvince =
                    route.GetOtherProvince(
                        current.Province
                    );

                if (nextProvince == null)
                    continue;

                if (visited.Contains(nextProvince))
                    continue;

                var newPath =
                    new List<SupplyRoute>(
                        current.Path
                    )
                    {
                        route
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
        var capacities = _routes.ToDictionary(
            route => route,
            route => route.FuelCapacityPerHour
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
    IReadOnlyDictionary<SupplyRoute, double> capacities)
    {
        if (ReferenceEquals(start, destination))
        {
            return new SupplyPath(
                Array.Empty<SupplyRoute>()
            );
        }

        var bestCapacity =
            new Dictionary<Province, double>();

        var previous =
            new Dictionary<
                Province,
                (Province Previous, SupplyRoute Route)
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

            foreach (var route in _routes)
            {
                var next =
                    route.GetOtherProvince(current);

                if (next == null)
                    continue;

                if (visited.Contains(next))
                    continue;

                double routeCapacity =
                    capacities[route];

                double candidateCapacity =
                    Math.Min(
                        bestCapacity[current],
                        routeCapacity
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
                        (current, route);

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
            new List<SupplyRoute>();

        var currentProvince = destination;

        while (!ReferenceEquals(
                currentProvince,
                start))
        {
            var step =
                previous[currentProvince];

            path.Add(step.Route);

            currentProvince =
                step.Previous;
        }

        path.Reverse();

        return new SupplyPath(path);
    }
}