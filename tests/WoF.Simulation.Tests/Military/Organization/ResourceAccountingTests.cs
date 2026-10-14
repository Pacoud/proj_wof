
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Organization;

public class ResourceAccountingTests
{
    private static Division CreateTestDivision()
    {
        return new Division(
            name: "1re Division",
            position: new Province(1, "Test"),
            fuel: 100,
            ammunition: 100
        );
    }

    [Fact]
    public void Battalion_AggregatesCompanyResources()
    {
        var battalion = new Battalion(
            1, "I Battalion", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "A", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 160,
                infantryEquipment: 140,
                trucks: 3)));

        battalion.AddCompany(new Company(
            2, "B", CompanyType.HeavyWeapons,
            new CompanyComposition(
                manpower: 140,
                infantryEquipment: 120,
                artillery: 2,
                tanks: 4)));

        var totals = battalion.GetResourceTotals();

        Assert.Equal(300, totals.Personnel.Available);
        Assert.Equal(300, totals.Personnel.Authorized);

        Assert.Equal(
            260, totals.InfantryEquipment.Available);

        Assert.Equal(2, totals.Artillery.Available);
        Assert.Equal(4, totals.Tanks.Operational);
        Assert.Equal(3, totals.Trucks.Available);
    }

    [Fact]
    public void CompanyLosses_UpdateBattalionAndDivisionTotals()
    {
        var division = CreateTestDivision();

        var battalion = new Battalion(
            1, "I Battalion", BattalionType.Infantry);

        var companyA = new Company(
            1, "A", CompanyType.Infantry,
            new CompanyComposition(160, 140));

        var companyB = new Company(
            2, "B", CompanyType.Infantry,
            new CompanyComposition(140, 120));

        battalion.AddCompany(companyA);
        battalion.AddCompany(companyB);
        division.AddBattalion(battalion);

        var before = division.GetHierarchicalResourceTotals();

        companyA.Composition!.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 5,
                WoundedInAction: 10,
                MissingOrCaptured: 2));

        companyA.Composition.InfantryEquipment.ApplyLoss(8);

        var after = division.GetHierarchicalResourceTotals();

        Assert.Equal(300, before.Personnel.Available);
        Assert.Equal(283, after.Personnel.Available);

        Assert.Equal(5, after.Personnel.KilledInAction);
        Assert.Equal(10, after.Personnel.TotalWoundedInAction);
        Assert.Equal(10, after.Personnel.WoundedUnavailable);
        Assert.Equal(2, after.Personnel.MissingOrCaptured);

        Assert.Equal(
            252, after.InfantryEquipment.Available);

        // L'ancien relevé est une copie, pas une vue mutable.
        Assert.Equal(300, before.Personnel.Available);
    }

    [Fact]
    public void UnallocatedCompany_CannotProduceIncompleteTotals()
    {
        var battalion = new Battalion(
            1, "I Battalion", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "A", CompanyType.Infantry));

        Assert.Throws<InvalidOperationException>(
            () => battalion.GetResourceTotals());
    }

    [Fact]
    public void CompanyComposition_CannotBeShared()
    {
        var resources = new CompanyComposition(160, 140);

        var first = new Company(
            1, "A", CompanyType.Infantry, resources);

        Assert.Same(resources, first.Composition);

        Assert.Throws<InvalidOperationException>(
            () => new Company(
                2, "B", CompanyType.Infantry, resources));
    }
}
