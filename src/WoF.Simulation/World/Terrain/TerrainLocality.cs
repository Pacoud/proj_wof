namespace WoF.Simulation.World.Terrain;

public enum TerrainLocalityKind
{
    Village,
    Town,
    City
}

/// <summary>
/// Localité générée : ses cases portent l'élément Buildings.
/// Cells est trié par indice croissant.
/// </summary>
public sealed record TerrainLocality(
    TerrainLocalityKind Kind,
    int CenterCell,
    IReadOnlyList<int> Cells);
