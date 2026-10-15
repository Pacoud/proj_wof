
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Organization;

public class HierarchicalLossOptimizationTests
{
    private static Division CreateFormation(
        bool withTanks = true)
    {
        int tanks = withTanks ? 5 : 0;

        var division = new Division(
            "Test Division",
            new Province(1, "Test"),
            fuel: 100,
            ammunition: 100,
            composition: new DivisionComposition(
                manpower: 100,
                infantryEquipment: 0,
                artillery: 0,
                tanks: tanks,
                trucks: 0
            )
        );

        var battalion = new Battalion(
            1, "Battalion", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "A", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 60,
                tanks: withTanks ? 3 : 0)
        ));

        battalion.AddCompany(new Company(
            2, "B", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 40,
                tanks: withTanks ? 2 : 0)
        ));

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    [Fact]
    public void PersonnelFastPath_MatchesDetailedAllocation()
    {
        var detailed = CreateFormation();
        var optimized = CreateFormation();

        var casualties = new PersonnelCasualtyReport(
            KilledInAction: 10,
            WoundedInAction: 5,
            MissingOrCaptured: 5
        );

        var allocations =
            HierarchicalPersonnelLossApplier.Apply(
                detailed, casualties);

        HierarchicalPersonnelLossApplier.ApplyInPlace(
            optimized, casualties);

        Assert.Equal(
            detailed.GetCurrentResourceTotals(),
            optimized.GetCurrentResourceTotals());

        Assert.Equal(
            10,
            allocations.Sum(x => x.Casualties.KilledInAction));

        Assert.Equal(
            5,
            allocations.Sum(x => x.Casualties.WoundedInAction));

        Assert.Equal(
            5,
            allocations.Sum(x => x.Casualties.MissingOrCaptured));

        // Vérifier les stocks de chaque compagnie,
        // pas seulement le total divisionnaire.
        for (int i = 0; i < 2; i++)
        {
            var original = detailed.Battalions[0]
                .Companies[i].Composition!.Manpower;

            var current = optimized.Battalions[0]
                .Companies[i].Composition!.Manpower;

            Assert.Equal(original.Current, current.Current);
            Assert.Equal(
                original.KilledInAction,
                current.KilledInAction);
            Assert.Equal(
                original.TotalWoundedInAction,
                current.TotalWoundedInAction);
            Assert.Equal(
                original.MissingOrCaptured,
                current.MissingOrCaptured);
        }
    }

    [Fact]
    public void TankFastPath_PreservesFractionalLosses()
    {
        var detailed = CreateFormation();
        var optimized = CreateFormation();

        var detailedApplier =
            new HierarchicalTankLossApplier(detailed);

        var optimizedApplier =
            new HierarchicalTankLossApplier(optimized);

        var expectations = new[]
        {
            new TankLossExpectation(0, 0.4),
            new TankLossExpectation(0, 0.4),
            new TankLossExpectation(0, 0.4),
            new TankLossExpectation(0.5, 0.2),
            new TankLossExpectation(0.7, 0.3),
            new TankLossExpectation(2, 2),
            new TankLossExpectation(0.5, 0.5)
        };

        foreach (var expectation in expectations)
        {
            var allocations =
                detailedApplier.Apply(expectation);

            var result =
                optimizedApplier.ApplyToPools(expectation);

            Assert.Equal(
                allocations.Sum(x => x.Losses.Damaged),
                result.Damaged);

            Assert.Equal(
                allocations.Sum(x => x.Losses.Destroyed),
                result.Destroyed);

            Assert.Equal(
                detailed.GetCurrentResourceTotals(),
                optimized.GetCurrentResourceTotals());
        }
    }

    [Fact]
    public void NoTanks_FastPathDoesNotChangeStocks()
    {
        var division = CreateFormation(withTanks: false);

        var applier =
            new HierarchicalTankLossApplier(division);

        var before = division.GetCurrentResourceTotals();

        var result = applier.ApplyToPools(
            new TankLossExpectation(
                Damaged: 2,
                Destroyed: 3));

        Assert.Equal(0, result.Total);
        Assert.Equal(before, division.GetCurrentResourceTotals());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            applier.ApplyToPools(
                new TankLossExpectation(
                    Damaged: double.NaN,
                    Destroyed: 0)));
    }

    [Fact]
    public void CachedReferences_ReadUpdatedPersonnel()
    {
        var division = CreateFormation();

        var first = division.GetLossTargets();

        var pool = division.Battalions[0]
            .Companies[0].Composition!.Manpower;

        pool.ApplyCasualties(
            new PersonnelCasualtyReport(20, 0, 0));

        pool.Reinforce(10);

        HierarchicalPersonnelLossApplier.ApplyInPlace(
            division,
            new PersonnelCasualtyReport(7, 0, 0));

        var second = division.GetLossTargets();

        Assert.Same(first, second);

        Assert.Equal(
            83,
            division.GetCurrentResourceTotals()
                .Personnel.Available);
    }
}
