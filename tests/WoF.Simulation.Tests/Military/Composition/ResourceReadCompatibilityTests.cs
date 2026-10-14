
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Fuel;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Power;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Composition;

public class ResourceReadCompatibilityTests
{
    [Fact]
    public void Snapshot_ReflectsDivisionResources()
    {
        var composition = new DivisionComposition(
            manpower: 1000,
            infantryEquipment: 850,
            artillery: 12,
            tanks: 20,
            trucks: 50
        );

        composition.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(5, 12, 3));

        composition.Tanks.ApplyExpectedLosses(
            new TankLossExpectation(2, 1));

        var totals =
            FormationResourceTotals.Capture(composition);

        Assert.Equal(980, totals.Personnel.Available);
        Assert.Equal(5, totals.Personnel.KilledInAction);
        Assert.Equal(12, totals.Personnel.TotalWoundedInAction);
        Assert.Equal(3, totals.Personnel.MissingOrCaptured);

        Assert.Equal(850, totals.InfantryEquipment.Available);
        Assert.Equal(12, totals.Artillery.Available);

        Assert.Equal(17, totals.Tanks.Operational);
        Assert.Equal(2, totals.Tanks.Damaged);
        Assert.Equal(1, totals.Tanks.Destroyed);
    }

    [Fact]
    public void CombatCalculators_KeepIdenticalResults()
    {
        var composition = new DivisionComposition(
            manpower: 8000,
            infantryEquipment: 6000,
            artillery: 36,
            tanks: 100,
            trucks: 400
        );

        var totals =
            FormationResourceTotals.Capture(composition);

        var oldPower =
            CompositionCombatPowerCalculator.Calculate(
                composition);

        var newPower =
            CompositionCombatPowerCalculator.Calculate(
                totals);

        Assert.Equal(oldPower, newPower);

        var oldFuel =
            CompositionCombatFuelCalculator.Calculate(
                composition);

        var newFuel =
            CompositionCombatFuelCalculator.Calculate(
                totals);

        Assert.Equal(oldFuel, newFuel);
    }

    [Fact]
    public void DivisionRead_UsesCurrentLegacyState()
    {
        var division = new Division(
            name: "Test Division",
            position: new Province(1, "A"),
            fuel: 100,
            ammunition: 100
        );

        var before = division.GetCurrentResourceTotals();

        division.Composition.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(4, 8, 0));

        var after = division.GetCurrentResourceTotals();

        Assert.Equal(
            before.Personnel.Available - 12,
            after.Personnel.Available);

        Assert.Equal(4, after.Personnel.KilledInAction);
        Assert.Equal(8, after.Personnel.TotalWoundedInAction);
    }
}
