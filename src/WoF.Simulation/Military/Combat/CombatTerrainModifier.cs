using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat;

public static class CombatTerrainModifier
{
    public static double GetPressureModifier(
        DivisionType divisionType,
        TerrainType terrain)
    {
        return (divisionType, terrain) switch
        {
            // INFANTRY

            (DivisionType.Infantry,
             TerrainType.Plains)
                => 1.00,

            (DivisionType.Infantry,
             TerrainType.Desert)
                => 0.95,

            (DivisionType.Infantry,
             TerrainType.Hills)
                => 0.95,

            (DivisionType.Infantry,
             TerrainType.Forest)
                => 0.95,

            (DivisionType.Infantry,
             TerrainType.Urban)
                => 1.00,

            (DivisionType.Infantry,
             TerrainType.Marsh)
                => 0.85,

            (DivisionType.Infantry,
             TerrainType.Mountain)
                => 0.90,


            // MOTORIZED

            (DivisionType.Motorized,
             TerrainType.Plains)
                => 1.05,

            (DivisionType.Motorized,
             TerrainType.Desert)
                => 1.00,

            (DivisionType.Motorized,
             TerrainType.Hills)
                => 0.85,

            (DivisionType.Motorized,
             TerrainType.Forest)
                => 0.80,

            (DivisionType.Motorized,
             TerrainType.Urban)
                => 0.85,

            (DivisionType.Motorized,
             TerrainType.Marsh)
                => 0.65,

            (DivisionType.Motorized,
             TerrainType.Mountain)
                => 0.65,


            // ARMORED

            (DivisionType.Armored,
             TerrainType.Plains)
                => 1.15,

            (DivisionType.Armored,
             TerrainType.Desert)
                => 1.05,

            (DivisionType.Armored,
             TerrainType.Hills)
                => 0.75,

            (DivisionType.Armored,
             TerrainType.Forest)
                => 0.65,

            (DivisionType.Armored,
             TerrainType.Urban)
                => 0.65,

            (DivisionType.Armored,
             TerrainType.Marsh)
                => 0.45,

            (DivisionType.Armored,
             TerrainType.Mountain)
                => 0.45,

            _ => 1.00
        };
    }
}