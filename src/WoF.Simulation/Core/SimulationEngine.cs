using WoF.Simulation.Military;
using WoF.Simulation.Time;
using WoF.Simulation.Logistics;

namespace WoF.Simulation.Core;

public sealed class SimulationEngine
{

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
        
        foreach (var depot in _supplyDepots)
        {
            depot.Supply(_divisions);
        }
    }

    public void AddSupplyDepot(SupplyDepot depot)
    {
    if (!_supplyDepots.Contains(depot))
    {
        _supplyDepots.Add(depot);
    }
    }
}