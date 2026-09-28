using WoF.Simulation.Military;
using WoF.Simulation.Time;
using WoF.Simulation.Logistics;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Core;

public sealed class SimulationEngine
{
    public SupplyNetwork SupplyNetwork { get; } = new();

    private readonly List<Division> _divisions = new();

    public SimulationClock Clock { get; } = new();

    private readonly List<SupplyDepot> _supplyDepots = new();

    public IReadOnlyList<SupplyDepot> SupplyDepots => _supplyDepots;

    public IReadOnlyList<Division> Divisions => _divisions;

    public void AddDivision(Division division)
    {
        if (!_divisions.Contains(division))
        {
            _divisions.Add(division);
        }
    }

    public void Tick()
    {
        Clock.AdvanceOneHour();

        foreach (var division in _divisions)
        {
            division.AdvanceOneHour();
        }

    // Une division venant d'arriver dans une province
    // peut être ravitaillée.
    SupplyNetwork.ProcessFuelSupply(
        _supplyDepots,
        _divisions
    );

    // Puis elle peut commencer son segment suivant.
    StartQueuedMovements();
    
    }

    private void StartQueuedMovements()
    {
        foreach (var division in _divisions)
        {
            if (division.IsInTransit)
                continue;

            if (division.NextQueuedDestination == null)
                continue;

            TryStartNextSegment(division);
        }
    }

    public void AddSupplyDepot(SupplyDepot depot)
    {
    if (!_supplyDepots.Contains(depot))
    {
        _supplyDepots.Add(depot);
    }
    }

    public void AddInfrastructureLink(InfrastructureLink link)
    {
        SupplyNetwork.AddLink(link);
    }

    public bool TryOrderMoveTo(
    Division division,
    Province destination)
    {
        Province? routingStart =
            GetRoutingStart(division);

        if (routingStart == null)
            return false;

        var path =
            MilitaryPathfinder.FindFastestPath(
                routingStart,
                destination,
                SupplyNetwork.Links,
                division.Type
            );

        if (path == null)
            return false;

        // Le premier élément est routingStart,
        // donc on ne le met pas dans la file.
        division.ReplacePlannedRoute(
            path.Skip(1)
        );

        if (!division.IsInTransit)
        {
            TryStartNextSegment(division);
        }

        return true;
    }

    private Province? GetRoutingStart(
    Division division)
    {
        if (division.IsInTransit)
        {
            return division.Transit!.Destination;
        }

        return division.CurrentProvince;
    }


    private bool TryStartNextSegment(
    Division division)
    {
        if (division.IsInTransit)
            return false;

        if (division.CurrentProvince == null)
            return false;

        Province? destination =
            division.NextQueuedDestination;

        if (destination == null)
            return false;

        MovementPlan plan =
            MovementSystem.CreateMovementPlan(
                division.CurrentProvince,
                destination,
                SupplyNetwork.Links,
                division.Type
            );

        bool started =
            division.TryStartMovement(
                destination,
                plan
            );

        if (started)
        {
            division.ConfirmNextSegmentStarted();
        }

        return started;
    }

    public bool TryOrderExplicitPath(
    Division division,
    IReadOnlyList<Province> waypoints)
    {
        if (waypoints.Count == 0)
            return false;

        Province? start =
            GetRoutingStart(division);

        if (start == null)
            return false;

        Province current = start;

        // On valide tout avant de modifier
        // l'ordre existant.
        foreach (var waypoint in waypoints)
        {
            if (!current.IsNeighbourOf(waypoint))
            {
                return false;
            }

            current = waypoint;
        }

        division.ReplacePlannedRoute(
            waypoints
        );

        if (!division.IsInTransit)
        {
            TryStartNextSegment(division);
        }

        return true;
    }


}
