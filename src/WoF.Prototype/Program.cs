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

var panzerDivision = new Division(
    name: "1re Division blindée",
    position: provinceA,
    fuel: 100,
    ammunition: 100,
    type: DivisionType.Armored
);

var depot = new SupplyDepot(
    "Dépôt principal",
    provinceA,
    fuelStock: 500,
    fuelTransferPerHour: 20
);


var france =
    new Country(1, "France");

var germany =
    new Country(2, "Germany");

var province =
    new Province(
        id: 1,
        name: "Province A",
        terrain: TerrainType.Plains,
        owner: france
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

