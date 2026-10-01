using WoF.Simulation.Military.Combat.Fuel;
using WoF.Simulation.Military.Combat.Power;

namespace WoF.Simulation.Tests.Combat.Fuel;

public class CombatFuelModifierTests
{
    [Fact]
    public void NoFuelPenalizesArmoredPowerMoreThanSmallArms()
    {
        var rawPower =
            new CombatPowerProfile(
                SmallArms: 10,
                Artillery: 10,
                Armored: 10
            );

        CombatPowerProfile adjusted =
            CombatFuelModifier.Apply(
                rawPower,
                fuelSatisfaction: 0
            );

        Assert.Equal(
            9.5,
            adjusted.SmallArms,
            2
        );

        Assert.Equal(
            7.0,
            adjusted.Artillery,
            2
        );

        Assert.Equal(
            2.0,
            adjusted.Armored,
            2
        );
    }

    [Fact]
    public void FullFuelLeavesCombatPowerUnchanged()
    {
        var rawPower =
            new CombatPowerProfile(
                SmallArms: 5,
                Artillery: 3,
                Armored: 8
            );

        CombatPowerProfile adjusted =
            CombatFuelModifier.Apply(
                rawPower,
                fuelSatisfaction: 1
            );

        Assert.Equal(
            rawPower,
            adjusted
        );
    }
}
