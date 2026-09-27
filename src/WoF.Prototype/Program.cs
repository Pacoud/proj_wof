using WoF.Simulation.Core;
using WoF.Simulation.Logistics;
using WoF.Simulation.Military;
using WoF.Simulation.World;

var provinceA = new Province(1, "Province A");

var division = new Division(
    "1re Division",
    provinceA,
    fuel: 20,
    ammunition: 100
);

var depot = new SupplyDepot(
    "Dépôt principal",
    provinceA,
    fuelStock: 500,
    fuelTransferPerHour: 20
);

var simulation = new SimulationEngine();

simulation.AddDivision(division);
simulation.AddSupplyDepot(depot);

for (int i = 0; i < 5; i++)
{
    Console.WriteLine(
        $"Heure {simulation.Clock.CurrentHour} | " +
        $"Division : {division.Fuel}/{division.FuelCapacity} | " +
        $"Dépôt : {depot.FuelStock}"
    );

    simulation.Tick();
}