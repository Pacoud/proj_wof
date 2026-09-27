using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Simulation.Logistics;

public sealed class SupplyDepot
{
    public string Name { get; }

    public Province Position { get; }

    public double FuelStock { get; private set; }

    public double FuelTransferPerHour { get; }

    public  SupplyDepot(
        string name,
        Province position,
        double fuelStock,
        double fuelTransferPerHour)
    {
        Name = name;
        Position = position;
        FuelStock = fuelStock;
        FuelTransferPerHour = fuelTransferPerHour;
    }

    public double WithdrawFuel(double requestedAmount)
    {
        if (requestedAmount <= 0)
            return 0;

        double withdrawn = Math.Min(
            requestedAmount,
            FuelStock
        );

        FuelStock -= withdrawn;

        return withdrawn;
    }
}
