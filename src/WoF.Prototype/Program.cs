using WoF.Simulation.Military;
using WoF.Simulation.Time;
using WoF.Simulation.World;

var clock = new SimulationClock();

var provinceA = new Province(1, "Province A");
var provinceB = new Province(2, "Province B");

provinceA.ConnectTo(provinceB);

var division = new Division(
    name: "1re Division",
    position: provinceA,
    fuel: 100,
    ammunition: 100
);

Console.WriteLine("=== WoF SIMULATION ===");
Console.WriteLine();

Console.WriteLine(
    $"Heure {clock.CurrentHour} : " +
    $"{division.Name} est dans {division.Position.Name}"
);

bool accepted = division.TryMoveTo(provinceB);

Console.WriteLine(
    accepted
        ? "Ordre de déplacement accepté."
        : "Ordre refusé."
);

for (int i = 0; i < 4; i++)
{
    clock.AdvanceOneHour();
    division.AdvanceOneHour();

    Console.WriteLine();

    Console.WriteLine($"Heure {clock.CurrentHour}");

    if (division.IsMoving)
    {
        Console.WriteLine(
            $"Division en déplacement vers " +
            $"{division.CurrentMovement!.Destination.Name}"
        );

        Console.WriteLine(
            $"Temps restant : " +
            $"{division.CurrentMovement.RemainingHours} h"
        );
    }
    else
    {
        Console.WriteLine(
            $"Division présente dans {division.Position.Name}"
        );
    }

    Console.WriteLine($"Carburant : {division.Fuel}");
}