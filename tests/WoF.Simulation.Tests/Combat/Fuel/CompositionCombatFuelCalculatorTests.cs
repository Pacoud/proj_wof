using WoF.Simulation.Military.Combat.Fuel;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Tests.Combat.Fuel;

public class CompositionCombatFuelCalculatorTests
{
    [Fact]
    public void ArmoredCompositionConsumesMostFuelThroughTanks()
    {
        var composition =
            new DivisionComposition(
                manpower: 8_000,
                infantryEquipment: 5_000,
                artillery: 36,
                tanks: 180,
                trucks: 600
            );

        CombatFuelDemandProfile demand =
            CompositionCombatFuelCalculator
                .Calculate(
                    composition
                );

        Assert.Equal(
            1.20,
            demand.Trucks,
            2
        );

        Assert.Equal(
            0.18,
            demand.Artillery,
            2
        );

        Assert.Equal(
            3.60,
            demand.Tanks,
            2
        );

        Assert.Equal(
            4.98,
            demand.Total,
            2
        );
    }


    [Fact]
    public void TankLossesReduceCombatFuelDemand()
    {
        var composition =
            new DivisionComposition(
                manpower: 8_000,
                infantryEquipment: 5_000,
                artillery: 36,
                tanks: 180,
                trucks: 600
            );

        CombatFuelDemandProfile before =
            CompositionCombatFuelCalculator
                .Calculate(
                    composition
                );

        composition.Tanks.ApplyExpectedLosses(
            new TankLossExpectation(Damaged: 0, Destroyed: 120)
        );

        CombatFuelDemandProfile after =
            CompositionCombatFuelCalculator
                .Calculate(
                    composition
                );

        Assert.Equal(
            60,
            composition.Tanks.Operational
        );

        Assert.Equal(0, composition.Tanks.Damaged);
        Assert.Equal(120, composition.Tanks.Destroyed);

        Assert.True(
            after.Tanks
            <
            before.Tanks
        );

        Assert.True(
            after.Total
            <
            before.Total
        );
    }

    [Fact]
    public void EquipmentWithoutManpowerConsumesNoCombatFuel()
    {
        var composition =
            new DivisionComposition(
                manpower:
                    new PersonnelPool(
                        authorized: 8_000,
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
                    new TankPool(
                        authorized: 180,
                        operational: 180
                    ),

                trucks:
                    new StrengthPool(
                        authorized: 600,
                        current: 600
                    )
            );

        CombatFuelDemandProfile demand =
            CompositionCombatFuelCalculator
                .Calculate(
                    composition
                );

        Assert.Equal(
            0,
            demand.Total
        );
    }
}
