using WoF.Simulation.World;

namespace WoF.Simulation.Military;

public sealed class TransitState
{
    public Province Origin { get; private set; }

    public Province Destination { get; private set; }

    public int TotalHours { get; }

    public int RemainingHours { get; private set; }

    public double PreviousProgress { get; private set; }

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

    public void BeginTick()
    {
        PreviousProgress = Progress;
    }

    public void Reverse() // Gère les cas ou la division est vaincue dans un combat inter provinces
    {
        if (IsCompleted)
            return;

        Province oldOrigin = Origin;

        int oldElapsedHours =
            ElapsedHours;

        Origin = Destination;
        Destination = oldOrigin;

        RemainingHours = oldElapsedHours;

        IsPaused = false;


        PreviousProgress = Progress;
    }
}
