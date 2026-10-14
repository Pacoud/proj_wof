
namespace WoF.Simulation.Military.Reinforcements;

public sealed class ReinforcementReserve
{
    public string Name { get; }

    // Stock réellement détenu par la réserve.
    public ReinforcementCargo Stock { get; private set; }

    public ReinforcementReserve(
        string name,
        ReinforcementCargo initialStock)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Reserve name cannot be empty.",
                nameof(name));
        }

        initialStock.ValidateNonNegative();

        Name = name;
        Stock = initialStock;
    }

    internal void Withdraw(ReinforcementCargo cargo)
    {
        cargo.ValidateNonNegative();

        if (!Stock.Contains(cargo))
        {
            throw new InvalidOperationException(
                "Reserve cannot supply the requested cargo.");
        }

        Stock = new ReinforcementCargo(
            Personnel:
                Stock.Personnel - cargo.Personnel,

            InfantryEquipment:
                Stock.InfantryEquipment
                - cargo.InfantryEquipment,

            Artillery:
                Stock.Artillery - cargo.Artillery,

            Tanks:
                Stock.Tanks - cargo.Tanks,

            Trucks:
                Stock.Trucks - cargo.Trucks
        );
    }
}
