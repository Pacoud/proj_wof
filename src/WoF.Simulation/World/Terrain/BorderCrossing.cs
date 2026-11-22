namespace WoF.Simulation.World.Terrain;

public enum BorderCrossingKind
{
    Road,
    River
}

/// <summary>
/// Point de passage d'une route ou d'une rivière sur une frontière.
///
/// CanonicalParameter : position le long de la frontière, dans [0,1],
/// mesurée dans le sens horaire depuis la province d'identifiant le plus petit.
/// Elle est identique des deux côtés de la frontière.
/// LocalParameter : même position, dans le sens horaire de cette province
/// (égal à 1 − CanonicalParameter chez la province d'identifiant le plus grand).
/// CellIndex : case du bord du cadre où le tracé touche la frontière.
/// </summary>
public sealed record BorderCrossing(
    int NeighborProvinceId,
    BorderCrossingKind Kind,
    double CanonicalParameter,
    double LocalParameter,
    int CellIndex);
