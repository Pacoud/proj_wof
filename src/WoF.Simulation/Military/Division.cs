using WoF.Simulation.World;
using WoF.Simulation.Military.Combat;

namespace WoF.Simulation.Military;

public sealed class Division
{
    public string Name { get; }

    public double Fuel { get; private set; }
    
    public double FuelCapacity {get;}

    public double Ammunition { get; private set; }

    public Country? Country { get; }

    public Engagement? CurrentEngagement { get; private set; }

    public Province? CurrentProvince { get; private set; }

    public TransitState? Transit { get; private set; }

    public MovementRoute? PlannedRoute { get; private set; }

    public Province? NextQueuedDestination =>
    PlannedRoute?.NextWaypoint;

    public bool IsInTransit =>
    Transit != null;

    public bool IsMoving => Transit != null && !Transit.IsPaused;

    public bool IsEngaged =>
    CurrentEngagement != null;

    public IReadOnlyList<Province> QueuedDestinations =>
    PlannedRoute?.RemainingWaypoints
    ?? Array.Empty<Province>();

    public DivisionType Type {get; }


    public double MissingFuel =>
    Math.Max(0, FuelCapacity - Fuel);

    public Division(
        string name,
        Province position,
        double fuel,
        double ammunition,
        double fuelCapacity = 100,
        DivisionType type = DivisionType.Infantry,
        Country? country = null)
    {
        Name = name;
        CurrentProvince = position;
        Fuel = fuel;
        Ammunition = ammunition;
        FuelCapacity = fuelCapacity;
        Type = type;
        Country = country;
    }

    public bool TryStartMovement(
        Province destination,
        MovementPlan plan)
    {
        if (IsInTransit)
            return false;
        
        if(CurrentProvince == null)
            return false;
        

        if (!CurrentProvince.IsNeighbourOf(destination))
            return false;

        if (plan.DurationHours <= 0)
            return false;

        if (plan.FuelPerHour < 0)
            return false;

        if (Fuel < plan.FuelPerHour)
            return false;

        Province origin = CurrentProvince;

        Transit = new TransitState(
            origin,
            destination,
            plan
        );
        
        CurrentProvince = null;

        return true;
    }

    public void AdvanceOneHour()
    {
        if (Transit == null)
            return;

        Transit.BeginTick();

        if (IsEngaged)
            return;

        if (Transit.IsPaused)
            return;

        double fuelNeeded =
            Transit.FuelPerHour;

        if (Fuel < fuelNeeded)
        {
            Transit.Pause();
            return;
        }

        ConsumeFuel(fuelNeeded);

        Transit.AdvanceOneHour();

        if (Transit.IsCompleted)
        {
            CurrentProvince =
                Transit.Destination;

            Transit = null;
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
        if (Transit == null)
            return false;

        if (Transit.IsPaused)
            return false;

        Transit.Pause();

        return true;
    }

    public bool ResumeMovement()
    {
        if (Transit == null)
            return false;

        if (!Transit.IsPaused)
            return false;

        if (Fuel < Transit.FuelPerHour)
            return false;

        Transit.Resume();

        return true;
    }

    public void ReplacePlannedRoute(
    IEnumerable<Province> destinations)
    {
        var route = destinations.ToList();

        PlannedRoute =
            route.Count == 0
                ? null
                : new MovementRoute(route);
    }

    public void ConfirmNextSegmentStarted()
    {
        if (PlannedRoute == null)
            return;

        PlannedRoute.ConsumeNextWaypoint();

        if (PlannedRoute.IsEmpty)
        {
            PlannedRoute = null;
        }
    }

    public void ClearPlannedRoute()
    {
        PlannedRoute = null;
    }


    public DivisionOperationalState OperationalState
    {
        get
        {
            if (IsEngaged)
                return DivisionOperationalState.Engaged;

            if (Transit == null)
                return DivisionOperationalState.Stationary;

            if (Transit.IsPaused)
                return DivisionOperationalState.Paused;

            return DivisionOperationalState.Moving;
        }
    }

    internal void JoinEngagement(
    Engagement engagement)
    {
        if (CurrentEngagement != null)
            return;

        CurrentEngagement = engagement;

        if (Transit != null)
        {
            Transit.Pause();
        }
    }

    public double ConsumeFuel(
    double amount)
    {
        if (amount <= 0)
            return 0;

        double consumed =
            Math.Min(amount, Fuel);

        Fuel = Math.Round(
            Fuel - consumed,
            2
        );

        return consumed;
    }

}
