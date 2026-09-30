using WoF.Simulation.World;

namespace WoF.Simulation.Military;

public sealed class TransitState
{
    public Province Origin { get; private set; }

    public Province Destination { get; private set; }

    public int TotalHours { get; }

    public double RemainingHours  => Math.Max(
            0,
            TotalHours - ElapsedHours
        );

    public double ElapsedHours { get; private set; }

    public double PreviousProgress { get; private set; }

    public double Progress =>
        TotalHours == 0
            ? 1.0
            : Math.Clamp (ElapsedHours / TotalHours,
            0.0,
            1.0
            );

    public double FuelPerHour { get; }

    public bool IsPaused { get; private set; }

    public bool IsCompleted =>
        ElapsedHours >= TotalHours;


    public TransitState(
        Province origin,
        Province destination,
        MovementPlan plan)
    {
        Origin = origin;
        Destination = destination;

        TotalHours = plan.DurationHours;
        ElapsedHours = 0;

        FuelPerHour = plan.FuelPerHour;
    }

    public void AdvanceOneHour()
    {
        if (IsPaused)
            return;

        ElapsedHours = 
        Math.Min(
            TotalHours,
            ElapsedHours + 1.0
        );
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

    internal void SynchronizeProgress(
        double progress)
        {
            if (progress  < 0
            || progress > 1)
            {
               throw new ArgumentOutOfRangeException(
                nameof(progress)
                ); 
            }    
            ElapsedHours = TotalHours * progress;

            PreviousProgress = progress; 
        }

    public void Reverse() // Gère les cas ou la division est vaincue dans un combat inter provinces
    {
        if (IsCompleted)
            return;

        Province oldOrigin = Origin;

        double oldElapsedHours =
            ElapsedHours;

        Origin = Destination;
        Destination = oldOrigin;

        ElapsedHours = TotalHours - oldElapsedHours;

        IsPaused = false;


        PreviousProgress = Progress;
    }
}
