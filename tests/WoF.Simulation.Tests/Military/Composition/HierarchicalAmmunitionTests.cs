
using WoF.Simulation.Military;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Composition;

public sealed class HierarchicalAmmunitionTests
{
    [Fact]
    public void DefaultInventory_IsEmpty()
    {
        var company = new CompanyComposition(manpower: 100);

        Assert.Equal(
            new AmmunitionTotals(),
            company.Ammunition.Capture());
    }

    [Fact]
    public void ConsumptionAndResupply_Work()
    {
        var composition = new CompanyComposition(
            manpower: 120,
            ammunition: new AmmunitionTotals(
                smallArms: 12000,
                machineGun: 4000));

        var inventory = composition.Ammunition;

        Assert.True(inventory.TryConsume(
            AmmunitionType.SmallArms, 2400));

        Assert.Equal(9600,
            inventory.GetAvailable(AmmunitionType.SmallArms));

        inventory.Resupply(AmmunitionType.SmallArms, 500);

        Assert.Equal(10100,
            inventory.GetAvailable(AmmunitionType.SmallArms));
    }

    [Fact]
    public void InsufficientStock_DoesNotConsume()
    {
        var inventory = new CompanyComposition(
            manpower: 100,
            ammunition: new AmmunitionTotals(smallArms: 100))
            .Ammunition;

        Assert.False(inventory.TryConsume(
            AmmunitionType.SmallArms, 150));

        Assert.Equal(100,
            inventory.GetAvailable(AmmunitionType.SmallArms));
    }

    [Fact]
    public void InvalidQuantities_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AmmunitionTotals(smallArms: -1));

        var inventory = new CompanyComposition(100).Ammunition;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            inventory.TryConsume(AmmunitionType.SmallArms, -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            inventory.Resupply(AmmunitionType.SmallArms, -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            inventory.TryConsume((AmmunitionType)999, 0));

        Assert.Equal(
            new AmmunitionTotals(),
            inventory.Capture());
    }

    [Fact]
    public void Overflow_DoesNotMutate()
    {
        var inventory = new CompanyComposition(
            manpower: 100,
            ammunition: new AmmunitionTotals(
                smallArms: long.MaxValue))
            .Ammunition;

        Assert.Throws<OverflowException>(() =>
            inventory.Resupply(AmmunitionType.SmallArms, 1));

        Assert.Equal(
            long.MaxValue,
            inventory.GetAvailable(AmmunitionType.SmallArms));

        Assert.Throws<OverflowException>(() =>
        {
            _ = new AmmunitionTotals(smallArms: long.MaxValue)
                + new AmmunitionTotals(smallArms: 1);
        });
    }

    [Fact]
    public void Companies_HaveIndependentInventories()
    {
        var initial = new AmmunitionTotals(smallArms: 1000);

        var first = new CompanyComposition(
            manpower: 100, ammunition: initial);

        var second = new CompanyComposition(
            manpower: 100, ammunition: initial);

        Assert.NotSame(first.Ammunition, second.Ammunition);

        Assert.True(first.Ammunition.TryConsume(
            AmmunitionType.SmallArms, 200));

        Assert.Equal(800, first.Ammunition.Capture().SmallArms);
        Assert.Equal(1000, second.Ammunition.Capture().SmallArms);
    }

    [Fact]
    public void Hierarchy_AggregatesCompanyStocks()
    {
        var division = new Division(
            name: "Test Division",
            position: new Province(1, "Test"),
            fuel: 100,
            ammunition: 75.5,
            composition: new DivisionComposition(
                manpower: 200,
                infantryEquipment: 160,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Battalion", BattalionType.Infantry);

        var first = new Company(
            1, "A", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 80,
                ammunition: new AmmunitionTotals(
                    smallArms: 1000)));

        var second = new Company(
            2, "B", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 80,
                ammunition: new AmmunitionTotals(
                    smallArms: 800)));

        battalion.AddCompany(first);
        battalion.AddCompany(second);
        division.AddBattalion(battalion);

        var before = battalion.GetResourceTotals().Ammunition;

        Assert.Equal(1800, before.SmallArms);

        // Les munitions abstraites Legacy ne sont
        // pas converties en cartouches.
        Assert.Equal(75.5, division.Ammunition);

        division.EnableHierarchicalResources();

        Assert.Equal(
            DivisionResourceMode.Hierarchical,
            division.ResourceMode);

        Assert.True(first.Composition!.Ammunition.TryConsume(
            AmmunitionType.SmallArms, 300));

        var after = division.GetCurrentResourceTotals().Ammunition;

        Assert.Equal(1500, after.SmallArms);

        // L'ancien releve est immuable.
        Assert.Equal(1800, before.SmallArms);

        // L'ancien stock reste inchange.
        Assert.Equal(75.5, division.Ammunition);
    }
}
