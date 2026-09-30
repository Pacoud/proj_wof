using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Power;
using WoF.Simulation.World;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Tests.Combat.Power;

public class CombatTerrainModifierTests
{
    [Fact]
    public void PlainsImproveArmoredComponentWithoutChangingSmallArms()
    {
        var rawPower =
            new CombatPowerProfile(
                SmallArms: 4,
                Artillery: 2,
                Armored: 10
            );

        CombatPowerProfile adjusted =
            CombatTerrainModifier.Apply(
                rawPower,
                TerrainType.Plains
            );

        Assert.Equal(
            4,
            adjusted.SmallArms,
            2
        );

        Assert.Equal(
            2,
            adjusted.Artillery,
            2
        );

        Assert.Equal(
            11.5,
            adjusted.Armored,
            2
        );
    }


    [Fact]
    public void MountainPenalizesEachComponentDifferently()
    {
        var rawPower =
            new CombatPowerProfile(
                SmallArms: 10,
                Artillery: 10,
                Armored: 10
            );

        CombatPowerProfile adjusted =
            CombatTerrainModifier.Apply(
                rawPower,
                TerrainType.Mountain
            );

        Assert.Equal(
            9.0,
            adjusted.SmallArms,
            2
        );

        Assert.Equal(
            7.5,
            adjusted.Artillery,
            2
        );

        Assert.Equal(
            4.5,
            adjusted.Armored,
            2
        );
    }


    [Fact]
    public void InfantryOnlyFormationIsNotAffectedByArmoredTerrainPenalty()
    {
        var rawPower =
            new CombatPowerProfile(
                SmallArms: 6,
                Artillery: 1,
                Armored: 0
            );

        CombatPowerProfile adjusted =
            CombatTerrainModifier.Apply(
                rawPower,
                TerrainType.Mountain
            );

        Assert.Equal(
            5.4,
            adjusted.SmallArms,
            2
        );

        Assert.Equal(
            0.75,
            adjusted.Artillery,
            2
        );

        Assert.Equal(
            0,
            adjusted.Armored
        );
    }

    [Fact]
    public void TerrainEffectDependsOnCompositionNotDivisionType()
    {
        var infantryComposition =
            new DivisionComposition(
                manpower: 10_000,
                infantryEquipment: 9_000,
                artillery: 48,
                tanks: 0
            );

        var mixedComposition =
            new DivisionComposition(
                manpower: 10_000,
                infantryEquipment: 7_000,
                artillery: 48,
                tanks: 100
            );

        CombatPowerProfile infantryRaw =
            CompositionCombatPowerCalculator
                .Calculate(
                    infantryComposition
                );

        CombatPowerProfile mixedRaw =
            CompositionCombatPowerCalculator
                .Calculate(
                    mixedComposition
                );

        CombatPowerProfile infantryMountain =
            CombatTerrainModifier.Apply(
                infantryRaw,
                TerrainType.Mountain
            );

        CombatPowerProfile mixedMountain =
            CombatTerrainModifier.Apply(
                mixedRaw,
                TerrainType.Mountain
            );

        double infantryRetention =
            infantryMountain.Total
            / infantryRaw.Total;

        double mixedRetention =
            mixedMountain.Total
            / mixedRaw.Total;

        Assert.True(
            mixedRetention
            <
            infantryRetention
        );
    }
}