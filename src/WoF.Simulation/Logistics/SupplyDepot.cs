using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Simulation.Logistics;

public sealed class SupplyDepot
{
    public string Name { get; }

    public Province Position { get; }

    public double FuelStock { get; private set; }

    public double FuelTransferPerHour { get; }

    public SupplyDepot(
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

    public double Supply(IEnumerable<Division> divisions)
    {
        double remainingTransferCapacity = FuelTransferPerHour;
        double totalTransferred = 0;

        foreach (var division in divisions)
        {
            if (remainingTransferCapacity <= 0)
                break;

            if (FuelStock <= 0)
                break;

            if (division.IsMoving)
                continue;

            if (!ReferenceEquals(division.Position, Position))
                continue;

            double possibleTransfer = Math.Min(
                remainingTransferCapacity,
                FuelStock
            );

            double actuallyReceived =
                division.ReceiveFuel(possibleTransfer);

            FuelStock -= actuallyReceived;
            remainingTransferCapacity -= actuallyReceived;
            totalTransferred += actuallyReceived;
        }

        return totalTransferred;
    }
}