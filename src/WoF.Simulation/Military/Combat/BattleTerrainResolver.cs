using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat;

public static class BattleTerrainResolver
{
    /// <summary>Province de la rencontre, selon la progression depuis A au contact.</summary>
    public static Province ResolveMeetingProvince(
        Province connectionA, Province connectionB, double progressFromA)
    {
        if (!double.IsFinite(progressFromA) || progressFromA < 0 || progressFromA > 1)
            throw new ArgumentOutOfRangeException(nameof(progressFromA));

        ArgumentNullException.ThrowIfNull(connectionA);
        ArgumentNullException.ThrowIfNull(connectionB);
        return progressFromA < 0.5 ? connectionA : connectionB;
    }

    // Conserver le point d'entrée des appelants qui demandent seulement le terrain.
    public static TerrainType ResolveMeetingTerrain(
        Province connectionA, Province connectionB, double progressFromA) =>
        ResolveMeetingProvince(connectionA, connectionB, progressFromA).Terrain;
}