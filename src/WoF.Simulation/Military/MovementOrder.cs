using WoF.Simulation.World;

namespace WoF.Simulation.Military;

public sealed class MovementOrder
{
    public Province Destination { get; }

    public int TotalHours { get; }

    public int RemainingHours { get; private set; }

    public MovementOrder(
        Province destination,
        int totalHours)
    {
        Destination = destination;
        TotalHours = totalHours;
        RemainingHours = totalHours;
    }

    public void AdvanceOneHour()
    {
        if (RemainingHours > 0)
        {
            RemainingHours--;
        }
    }

    public bool IsCompleted => RemainingHours == 0;
}