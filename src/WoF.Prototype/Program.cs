using WoF.Simulation.Military;
using WoF.Simulation.World;

var provinceA = new Province(1, "Province A");
var provinceB = new Province(2, "Province B");
var provinceC = new Province(3, "Province C");

provinceA.ConnectTo(provinceB);

var division = new Division(
    name: "1re Division",
    position: provinceA,
    fuel: 100,
    ammunition: 100
);

Console.WriteLine("=== GRAND STRATEGY PROTOTYPE ===");
Console.WriteLine();

Console.WriteLine(
    $"{division.Name} se trouve dans {division.Position.Name}"
);

Console.WriteLine($"Carburant : {division.Fuel}");

Console.WriteLine();
Console.WriteLine("Ordre : déplacement vers Province B");

bool success = division.MoveTo(provinceB);

Console.WriteLine(
    success
        ? "Déplacement réussi."
        : "Déplacement impossible."
);

Console.WriteLine($"Position : {division.Position.Name}");
Console.WriteLine($"Carburant : {division.Fuel}");

Console.WriteLine();
Console.WriteLine("Ordre : déplacement vers Province C");

success = division.MoveTo(provinceC);

Console.WriteLine(
    success
        ? "Déplacement réussi."
        : "Déplacement impossible."
);