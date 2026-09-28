using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Logistics;

public sealed class SupplyPath
{
    public IReadOnlyList<InfrastructureLink> Links { get; }

    public double Capacity { get; }

    public SupplyPath(
        IReadOnlyList<InfrastructureLink> links)
    {
        Links = links;

        Capacity = links.Count == 0
            ? double.PositiveInfinity
            : links.Min(
                link => link.TransportCapacityPerHour
            );
    }
}