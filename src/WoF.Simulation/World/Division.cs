using WoF.Simulation.World;

namespace WoF.Simulation.Military;

public sealed class Division
{
    private const double MovementFuelCost = 8;

    public string Name { get; }

    public Province Position { get; private set; }

    public double Fuel { get; private set; }

    public double Ammunition { get; private set; }

    public Division(
        string name,
        Province position,
        double fuel,
        double ammunition)
    {
        Name = name;
        Position = position;
        Fuel = fuel;
        Ammunition = ammunition;
    }

    public bool MoveTo(Province destination)
    {
        if (!Position.IsNeighbourOf(destination))
            return false;

        if (Fuel < MovementFuelCost)
            return false;

        Fuel -= MovementFuelCost;
        Position = destination;

        return true;
    }
}