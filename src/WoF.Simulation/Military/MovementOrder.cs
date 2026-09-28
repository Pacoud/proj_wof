using WoF.Simulation.World;

namespace WoF.Simulation.Military;

public sealed class MovementOrder
{
    public Province Destination { get; }

    public int TotalHours { get; }

    public int RemainingHours { get; private set; }

    public double FuelPerHour {get; }

    public bool IsPaused {get; private set; }

    public int ElapsedHours => TotalHours - RemainingHours;

    public bool IsCompleted => RemainingHours == 0;


    public MovementOrder(
        Province destination,
        MovementPlan plan)
    {
        Destination = destination;
        TotalHours = plan.DurationHours;
        RemainingHours = plan.DurationHours;
        FuelPerHour = plan.FuelPerHour;
    }

    public void AdvanceOneHour()
    {
        if (IsPaused)
            return;

        if (RemainingHours > 0)
        {
            RemainingHours--;
        }
    }

    public void Pause()
    {
        IsPaused = true;
    }

    public void Resume()
    {
        IsPaused = false;
    }

}