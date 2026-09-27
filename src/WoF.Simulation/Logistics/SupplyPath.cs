using WoF.Simulation.World;

namespace WoF.Simulation.Logistics;

public sealed class SupplyPath
{
    public IReadOnlyList<SupplyRoute> Routes { get; }

    public double Capacity { get; }

    public SupplyPath(
        IReadOnlyList<SupplyRoute> routes)
    {
        Routes = routes;

        Capacity = routes.Count == 0
            ? double.PositiveInfinity
            : routes.Min(
                route => route.FuelCapacityPerHour
            );
    }
}