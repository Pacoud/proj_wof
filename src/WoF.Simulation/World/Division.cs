using WoF.Simulation.World;

namespace WoF.Simulation.Military;

public sealed class Division
{
    private const double MovementFuelCost = 8;
    private const int MovementDurationHours = 3;

    public string Name { get; }

    public Province Position { get; private set; }

    public double Fuel { get; private set; }

    public double Ammunition { get; private set; }

    public MovementOrder? CurrentMovement { get; private set; }

    public bool IsMoving => CurrentMovement != null;

    public Division(
        string name,
        Province position,
        double fuel,
        double ammunition)
    {
        Name = name;
        Position = position;
        Fuel = fuel;
        Ammunition = ammunition;
    }

    public bool TryMoveTo(Province destination)
    {
        if (IsMoving)
            return false;

        if (!Position.IsNeighbourOf(destination))
            return false;

        if (Fuel < MovementFuelCost)
            return false;

        Fuel -= MovementFuelCost;

        CurrentMovement = new MovementOrder(
            destination,
            MovementDurationHours
        );

        return true;
    }

    public void AdvanceOneHour()
    {
        if (CurrentMovement == null)
            return;

        CurrentMovement.AdvanceOneHour();

        if (CurrentMovement.IsCompleted)
        {
            Position = CurrentMovement.Destination;
            CurrentMovement = null;
        }
    }
}