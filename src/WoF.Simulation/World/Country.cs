namespace WoF.Simulation.World;

public sealed class Country
{
    public int Id { get; }

    public string Name { get; }

    public Country(int id, string name)
    {
        Id = id;
        Name = name;
    }
}