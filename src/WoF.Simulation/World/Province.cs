namespace WoF.Simulation.World;

public sealed class Province
{
    public int Id { get; }

    public TerrainType Terrain {get; }

    public string Name { get; }

    public Country? Owner { get; }

    public Country? Controller { get; private set; }


    private readonly List<Province> _neighbours = new();

    public IReadOnlyList<Province> Neighbours => _neighbours;

    public Province(int id, string name, TerrainType terrain = TerrainType.Plains, Country? owner = null, Country? controller = null)
    {
        Id = id;
        Name = name;
        Terrain = terrain;

        Owner = owner; 

        Controller = controller ?? owner;
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


    public void ChangeController(Country? newController)
    {
        Controller = newController;
    }
}
