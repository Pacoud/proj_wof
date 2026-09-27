namespace GrandStrategy.Simulation.World;

public sealed class Province
{
    public int Id { get; }

    public string Name { get; }

    private readonly List<Province> _neighbours = new();

    public IReadOnlyList<Province> Neighbours => _neighbours;

    public Province(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public void AddNeighbour(Province province)
    {
        if (!_neighbours.Contains(province))
        {
            _neighbours.Add(province);
        }
    }
}