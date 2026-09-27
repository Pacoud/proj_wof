using GrandStrategy.Simulation.Military;
using GrandStrategy.Simulation.World;

var provinceA = new Province(1, "Province A");
var provinceB = new Province(2, "Province B");

provinceA.AddNeighbour(provinceB);
provinceB.AddNeighbour(provinceA);

var division = new Division(
    name: "1re Division",
    position: provinceA,
    fuel: 100,
    ammunition: 100
);

Console.WriteLine("=== GRAND STRATEGY PROTOTYPE ===");
Console.WriteLine();

Console.WriteLine($"Division : {division.Name}");
Console.WriteLine($"Position : {division.Position.Name}");
Console.WriteLine($"Carburant : {division.Fuel}");
Console.WriteLine($"Munitions : {division.Ammunition}");

Console.WriteLine();

Console.WriteLine("Provinces voisines :");

foreach (var neighbour in division.Position.Neighbours)
{
    Console.WriteLine($"- {neighbour.Name}");
}