using WoF.Simulation.Military;

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

                SupplyRoute? usedRoute = null;

                double routeCapacity =
                    double.PositiveInfinity;

                // Même province : aucune route nécessaire.
                if (!ReferenceEquals(
                        division.Position,
                        depot.Position))
                {
                    usedRoute = _routes.FirstOrDefault(
                        route => route.Connects(
                            depot.Position,
                            division.Position
                        )
                    );

                    // Aucun chemin direct.
                    if (usedRoute == null)
                        continue;

                    routeCapacity =
                        remainingRouteCapacity[usedRoute];

                    if (routeCapacity <= 0)
                        continue;
                }

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
                    routeCapacity
                );

                if (requestedFuel <= 0)
                    continue;

                double withdrawn =
                    depot.WithdrawFuel(requestedFuel);

                double received =
                    division.ReceiveFuel(withdrawn);

                remainingDepotCapacity -= received;

                if (usedRoute != null)
                {
                    remainingRouteCapacity[usedRoute]
                        -= received;
                }
            }
        }
    }
}