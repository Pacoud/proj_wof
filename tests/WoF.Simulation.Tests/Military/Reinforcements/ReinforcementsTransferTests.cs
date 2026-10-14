
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.Military.Reinforcements;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Reinforcements;

public class ReinforcementTransferTests
{
    private static (Division Division, Company Company)
        CreateFormation()
    {
        var division = new Division(
            "Test Division",
            new Province(1, "Test Province"),
            fuel: 100,
            ammunition: 100
        );

        var battalion = new Battalion(
            1, "I Battalion", BattalionType.Infantry);

        var company = new Company(
            1, "Company A", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 80,
                artillery: 4,
                tanks: 5,
                trucks: 6
            ));

        battalion.AddCompany(company);
        division.AddBattalion(battalion);

        // Créer des postes et stocks vacants.
        var c = company.Composition!;

        c.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(20, 0, 0));

        c.InfantryEquipment.ApplyLoss(15);
        c.Artillery.ApplyLoss(2);
        c.Trucks.ApplyLoss(3);

        c.Tanks.ApplyResolvedLosses(
            new TankLossResult(
                Damaged: 0,
                Destroyed: 2
            ));

        return (division, company);
    }

    private static ReinforcementReserve CreateReserve()
    {
        return new ReinforcementReserve(
            "Reserve A",
            new ReinforcementCargo(
                Personnel: 60,
                InfantryEquipment: 50,
                Artillery: 10,
                Tanks: 4,
                Trucks: 8
            ));
    }

    [Fact]
    public void Transfer_ConservesAllPhysicalResources()
    {
        var (division, company) = CreateFormation();
        var reserve = CreateReserve();

        var before =
            division.GetHierarchicalResourceTotals();

        var sourceBefore = reserve.Stock;

        int legacyBefore =
            division.Composition.Manpower.Current;

        var receipt = ReinforcementTransferService.Transfer(
            reserve,
            company,
            new ReinforcementCargo(
                Personnel: 12,
                InfantryEquipment: 10,
                Artillery: 1,
                Tanks: 1,
                Trucks: 2
            ));

        var after =
            division.GetHierarchicalResourceTotals();

        Assert.Equal(92, after.Personnel.Available);
        Assert.Equal(75, after.InfantryEquipment.Available);
        Assert.Equal(3, after.Artillery.Available);
        Assert.Equal(4, after.Tanks.Operational);
        Assert.Equal(5, after.Trucks.Available);

        Assert.Equal(48, reserve.Stock.Personnel);
        Assert.Equal(40, reserve.Stock.InfantryEquipment);
        Assert.Equal(9, reserve.Stock.Artillery);
        Assert.Equal(3, reserve.Stock.Tanks);
        Assert.Equal(6, reserve.Stock.Trucks);

        // Conservation source + destination.
        Assert.Equal(
            sourceBefore.Personnel + before.Personnel.Available,
            reserve.Stock.Personnel + after.Personnel.Available);

        Assert.Equal(
            sourceBefore.InfantryEquipment
                + before.InfantryEquipment.Available,
            reserve.Stock.InfantryEquipment
                + after.InfantryEquipment.Available);

        Assert.Equal(
            sourceBefore.Artillery + before.Artillery.Available,
            reserve.Stock.Artillery + after.Artillery.Available);

        Assert.Equal(
            sourceBefore.Tanks + before.Tanks.Operational,
            reserve.Stock.Tanks + after.Tanks.Operational);

        Assert.Equal(
            sourceBefore.Trucks + before.Trucks.Available,
            reserve.Stock.Trucks + after.Trucks.Available);

        // Le renfort ne supprime pas les pertes passées.
        Assert.Equal(20, after.Personnel.KilledInAction);

        // Le modèle de combat historique est indépendant.
        Assert.Equal(
            legacyBefore,
            division.Composition.Manpower.Current);

        Assert.Equal("Reserve A", receipt.SourceName);
        Assert.Equal(1, receipt.BattalionId);
        Assert.Equal(1, receipt.CompanyId);
        Assert.Equal(12, receipt.Delivered.Personnel);
    }

    [Fact]
    public void InsufficientReserve_DoesNotChangeStocks()
    {
        var (division, company) = CreateFormation();
        var reserve = CreateReserve();

        var before = division.GetHierarchicalResourceTotals();
        var sourceBefore = reserve.Stock;

        Assert.Throws<InvalidOperationException>(() =>
            ReinforcementTransferService.Transfer(
                reserve,
                company,
                new ReinforcementCargo(
                    Personnel: 61,
                    InfantryEquipment: 0,
                    Artillery: 0,
                    Tanks: 0,
                    Trucks: 0
                )));

        Assert.Equal(sourceBefore, reserve.Stock);
        Assert.Equal(
            before,
            division.GetHierarchicalResourceTotals());
    }

    [Fact]
    public void InsufficientCompanyCapacity_IsAtomic()
    {
        var (division, company) = CreateFormation();
        var reserve = CreateReserve();

        var before = division.GetHierarchicalResourceTotals();
        var sourceBefore = reserve.Stock;

        // Seulement 3 places vacantes pour les camions.
        // La demande de 4 doit tout faire échouer.
        Assert.Throws<InvalidOperationException>(() =>
            ReinforcementTransferService.Transfer(
                reserve,
                company,
                new ReinforcementCargo(
                    Personnel: 10,
                    InfantryEquipment: 10,
                    Artillery: 1,
                    Tanks: 1,
                    Trucks: 4
                )));

        Assert.Equal(sourceBefore, reserve.Stock);
        Assert.Equal(
            before,
            division.GetHierarchicalResourceTotals());
    }

    [Fact]
    public void WoundedPersonnel_KeepTheirAuthorizedSlots()
    {
        var composition = new CompanyComposition(
            manpower: 100);

        var company = new Company(
            1, "Wounded Company",
            CompanyType.Infantry,
            composition);

        var reserve = new ReinforcementReserve(
            "Personnel Reserve",
            new ReinforcementCargo(30, 0, 0, 0, 0));

        composition.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 0,
                WoundedInAction: 20,
                MissingOrCaptured: 0
            ));

        Assert.Equal(80, composition.Manpower.Current);
        Assert.Equal(20, composition.Manpower.WoundedUnavailable);
        Assert.Equal(0, composition.Manpower.VacantPositions);

        Assert.Throws<InvalidOperationException>(() =>
            ReinforcementTransferService.Transfer(
                reserve,
                company,
                new ReinforcementCargo(1, 0, 0, 0, 0)));

        Assert.Equal(30, reserve.Stock.Personnel);
        Assert.Equal(80, composition.Manpower.Current);
    }

    [Fact]
    public void MissingCompanyComposition_PreventsTransfer()
    {
        var company = new Company(
            1, "Unallocated",
            CompanyType.Infantry);

        var reserve = CreateReserve();
        var before = reserve.Stock;

        Assert.Throws<InvalidOperationException>(() =>
            ReinforcementTransferService.Transfer(
                reserve,
                company,
                new ReinforcementCargo(5, 0, 0, 0, 0)));

        Assert.Equal(before, reserve.Stock);
    }

    [Fact]
    public void NegativeQuantities_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ReinforcementReserve(
                "Invalid Reserve",
                new ReinforcementCargo(
                    -1, 0, 0, 0, 0)));

        var (_, company) = CreateFormation();
        var reserve = CreateReserve();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReinforcementTransferService.Transfer(
                reserve,
                company,
                new ReinforcementCargo(
                    0, -1, 0, 0, 0)));
    }
}
