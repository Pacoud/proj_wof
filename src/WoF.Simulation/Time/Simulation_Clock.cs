namespace WoF.Simulation.Time;

public sealed class SimulationClock
{
    public long CurrentHour { get; private set; }

    public SimulationClock()
    {
        CurrentHour = 0;
    }

    public void AdvanceOneHour()
    {
        CurrentHour++;
    }
}