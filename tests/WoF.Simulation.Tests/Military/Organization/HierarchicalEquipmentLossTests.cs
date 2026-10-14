
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Organization;

public class HierarchicalEquipmentLossTests
{
    private static (
        Division Division,
        Company A,
        Company B
    ) CreateFormation()
    {
        var division = new Division(
            name: "Test Division",
            position: new Province(1, "Test"),
            fuel: 100,
            ammunition: 100
        );

        var battalion = new Battalion(
            1, "I Battalion", BattalionType.Infantry);

        var a = new Company(
            1, "A", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 120,
                infantryEquipment: 90,
                artillery: 3,
                trucks: 2
            ));

        var b = new Company(
            2, "B", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 90,
                infantryEquipment: 30,
                artillery: 1,
                trucks: 6
            ));

        battalion.AddCompany(a);
        battalion.AddCompany(b);
        division.AddBattalion(battalion);

        return (division, a, b);
    }

    [Fact]
    public void Losses_AreDistributedAcrossCompanies()
    {
        var (division, a, b) = CreateFormation();

        var allocations =
            HierarchicalEquipmentLossApplier.Apply(
                division,
                new EquipmentLossReport(
                    InfantryEquipment: 40,
                    Artillery: 2,
                    Trucks: 4
                ));

        Assert.Equal(
            60, a.Composition!.InfantryEquipment.Current);

        Assert.Equal(
            20, b.Composition!.InfantryEquipment.Current);

        Assert.Equal(1, a.Composition.Artillery.Current);
        Assert.Equal(1, b.Composition.Artillery.Current);

        Assert.Equal(1, a.Composition.Trucks.Current);
        Assert.Equal(3, b.Composition.Trucks.Current);

        var totals =
            division.GetHierarchicalResourceTotals();

        Assert.Equal(80, totals.InfantryEquipment.Available);
        Assert.Equal(2, totals.Artillery.Available);
        Assert.Equal(4, totals.Trucks.Available);

        Assert.Equal(
            40,
            allocations.Sum(
                x => x.Losses.InfantryEquipment));

        Assert.Equal(
            2,
            allocations.Sum(
                x => x.Losses.Artillery));

        Assert.Equal(
            4,
            allocations.Sum(
                x => x.Losses.Trucks));
    }

    [Fact]
    public void ImpossibleLosses_DoNotModifyAnyStock()
    {
        var (division, a, b) = CreateFormation();

        // Artillerie : 4 disponibles, 5 demandés.
        // Les autres demandes sont valides.
        Assert.Throws<InvalidOperationException>(
            () => HierarchicalEquipmentLossApplier.Apply(
                division,
                new EquipmentLossReport(
                    InfantryEquipment: 20,
                    Artillery: 5,
                    Trucks: 3
                )));

        Assert.Equal(
            90, a.Composition!.InfantryEquipment.Current);

        Assert.Equal(
            30, b.Composition!.InfantryEquipment.Current);

        Assert.Equal(3, a.Composition.Artillery.Current);
        Assert.Equal(1, b.Composition.Artillery.Current);

        Assert.Equal(2, a.Composition.Trucks.Current);
        Assert.Equal(6, b.Composition.Trucks.Current);
    }

    [Fact]
    public void NegativeLosses_AreRejected()
    {
        var (division, a, _) = CreateFormation();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => HierarchicalEquipmentLossApplier.Apply(
                division,
                new EquipmentLossReport(-1, 0, 0)));

        Assert.Equal(
            90, a.Composition!.InfantryEquipment.Current);
    }

    [Fact]
    public void MissingComposition_IsRejectedBeforeMutation()
    {
        var (division, a, _) = CreateFormation();

        var battalion = new Battalion(
            2, "II Battalion", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "Unallocated", CompanyType.Infantry));

        division.AddBattalion(battalion);

        Assert.Throws<InvalidOperationException>(
            () => HierarchicalEquipmentLossApplier.Apply(
                division,
                new EquipmentLossReport(10, 0, 0)));

        Assert.Equal(
            90, a.Composition!.InfantryEquipment.Current);
    }

    [Fact]
    public void ZeroLosses_DoNotChangeResources()
    {
        var (division, _, _) = CreateFormation();

        var before =
            division.GetHierarchicalResourceTotals();

        var result =
            HierarchicalEquipmentLossApplier.Apply(
                division,
                new EquipmentLossReport(0, 0, 0));

        var after =
            division.GetHierarchicalResourceTotals();

        Assert.Empty(result);
        Assert.Equal(before, after);
    }

    [Fact]
    public void HierarchicalLosses_DoNotChangeLegacyStocks()
    {
        var (division, _, _) = CreateFormation();

        int legacyEquipment =
            division.Composition.InfantryEquipment.Current;

        HierarchicalEquipmentLossApplier.Apply(
            division,
            new EquipmentLossReport(20, 1, 2));

        Assert.Equal(
            legacyEquipment,
            division.Composition.InfantryEquipment.Current);
    }
}
