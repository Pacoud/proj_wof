
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat.Tactical;

[Flags]
public enum SectorFeature
{
    None = 0,
    Woods = 1,
    Buildings = 2,
    Entrenchments = 4,
    Road = 8
}

public sealed class BattleSector
{
    public int Id { get; }
    public string Name { get; }
    public TerrainType DominantTerrain { get; }
    public int ElevationMeters { get; }
    public SectorFeature Features { get; }

    public BattleSector(
        int id,
        string name,
        TerrainType dominantTerrain,
        int elevationMeters = 0,
        SectorFeature features = SectorFeature.None)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Sector name cannot be empty.",
                nameof(name));

        Id = id;
        Name = name;
        DominantTerrain = dominantTerrain;
        ElevationMeters = elevationMeters;
        Features = features;
    }
}
