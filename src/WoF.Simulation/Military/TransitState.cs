using WoF.Simulation.World;

namespace WoF.Simulation.Military;

public sealed class TransitState
{
    public Province Origin { get; }

    public Province Destination { get; }

    public int TotalHours { get; }

    public int RemainingHours { get; private set; }

    public int ElapsedHours =>
        TotalHours - RemainingHours;

    public double Progress =>
        TotalHours == 0
            ? 1.0
            : (double)ElapsedHours / TotalHours;

    public double FuelPerHour { get; }

    public bool IsPaused { get; private set; }

    public bool IsCompleted =>
        RemainingHours == 0;

    public TransitState(
        Province origin,
        Province destination,
        MovementPlan plan)
    {
        Origin = origin;
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