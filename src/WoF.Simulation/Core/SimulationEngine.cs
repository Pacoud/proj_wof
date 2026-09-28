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

    public bool TryMoveDivision(
        Division division,
        Province destination)
   {
    // Division déjà sur une liaison :
    // l'ordre devient un ordre futur.
    if (division.IsInTransit)
    {
        return division.QueueDestination(
            destination
        );
    }

    return TryStartDivisionMovement(
        division,
        destination
    );
    }

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

        StartPendingMovements();

        SupplyNetwork.ProcessFuelSupply(
            _supplyDepots,
            _divisions
    );
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

    //Helper 
    private bool TryStartDivisionMovement(
    Division division,
    Province destination)
    {
        if (division.CurrentProvince == null)
            return false;

        MovementPlan plan =
            MovementSystem.CreateMovementPlan(
                division.CurrentProvince,
                destination,
                SupplyNetwork.Links,
                division.Type
            );

        return division.TryStartMovement(
            destination,
            plan
        );
    }

    private void StartPendingMovements()
    {
        foreach (var division in _divisions)
        {
            if (division.IsInTransit)
                continue;

            if (division.CurrentProvince == null)
                continue;

            if (division.PendingDestination == null)
                continue;

            Province destination =
                division.PendingDestination;

            bool started =
                TryStartDivisionMovement(
                    division,
                    destination
                );

            if (started)
            {
                division.ClearPendingDestination();
            }
        }
    }


}
