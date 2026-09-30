using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat;

public static class BattleTerrainResolver
{
    public static TerrainType ResolveMeetingTerrain(
        Province connectionA,
        Province connectionB,
        double progressFromA)
    {
        if (progressFromA < 0
            || progressFromA > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(progressFromA)
            );
        }

        return progressFromA < 0.5
            ? connectionA.Terrain
            : connectionB.Terrain;
    }
}