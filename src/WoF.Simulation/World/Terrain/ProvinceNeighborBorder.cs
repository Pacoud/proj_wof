namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Frontière avec une province voisine, vue depuis la province décrite.
///
/// BearingDegrees : relèvement de la voisine, 0° = nord, 90° = est.
/// RoadLevel : nombre de routes qui franchissent la frontière (0 à 3).
/// Les deux côtés d'une même frontière doivent recevoir le même RoadLevel.
/// </summary>
public sealed record ProvinceNeighborBorder
{
    public int NeighborProvinceId { get; }
    public double BearingDegrees { get; }
    public int RoadLevel { get; }

    public ProvinceNeighborBorder(
        int neighborProvinceId,
        double bearingDegrees,
        int roadLevel = 0)
    {
        if (!double.IsFinite(bearingDegrees)
            || bearingDegrees < 0
            || bearingDegrees >= 360)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bearingDegrees));
        }

        if (roadLevel < 0 || roadLevel > 3)
            throw new ArgumentOutOfRangeException(
                nameof(roadLevel));

        NeighborProvinceId = neighborProvinceId;
        BearingDegrees = bearingDegrees;
        RoadLevel = roadLevel;
    }
}
