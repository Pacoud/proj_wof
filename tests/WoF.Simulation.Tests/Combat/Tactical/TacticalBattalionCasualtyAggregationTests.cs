
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalBattalionCasualtyAggregationTests
{
    [Fact]
    public void CompanyCasualtiesPropagateThroughBattalionToDivision()
    {
        var province = new Province(1, "Test");

        var division = new Division(
            "Division",
            province,
            fuel: 100,
            ammunition: 100,
            composition: new DivisionComposition(
                manpower: 200,
                infantryEquipment: 200,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Infantry Battalion",
            BattalionType.Infantry);

        var first = new Company(
            1, "A", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 100));

        var second = new Company(
            2, "B", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 100));

        battalion.AddCompany(first);
        battalion.AddCompany(second);

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        first.Composition!.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 4,
                WoundedInAction: 6,
                MissingOrCaptured: 0));

        var battalionTotals = battalion.GetResourceTotals();
        var divisionTotals = division.GetCurrentResourceTotals();

        Assert.Equal(90, first.Composition.Manpower.Current);
        Assert.Equal(100, second.Composition!.Manpower.Current);

        Assert.Equal(190, battalionTotals.Personnel.Available);
        Assert.Equal(190, divisionTotals.Personnel.Available);

        Assert.Equal(4, battalionTotals.Personnel.KilledInAction);
        Assert.Equal(6, battalionTotals.Personnel.WoundedUnavailable);

        Assert.Equal(4, divisionTotals.Personnel.KilledInAction);
        Assert.Equal(6, divisionTotals.Personnel.WoundedUnavailable);
    }
}
