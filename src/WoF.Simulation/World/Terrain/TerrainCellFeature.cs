namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Éléments portés par une case du terrain généré.
/// Le constructeur tactique les traduit en SectorFeature.
/// </summary>
[Flags]
public enum TerrainCellFeature : byte
{
    None = 0,
    Woods = 1,
    Buildings = 2,
    Road = 4
}
