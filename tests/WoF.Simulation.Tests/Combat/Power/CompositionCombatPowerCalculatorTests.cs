using WoF.Simulation.Military.Combat.Power;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Tests.Combat.Power;

public class CompositionCombatPowerCalculatorTests
{
    [Fact]
    public void InfantryCompositionProducesExpectedPowerBreakdown()
    {
        var composition =
            new DivisionComposition(
                manpower: 10_000,
                infantryEquipment: 9_000,
                artillery: 48,
                tanks: 0
            );

        CombatPowerProfile power =
            CompositionCombatPowerCalculator
                .Calculate(
                    composition
                );

        Assert.Equal(
            4.50,
            power.SmallArms,
            2
        );

        Assert.Equal(
            1.44,
            power.Artillery,
            2
        );

        Assert.Equal(
            0,
            power.Armored
        );

        Assert.Equal(
            5.94,
            power.Total,
            2
        );
    }

    [Fact]
    public void ArmoredCompositionDerivesMostPowerFromTanks()
    {
        var composition =
            new DivisionComposition(
                manpower: 8_000,
                infantryEquipment: 5_000,
                artillery: 36,
                tanks: 180
            );

        CombatPowerProfile power =
            CompositionCombatPowerCalculator
                .Calculate(
                    composition
                );

        Assert.Equal(
            6.30,
            power.Armored,
            2
        );

        Assert.True(
            power.Armored
            >
            power.SmallArms
        );

        Assert.Equal(
            9.88,
            power.Total,
            2
        );
    }

    [Fact]
    public void TankLossesReduceArmoredCombatPower()
    {
        var composition =
            new DivisionComposition(
                manpower: 8_000,
                infantryEquipment: 5_000,
                artillery: 36,
                tanks: 180
            );

        CombatPowerProfile before =
            CompositionCombatPowerCalculator
                .Calculate(
                    composition
                );

        composition
            .Tanks
            .ApplyLoss(
                90
            );

        CombatPowerProfile after =
            CompositionCombatPowerCalculator
                .Calculate(
                    composition
                );

        Assert.Equal(
            180,
            composition.Tanks.Authorized
        );

        Assert.Equal(
            90,
            composition.Tanks.Current
        );

        Assert.True(
            after.Armored
            <
            before.Armored
        );

        Assert.True(
            after.Total
            <
            before.Total
        );
    }

    [Fact]
    public void ManpowerLossesReduceAvailableCombatPower()
    {
        var composition =
            new DivisionComposition(
                manpower: 8_000,
                infantryEquipment: 5_000,
                artillery: 36,
                tanks: 180
            );

        CombatPowerProfile before =
            CompositionCombatPowerCalculator
                .Calculate(
                    composition
                );

        composition
            .Manpower
            .ApplyLoss(
                4_000
            );

        CombatPowerProfile after =
            CompositionCombatPowerCalculator
                .Calculate(
                    composition
                );

        Assert.True(
            after.SmallArms
            <
            before.SmallArms
        );

        Assert.True(
            after.Artillery
            <
            before.Artillery
        );

        Assert.True(
            after.Armored
            <
            before.Armored
        );

        Assert.True(
            after.Total
            <
            before.Total
        );
    }

    [Fact]
    public void EquipmentWithoutManpowerProducesNoCombatPower()
    {
        var composition =
            new DivisionComposition(
                manpower:
                    new StrengthPool(
                        authorized: 0,
                        current: 0
                    ),

                infantryEquipment:
                    new StrengthPool(
                        authorized: 5_000,
                        current: 5_000
                    ),

                artillery:
                    new StrengthPool(
                        authorized: 36,
                        current: 36
                    ),

                tanks:
                    new StrengthPool(
                        authorized: 180,
                        current: 180
                    )
            );

        CombatPowerProfile power =
            CompositionCombatPowerCalculator
                .Calculate(
                    composition
                );

        Assert.Equal(
            0,
            power.Total
        );
    }


}