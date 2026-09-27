using GrandStrategy.Simulation.World;

namespace GrandStrategy.Simulation.Military;

public sealed class Division
{
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
}