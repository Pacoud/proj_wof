
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Organization;

public class HierarchicalPersonnelLossTests
{
    private static (
        Division Division,
        Company A,
        Company B
    ) CreateTestFormation()
    {
        var division = new Division(
            name: "Division Test",
            position: new Province(1, "Test"),
            fuel: 100,
            ammunition: 100
        );

        var battalion = new Battalion(
            1, "I Battalion", BattalionType.Infantry);

        var companyA = new Company(
            1, "A", CompanyType.Infantry,
            new CompanyComposition(60));

        var companyB = new Company(
            2, "B", CompanyType.Infantry,
            new CompanyComposition(40));

        battalion.AddCompany(companyA);
        battalion.AddCompany(companyB);
        division.AddBattalion(battalion);

        return (division, companyA, companyB);
    }

    [Fact]
    public void Losses_AreDistributedProportionally()
    {
        var (division, a, b) = CreateTestFormation();

        var results = HierarchicalPersonnelLossApplier.Apply(
            division,
            new PersonnelCasualtyReport(
                KilledInAction: 10,
                WoundedInAction: 5,
                MissingOrCaptured: 5));

        Assert.Equal(48, a.Composition!.Manpower.Current);
        Assert.Equal(32, b.Composition!.Manpower.Current);

        var totals = division.GetHierarchicalResourceTotals();

        Assert.Equal(80, totals.Personnel.Available);
        Assert.Equal(10, totals.Personnel.KilledInAction);
        Assert.Equal(5, totals.Personnel.TotalWoundedInAction);
        Assert.Equal(5, totals.Personnel.MissingOrCaptured);

        Assert.Equal(2, results.Count);

        Assert.Equal(
            10,
            results.Sum(r => r.Casualties.KilledInAction));

        Assert.Equal(
            5,
            results.Sum(r => r.Casualties.WoundedInAction));
    }

    [Fact]
    public void ExcessiveLosses_DoNotModifyAnyCompany()
    {
        var (division, a, b) = CreateTestFormation();

        Assert.Throws<InvalidOperationException>(
            () => HierarchicalPersonnelLossApplier.Apply(
                division,
                new PersonnelCasualtyReport(120, 0, 0)));

        Assert.Equal(60, a.Composition!.Manpower.Current);
        Assert.Equal(40, b.Composition!.Manpower.Current);
    }

    [Fact]
    public void MissingComposition_IsRejectedBeforeMutation()
    {
        var (division, a, _) = CreateTestFormation();

        var battalion = new Battalion(
            2, "II Battalion", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "Unallocated", CompanyType.Infantry));

        division.AddBattalion(battalion);

        Assert.Throws<InvalidOperationException>(
            () => HierarchicalPersonnelLossApplier.Apply(
                division,
                new PersonnelCasualtyReport(5, 0, 0)));

        Assert.Equal(60, a.Composition!.Manpower.Current);
    }

    [Fact]
    public void Losses_UseRemainingAvailablePersonnel()
    {
        var (division, a, b) = CreateTestFormation();

        // Une partie de la compagnie A a déjà été perdue.
        a.Composition!.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(50, 0, 0));

        HierarchicalPersonnelLossApplier.Apply(
            division,
            new PersonnelCasualtyReport(10, 0, 0));

        // A possède 10 hommes, B en possède 40.
        // Répartition : 2 pertes pour A, 8 pour B.
        Assert.Equal(8, a.Composition.Manpower.Current);
        Assert.Equal(32, b.Composition!.Manpower.Current);
    }

    [Fact]
    public void LegacyDivisionResources_AreNotModified()
    {
        var (division, _, _) = CreateTestFormation();

        int legacyBefore =
            division.Composition.Manpower.Current;

        HierarchicalPersonnelLossApplier.Apply(
            division,
            new PersonnelCasualtyReport(5, 3, 2));

        Assert.Equal(
            legacyBefore,
            division.Composition.Manpower.Current);

        Assert.Equal(
            90,
            division.GetHierarchicalResourceTotals()
                .Personnel.Available);
    }

    [Fact]
    public void NegativeLosses_AreRejected()
    {
        var (division, a, _) = CreateTestFormation();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => HierarchicalPersonnelLossApplier.Apply(
                division,
                new PersonnelCasualtyReport(-1, 0, 0)));

        Assert.Equal(60, a.Composition!.Manpower.Current);
    }
}
