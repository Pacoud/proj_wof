using WoF.Simulation.World;

namespace WoF.Simulation.Military;

public sealed class Division
{
    public string Name { get; }

    public Province Position { get; private set; }

    public double Fuel { get; private set; }
    
    public double FuelCapacity {get;}

    public double Ammunition { get; private set; }

    public MovementOrder? CurrentMovement { get; private set; }

    public bool IsInTransit =>
    CurrentMovement != null;


    public bool IsMoving => CurrentMovement != null && !CurrentMovement.IsPaused;

    public DivisionType Type {get; }


    public double MissingFuel =>
    Math.Max(0, FuelCapacity - Fuel);

    public Division(
        string name,
        Province position,
        double fuel,
        double ammunition,
        double fuelCapacity = 100,
        DivisionType type = DivisionType.Infantry)
    {
        Name = name;
        Position = position;
        Fuel = fuel;
        Ammunition = ammunition;
        FuelCapacity = fuelCapacity;
        Type = type;
    }

    public bool TryMoveTo(
        Province destination,
        MovementPlan plan)
    {
        if (IsInTransit)
            return false;

        if (!Position.IsNeighbourOf(destination))
            return false;

        if (plan.DurationHours <= 0)
            return false;
        
        if (plan.FuelPerHour < 0)
            return false;
        
        if (Fuel < plan.FuelPerHour)
            return false;

        CurrentMovement = new MovementOrder(
            destination,
            plan
        );

        return true;
    }

    public void AdvanceOneHour()
    {
        if (CurrentMovement == null)
            return;

        if (CurrentMovement.IsPaused)
            return;
        
        double fuelNeeded = 
            CurrentMovement.FuelPerHour;
        
        if (Fuel < fuelNeeded)
        {
            CurrentMovement.Pause();
            return;
        }

        Fuel = Math.Round(
            Fuel - fuelNeeded,
            2
        );

        CurrentMovement.AdvanceOneHour();

        if (CurrentMovement.IsCompleted)
        {
            Position = CurrentMovement.Destination;
            CurrentMovement = null;
        }
    }

    public double ReceiveFuel(double amount)
    {
        if (amount <= 0)
        return 0;

    double availableSpace = FuelCapacity - Fuel;

    if (availableSpace <= 0)
        return 0;

    double received = Math.Min(amount, availableSpace);

    Fuel += received;

    return received;
    }

    public bool StopMovement()
    {
    if (CurrentMovement == null)
        return false;

    if (CurrentMovement.IsPaused)
        return false;

    CurrentMovement.Pause();

    return true;
    }

    public bool ResumeMovement()
    {
    if (CurrentMovement == null)
        return false;

    if (!CurrentMovement.IsPaused)
        return false;

    if (Fuel < CurrentMovement.FuelPerHour)
        return false;

    CurrentMovement.Resume();

    return true;
    }


}
