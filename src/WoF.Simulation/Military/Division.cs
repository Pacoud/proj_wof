using WoF.Simulation.World;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Retreat;

namespace WoF.Simulation.Military;

public sealed class Division
{
    public string Name { get; }

    public double Fuel { get; private set; }
    
    public double FuelCapacity {get;}

    public double MaxOrganization { get; }

    public double Organization { get; private set; }

    public double Ammunition { get; private set; }

    public Country? Country { get; }

    public Engagement? CurrentEngagement { get; private set; }

    public Province? CurrentProvince { get; private set; }

    public TransitState? Transit { get; private set; }

    public MovementRoute? PlannedRoute { get; private set; }

    public Province? NextQueuedDestination =>
    PlannedRoute?.NextWaypoint;

    public bool IsBroken =>
    Organization <= 0;

    public bool IsInTransit =>
    Transit != null;

    public bool IsMoving => Transit != null && !Transit.IsPaused;

    public bool IsEngaged =>
    CurrentEngagement != null;

    public RetreatPhase RetreatPhase { get; private set; }
    = RetreatPhase.None;

    public bool IsRetreating =>
    RetreatPhase != RetreatPhase.None;

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
        Country? country = null,
        double maxOrganization = 100)
    {
        if (maxOrganization <= 0)
            {
                throw new ArgumentOutOfRangeException(
                nameof(maxOrganization)
                );
            }

        Name = name;
        CurrentProvince = position;

        Fuel = fuel;
        Ammunition = ammunition;
        FuelCapacity = fuelCapacity;

        Type = type;
        Country = country;

        MaxOrganization = maxOrganization;
        Organization = maxOrganization;
    }

    public bool TryStartMovement(
        Province destination,
        MovementPlan plan)
    {   
        return TryStartMovementInternal(
            destination,
            plan,
            allowBroken: false
        );
    }

    internal bool TryStartRetreatMovement(
        Province destination,
        MovementPlan plan)

    {
        if (!IsRetreating)
            return false;

        return TryStartMovementInternal(
            destination,
            plan,
            allowBroken: true
        );
    }

    private bool TryStartMovementInternal(
        Province destination,
        MovementPlan plan,
        bool allowBroken)
    { 
        if (IsInTransit)
        return false;

        if (CurrentProvince == null)
            return false;

        if (IsBroken && !allowBroken)
            return false;

        if (!CurrentProvince.IsNeighbourOf(destination))
            return false;

        if (plan.DurationHours <= 0)
            return false;

        if (plan.FuelPerHour < 0)
            return false;

        if (!allowBroken && Fuel < plan.FuelPerHour)
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

        if (IsRetreating)
        {
            ConsumeFuel(fuelNeeded);
        }
        else 
        {
            if (Fuel < fuelNeeded)
            {
                Transit.Pause();
                return;
            }

        ConsumeFuel(fuelNeeded);
        }

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
            
            if (IsRetreating)
                return DivisionOperationalState.Retreating;

            if (IsBroken)
                return DivisionOperationalState.Broken;

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
        if (IsBroken)
            return;

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


    public double LoseOrganization(double amount)
    {
        if (amount <= 0)
            return 0;

        double lost =
            Math.Min(amount, Organization);

        Organization = Math.Round(
            Organization - lost,
            2
        );

        return lost;
    }

    internal void LeaveEngagement(
    Engagement engagement)
    {
        if (!ReferenceEquals(
                CurrentEngagement,
                engagement))
        {
            return;
        }

        CurrentEngagement = null;
    }

    internal void BeginRetreat(
    IEnumerable<Province> route)
    {
        ClearPlannedRoute();

        ReplacePlannedRoute(route);

        RetreatPhase =
            RetreatPhase.MovingToSafety;
    }

    internal void CompleteRetreat()
    {
        if (IsInTransit)
            return;

        if (NextQueuedDestination != null)
            return;

        RetreatPhase =
            RetreatPhase.None;
    }


    public bool StopMovement()
    {
        if (IsRetreating)
            return false;

        if (Transit == null)
            return false;

        if (Transit.IsPaused)
            return false;

        Transit.Pause();

        return true;
    }

        public bool ResumeMovement()
    {   
        if (IsBroken)
            return false;   

        if (Transit == null)
            return false;

        if (!Transit.IsPaused)
            return false;

        if (Fuel < Transit.FuelPerHour)
            return false;

        Transit.Resume();

        return true;
    }

    internal bool BeginReturnToOriginRetreat()
    {
        if (!IsBroken)
            return false;

        if (Transit == null)
            return false;

        ClearPlannedRoute();

        Transit.Reverse();

        RetreatPhase =
            RetreatPhase.ReturningToOrigin;

        return true;
    }


    internal void ResumeTransitAfterEngagement()
    {
        if (IsBroken)
            return;

        if (Transit == null)
            return;

        Transit.Resume();
    }


}
