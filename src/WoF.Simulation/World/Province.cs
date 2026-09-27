namespace WoF.Simulation.World;

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

    public void ConnectTo(Province province)
    {
        if (province == this)
            return;

        if (!_neighbours.Contains(province))
            _neighbours.Add(province);

        if (!province._neighbours.Contains(this))
            province._neighbours.Add(this);
    }

    public bool IsNeighbourOf(Province province)
    {
        return _neighbours.Contains(province);
    }
}