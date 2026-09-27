using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.World;

var provinceA = new Province(1, "Province A");
var provinceB = new Province(2, "Province B");

provinceA.ConnectTo(provinceB);

var division1 = new Division(
    name: "1re Division",
    position: provinceA,
    fuel: 100,
    ammunition: 100
);

var division2 = new Division(
    name: "2e Division",
    position: provinceA,
    fuel: 100,
    ammunition: 100
);

var simulation = new SimulationEngine();

simulation.AddDivision(division1);
simulation.AddDivision(division2);

division1.TryMoveTo(provinceB);
division2.TryMoveTo(provinceB);

for (int i = 0; i < 3; i++)
{
    simulation.Tick();

    Console.WriteLine($"Heure {simulation.Clock.CurrentHour}");

    foreach (var division in simulation.Divisions)
    {
        Console.WriteLine(
            $"{division.Name} | " +
            $"Position : {division.Position.Name} | " +
            $"En mouvement : {division.IsMoving}"
        );
    }

    Console.WriteLine();
}