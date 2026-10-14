
using WoF.Simulation.Military;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Organization;

public class MilitaryHierarchyTests
{
    private static Division CreateDivision(string name = "Division A")
    {
        return new Division(
            name: name,
            position: new Province(1, "Test Province"),
            fuel: 100,
            ammunition: 100
        );
    }

    [Fact]
    public void Division_CanContainBattalionsWithCompanies()
    {
        var division = CreateDivision();

        var battalion = new Battalion(
            1, "I Battalion", BattalionType.Infantry);

        var companyA = new Company(
            1, "Company A", CompanyType.Infantry);

        var companyB = new Company(
            2, "Company B", CompanyType.HeavyWeapons);

        battalion.AddCompany(companyA);
        battalion.AddCompany(companyB);

        division.AddBattalion(battalion);

        Assert.Single(division.Battalions);
        Assert.Equal(2, battalion.Companies.Count);

        Assert.Same(division, battalion.ParentDivision);
        Assert.Same(battalion, companyA.ParentBattalion);
        Assert.Same(battalion, companyB.ParentBattalion);
    }

    [Fact]
    public void Company_CannotBelongToTwoBattalions()
    {
        var first = new Battalion(
            1, "First", BattalionType.Infantry);

        var second = new Battalion(
            2, "Second", BattalionType.Infantry);

        var company = new Company(
            1, "Company A", CompanyType.Infantry);

        first.AddCompany(company);

        Assert.Throws<InvalidOperationException>(
            () => second.AddCompany(company));

        Assert.Empty(second.Companies);
        Assert.Same(first, company.ParentBattalion);
    }

    [Fact]
    public void Battalion_CannotBelongToTwoDivisions()
    {
        var first = CreateDivision("First Division");
        var second = CreateDivision("Second Division");

        var battalion = new Battalion(
            1, "I Battalion", BattalionType.Infantry);

        first.AddBattalion(battalion);

        Assert.Throws<InvalidOperationException>(
            () => second.AddBattalion(battalion));

        Assert.Empty(second.Battalions);
        Assert.Same(first, battalion.ParentDivision);
    }

    [Fact]
    public void DuplicateIdentifiersWithinParent_AreRejected()
    {
        var division = CreateDivision();

        division.AddBattalion(new Battalion(
            1, "First", BattalionType.Infantry));

        Assert.Throws<InvalidOperationException>(
            () => division.AddBattalion(new Battalion(
                1, "Duplicate", BattalionType.Artillery)));

        var battalion = new Battalion(
            2, "Second", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "A", CompanyType.Infantry));

        Assert.Throws<InvalidOperationException>(
            () => battalion.AddCompany(new Company(
                1, "B", CompanyType.Infantry)));
    }

    [Fact]
    public void AddingHierarchy_DoesNotChangeExistingDivisionResources()
    {
        var division = CreateDivision();

        int initialManpower =
            division.Composition.Manpower.Current;

        var battalion = new Battalion(
            1, "I Battalion", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "A", CompanyType.Infantry));

        division.AddBattalion(battalion);

        Assert.Equal(
            initialManpower,
            division.Composition.Manpower.Current);
    }

    [Fact]
    public void LargeHierarchy_CanBeConstructed()
    {
        var divisions = new List<Division>();

        for (int d = 0; d < 100; d++)
        {
            var division = CreateDivision($"Division {d}");

            for (int b = 1; b <= 12; b++)
            {
                var battalion = new Battalion(
                    b,
                    $"Battalion {b}",
                    BattalionType.Infantry);

                for (int c = 1; c <= 4; c++)
                {
                    battalion.AddCompany(new Company(
                        c,
                        $"Company {c}",
                        CompanyType.Infantry));
                }

                division.AddBattalion(battalion);
            }

            divisions.Add(division);
        }

        Assert.Equal(100, divisions.Count);

        Assert.Equal(
            1200,
            divisions.Sum(d => d.Battalions.Count));

        Assert.Equal(
            4800,
            divisions.Sum(d =>
                d.Battalions.Sum(b => b.Companies.Count)));
    }
}
