using WoF.Simulation.World;

namespace WoF.Simulation.Military;

public sealed class MovementRoute
{
    private readonly Queue<Province> _remainingWaypoints;

    public IReadOnlyList<Province> RemainingWaypoints =>
        _remainingWaypoints.ToArray();

    public Province? NextWaypoint =>
        _remainingWaypoints.Count > 0
            ? _remainingWaypoints.Peek()
            : null;

    public bool IsEmpty =>
        _remainingWaypoints.Count == 0;

    public MovementRoute(
        IEnumerable<Province> waypoints)
    {
        _remainingWaypoints =
            new Queue<Province>(waypoints);
    }

    public void ConsumeNextWaypoint()
    {
        if (_remainingWaypoints.Count > 0)
        {
            _remainingWaypoints.Dequeue();
        }
    }
}