
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Organization;

public class HierarchicalTankLossTests
{
    private static (
        Division Division,
        Company A,
        Company B
    ) CreateFormation()
    {
        var division = new Division(
            "Armored Division",
            new Province(1, "Test"),
            fuel: 100,
            ammunition: 100
        );

        var battalion = new Battalion(
            1, "Tank Battalion", BattalionType.Armored);

        var a = new Company(
            1, "A", CompanyType.Armor,
            new CompanyComposition(
                manpower: 100, tanks: 3));

        var b = new Company(
            2, "B", CompanyType.Armor,
            new CompanyComposition(
                manpower: 80, tanks: 2));

        battalion.AddCompany(a);
        battalion.AddCompany(b);
        division.AddBattalion(battalion);

        return (division, a, b);
    }

    [Fact]
    public void FractionalLosses_AccumulateAcrossHours()
    {
        var (division, _, _) = CreateFormation();
        var applier =
            new HierarchicalTankLossApplier(division);

        var expectation =
            new TankLossExpectation(
                Damaged: 0,
                Destroyed: 0.4);

        Assert.Empty(applier.Apply(expectation));
        Assert.Empty(applier.Apply(expectation));

        var thirdHour = applier.Apply(expectation);

        Assert.Single(thirdHour);
        Assert.Equal(1, thirdHour[0].Losses.Destroyed);

        var totals = division.GetHierarchicalResourceTotals();

        Assert.Equal(4, totals.Tanks.Operational);
        Assert.Equal(1, totals.Tanks.Destroyed);
    }

    [Fact]
    public void DestroyedAndDamaged_AreKeptSeparate()
    {
        var (division, _, _) = CreateFormation();
        var applier =
            new HierarchicalTankLossApplier(division);

        applier.Apply(new TankLossExpectation(
            Damaged: 2,
            Destroyed: 2
        ));

        var totals = division.GetHierarchicalResourceTotals();

        Assert.Equal(1, totals.Tanks.Operational);
        Assert.Equal(2, totals.Tanks.Damaged);
        Assert.Equal(2, totals.Tanks.Destroyed);
    }

    [Fact]
    public void ExcessLosses_AreLimitedByOperationalTanks()
    {
        var (division, _, _) = CreateFormation();
        var applier =
            new HierarchicalTankLossApplier(division);

        applier.Apply(new TankLossExpectation(
            Damaged: 2,
            Destroyed: 10
        ));

        var totals = division.GetHierarchicalResourceTotals();

        Assert.Equal(0, totals.Tanks.Operational);
        Assert.Equal(0, totals.Tanks.Damaged);
        Assert.Equal(5, totals.Tanks.Destroyed);
    }

    [Fact]
    public void InvalidExpectation_DoesNotModifyStocks()
    {
        var (division, _, _) = CreateFormation();
        var applier =
            new HierarchicalTankLossApplier(division);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => applier.Apply(new TankLossExpectation(
                Damaged: double.NaN,
                Destroyed: 1
            )));

        var totals = division.GetHierarchicalResourceTotals();

        Assert.Equal(5, totals.Tanks.Operational);
        Assert.Equal(0, totals.Tanks.Destroyed);
    }

    [Fact]
    public void MissingComposition_IsRejectedBeforeMutation()
    {
        var (division, a, _) = CreateFormation();
        var extra = new Battalion(
            2, "Second", BattalionType.Armored);

        extra.AddCompany(new Company(
            1, "Unallocated", CompanyType.Armor));

        division.AddBattalion(extra);

        var applier =
            new HierarchicalTankLossApplier(division);

        Assert.Throws<InvalidOperationException>(
            () => applier.Apply(new TankLossExpectation(
                Damaged: 0,
                Destroyed: 1
            )));

        Assert.Equal(3, a.Composition!.Tanks.Operational);
    }

    [Fact]
    public void ResolvedLosses_CannotExceedCompanyStock()
    {
        var (_, a, _) = CreateFormation();

        Assert.Throws<InvalidOperationException>(
            () => a.Composition!.Tanks.ApplyResolvedLosses(
                new TankLossResult(
                    Damaged: 2,
                    Destroyed: 2
                )));

        Assert.Equal(3, a.Composition!.Tanks.Operational);
    }

    [Fact]
    public void HierarchicalLosses_DoNotChangeLegacyStock()
    {
        var (division, _, _) = CreateFormation();

        int before =
            division.Composition.Tanks.Operational;

        var applier =
            new HierarchicalTankLossApplier(division);

        applier.Apply(new TankLossExpectation(
            Damaged: 1,
            Destroyed: 2
        ));

        Assert.Equal(
            before,
            division.Composition.Tanks.Operational);

        Assert.Equal(
            2,
            division.GetHierarchicalResourceTotals()
                .Tanks.Destroyed);
    }
}
