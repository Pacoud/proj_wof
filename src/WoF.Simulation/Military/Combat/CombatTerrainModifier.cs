using WoF.Simulation.Military.Combat.Power;
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat;

public static class CombatTerrainModifier
{
    public static TerrainCombatModifiers GetModifiers(
        TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Plains =>
                new TerrainCombatModifiers(
                    SmallArms: 1.00,
                    Artillery: 1.00,
                    Armored: 1.15
                ),

            TerrainType.Desert =>
                new TerrainCombatModifiers(
                    SmallArms: 0.95,
                    Artillery: 1.00,
                    Armored: 1.05
                ),

            TerrainType.Hills =>
                new TerrainCombatModifiers(
                    SmallArms: 0.95,
                    Artillery: 0.90,
                    Armored: 0.75
                ),

            TerrainType.Forest =>
                new TerrainCombatModifiers(
                    SmallArms: 0.95,
                    Artillery: 0.85,
                    Armored: 0.65
                ),

            TerrainType.Urban =>
                new TerrainCombatModifiers(
                    SmallArms: 1.00,
                    Artillery: 0.90,
                    Armored: 0.65
                ),

            TerrainType.Marsh =>
                new TerrainCombatModifiers(
                    SmallArms: 0.85,
                    Artillery: 0.70,
                    Armored: 0.45
                ),

            TerrainType.Mountain =>
                new TerrainCombatModifiers(
                    SmallArms: 0.90,
                    Artillery: 0.75,
                    Armored: 0.45
                ),

            _ =>
                new TerrainCombatModifiers(
                    SmallArms: 1.00,
                    Artillery: 1.00,
                    Armored: 1.00
                )
        };
    }

    public static CombatPowerProfile Apply(
        CombatPowerProfile power,
        TerrainType terrain)
    {
        TerrainCombatModifiers modifiers =
            GetModifiers(
                terrain
            );

        return new CombatPowerProfile(
            SmallArms:
                Math.Round(
                    power.SmallArms
                    * modifiers.SmallArms,
                    4
                ),

            Artillery:
                Math.Round(
                    power.Artillery
                    * modifiers.Artillery,
                    4
                ),

            Armored:
                Math.Round(
                    power.Armored
                    * modifiers.Armored,
                    4
                )
        );
    }
}