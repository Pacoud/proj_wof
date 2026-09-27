using WoF.Simulation.Military;
using WoF.Simulation.Time;

namespace WoF.Simulation.Core;

public sealed class SimulationEngine
{
    private readonly List<Division> _divisions = new();

    public SimulationClock Clock { get; } = new();

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
    }
}