using WoF.Simulation.World;

namespace WoF.Simulation.Logistics;

public sealed class SupplyRoute
{
    public Province ProvinceA { get; }

    public Province ProvinceB { get; }

    public double FuelCapacityPerHour { get; }

    public SupplyRoute(
        Province provinceA,
        Province provinceB,
        double fuelCapacityPerHour)
    {
        if (!provinceA.IsNeighbourOf(provinceB))
        {
            throw new ArgumentException(
                "A supply route can only connect neighbouring provinces."
            );
        }

        ProvinceA = provinceA;
        ProvinceB = provinceB;
        FuelCapacityPerHour = fuelCapacityPerHour;
    }

    public bool Connects(
        Province first,
        Province second)
    {
        return
            (ReferenceEquals(ProvinceA, first) &&
             ReferenceEquals(ProvinceB, second))
            ||
            (ReferenceEquals(ProvinceA, second) &&
             ReferenceEquals(ProvinceB, first));
    }
}