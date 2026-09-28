using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Military;

public static class MovementSystem
{
    private const int BaseMovementDurationHours = 6;

    public static int CalculateMovementDuration(
        Province origin,
        Province destination,
        IEnumerable<InfrastructureLink> links,
        DivisionType divisionType = DivisionType.Infantry)
  

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
                destination.Terrain,
                divisionType
            );

            double divisionMultiplier =
                GetDivisionMovementMultiplier(divisionType);
        
        return Math.Max(
            1,
            (int)Math.Ceiling(
                infrastructureDuration
                * terrainMultiplier
                * divisionMultiplier
            )
        );

    }
    private static double GetTerrainMovementMultiplier(
        TerrainType terrain,
        DivisionType divisionType)
        {
            return divisionType switch
        {
            DivisionType.Infantry =>
                terrain switch
                {
                    TerrainType.Plains => 1.00,
                    TerrainType.Urban => 1.15,
                    TerrainType.Desert => 1.20,
                    TerrainType.Forest => 1.30,
                    TerrainType.Hills => 1.40,
                    TerrainType.Marsh => 1.60,
                    TerrainType.Mountain => 2.00,
                    _ => 1.00
                },

            DivisionType.Motorized =>
                terrain switch
                {
                    TerrainType.Plains => 1.00,
                    TerrainType.Urban => 1.25,
                    TerrainType.Desert => 1.10,
                    TerrainType.Forest => 1.50,
                    TerrainType.Hills => 1.70,
                    TerrainType.Marsh => 2.20,
                    TerrainType.Mountain => 2.60,
                    _ => 1.00
                },

            DivisionType.Armored =>
                terrain switch
                {
                    TerrainType.Plains => 1.00,
                    TerrainType.Urban => 1.35,
                    TerrainType.Desert => 1.15,
                    TerrainType.Forest => 1.60,
                    TerrainType.Hills => 1.80,
                    TerrainType.Marsh => 2.50,
                    TerrainType.Mountain => 3.00,
                    _ => 1.00
                },

                    _ => 1.00

        };
    }

    private static double GetDivisionMovementMultiplier(
    DivisionType divisionType)
    {
    return divisionType switch
    {
        DivisionType.Infantry => 1.00,
        DivisionType.Motorized => 0.60,
        DivisionType.Armored => 0.75,

        _ => 1.00
    };
    }
 }