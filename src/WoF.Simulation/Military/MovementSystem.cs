using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Military;

public static class MovementSystem
{
    private const int BaseMovementDurationHours = 6;

    public static int CalculateMovementDuration(
        Province origin,
        Province destination,
        IEnumerable<InfrastructureLink> links)
    {
        if (!origin.IsNeighbourOf(destination))
        {
            throw new ArgumentException(
                "Movement is only possible between neighbouring provinces."
            );
        }

        var bestRoad = links
            .Where(link =>
                link.Type == InfrastructureType.Road
                &&
                link.Connects(origin, destination))
            .OrderByDescending(link => link.Level)
            .FirstOrDefault();
        
        int infrastructureDuration;

        // Aucune route :
        // déplacement possible mais lent.
        if (bestRoad == null)
        {
            infrastructureDuration = BaseMovementDurationHours;
        }
        else
        {
            infrastructureDuration = bestRoad.Level switch
            {
                1 => 5,
                2 => 4,
                3 => 3,

                _ => BaseMovementDurationHours
            };
        }

        double terrainMultiplier = 
            GetTerrainMovementMultiplier(
                destination.Terrain
            );
        
        return (int)Math.Ceiling(
            infrastructureDuration
            * terrainMultiplier
        );

    }
    private static double GetTerrainMovementMultiplier(
        TerrainType terrain)
        {
            return terrain switch
            {
                TerrainType.Plains => 1.00,
                TerrainType.Urban => 1.15,
                TerrainType.Desert => 1.20,
                TerrainType.Forest => 1.30,
                TerrainType.Hills => 1.40,
                TerrainType.Marsh => 1.60,
                TerrainType.Mountain => 2.00,

                _ => 1.00

            };
        }
 }