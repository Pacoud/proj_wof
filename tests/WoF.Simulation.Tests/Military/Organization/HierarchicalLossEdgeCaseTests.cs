
using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.Military.Reinforcements;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Organization;

public class HierarchicalLossEdgeCaseTests
{
    // =====================================================
    // TEST 1 : PLUS DE 128 COMPAGNIES
    // =====================================================

    private static Division CreateLargeFormation()
    {
        const int battalionCount = 11;
        const int companiesPerBattalion = 12;
        const int totalCompanies = 132;

        var division = new Division(
            name: "Large Test Division",
            position: new Province(1, "Test"),
            fuel: 100,
            ammunition: 100,
            composition: new DivisionComposition(
                manpower: totalCompanies,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0
            )
        );

        for (int b = 1; b <= battalionCount; b++)
        {
            var battalion = new Battalion(
                b,
                $"Battalion {b}",
                BattalionType.Infantry
            );

            for (int c = 1; c <= companiesPerBattalion; c++)
            {
                battalion.AddCompany(new Company(
                    c,
                    $"Company {c}",
                    CompanyType.Infantry,
                    new CompanyComposition(manpower: 1)
                ));
            }

            division.AddBattalion(battalion);
        }

        division.EnableHierarchicalResources();

        return division;
    }

    [Fact]
    public void MoreThan128Companies_PreserveExactLossDistribution()
    {
        var detailed = CreateLargeFormation();
        var optimized = CreateLargeFormation();

        Assert.Equal(132, detailed.GetLossTargets().Length);
        Assert.Equal(132, optimized.GetLossTargets().Length);

        var casualties = new PersonnelCasualtyReport(
            KilledInAction: 66,
            WoundedInAction: 33,
            MissingOrCaptured: 11
        );

        // API publique avec allocations détaillées.
        var detailedReports =
            HierarchicalPersonnelLossApplier.Apply(
                detailed,
                casualties
            );

        // Chemin optimisé utilisé par le moteur.
        HierarchicalPersonnelLossApplier.ApplyInPlace(
            optimized,
            casualties
        );

        Assert.Equal(
            detailed.GetCurrentResourceTotals(),
            optimized.GetCurrentResourceTotals()
        );

        var totals = optimized.GetCurrentResourceTotals();

        Assert.Equal(22, totals.Personnel.Available);
        Assert.Equal(66, totals.Personnel.KilledInAction);
        Assert.Equal(33, totals.Personnel.TotalWoundedInAction);
        Assert.Equal(33, totals.Personnel.WoundedUnavailable);
        Assert.Equal(11, totals.Personnel.MissingOrCaptured);

        Assert.Equal(110, detailedReports.Count);

        // Chaque compagnie possède initialement
        // exactement un homme.
        //
        // Les égalités de restes doivent être
        // résolues dans l'ordre des identifiants :
        // 66 KIA, puis 33 WIA, puis 11 MIA.
        var detailedCompanies = detailed.Battalions
            .OrderBy(b => b.Id)
            .SelectMany(b => b.Companies.OrderBy(c => c.Id))
            .ToArray();

        var optimizedCompanies = optimized.Battalions
            .OrderBy(b => b.Id)
            .SelectMany(b => b.Companies.OrderBy(c => c.Id))
            .ToArray();

        for (int i = 0; i < 132; i++)
        {
            var original =
                detailedCompanies[i].Composition!.Manpower;

            var current =
                optimizedCompanies[i].Composition!.Manpower;

            Assert.Equal(original.Current, current.Current);
            Assert.Equal(
                original.KilledInAction,
                current.KilledInAction
            );
            Assert.Equal(
                original.TotalWoundedInAction,
                current.TotalWoundedInAction
            );
            Assert.Equal(
                original.MissingOrCaptured,
                current.MissingOrCaptured
            );

            Assert.Equal(i >= 110 ? 1 : 0, current.Current);
            Assert.Equal(i < 66 ? 1 : 0,
                current.KilledInAction);
            Assert.Equal(i >= 66 && i < 99 ? 1 : 0,
                current.TotalWoundedInAction);
            Assert.Equal(i >= 99 && i < 110 ? 1 : 0,
                current.MissingOrCaptured);
        }
    }

    // =====================================================
    // TEST 2 : COMBAT BLINDE ET RENFORTS
    // =====================================================

    private static Division CreateArmoredFormation(
        Province province,
        Country country,
        string name,
        int tanks,
        int artillery)
    {
        var division = new Division(
            name: name,
            position: province,
            fuel: 100_000,
            ammunition: 100_000,
            fuelCapacity: 100_000,
            type: DivisionType.Armored,
            country: country,
            maxOrganization: 100_000,
            composition: new DivisionComposition(
                manpower: 4000,
                infantryEquipment: 0,
                artillery: artillery,
                tanks: tanks
            )
        );

        var battalion = new Battalion(
            1,
            "Armored Battalion",
            BattalionType.Armored
        );

        for (int i = 1; i <= 2; i++)
        {
            battalion.AddCompany(new Company(
                i,
                $"Company {i}",
                CompanyType.Armor,
                new CompanyComposition(
                    manpower: 2000,
                    artillery: artillery / 2,
                    tanks: tanks / 2
                )
            ));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    [Fact]
    public void ArmoredBattle_PreservesLossesAfterExhaustionAndReinforcement()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(
            1,
            "Armored Battlefield",
            owner: france
        );

        var engine = new SimulationEngine();

        var french = CreateArmoredFormation(
            province,
            france,
            "French Armored",
            tanks: 200,
            artillery: 0
        );

        var german = CreateArmoredFormation(
            province,
            germany,
            "German Armored",
            tanks: 60,
            artillery: 2000
        );

        engine.AddDivision(french);
        engine.AddDivision(german);
        engine.DeclareWar(france, germany);

        // Premier tick : création de l'engagement.
        engine.Tick();

        Assert.True(french.IsEngaged);
        Assert.True(german.IsEngaged);

        // Plusieurs heures de véritable combat.
        for (int i = 0; i < 5; i++)
            engine.Tick();

        var afterCombat =
            french.GetCurrentResourceTotals().Tanks;

        // L'artillerie et les blindés ennemis
        // doivent avoir provoqué de vraies pertes.
        Assert.True(
            afterCombat.Damaged + afterCombat.Destroyed > 0,
            "Expected real tank losses during combat."
        );

        Assert.True(afterCombat.Operational > 0);
        Assert.True(french.IsEngaged);

        // Forcer ensuite la destruction des chars
        // opérationnels restants pour tester
        // la transition vers un stock nul.
        int operationalBeforeExhaustion =
            afterCombat.Operational;

        var exhaustion = french.ApplyExpectedTankLosses(
            new TankLossExpectation(
                Damaged: 0,
                Destroyed: 100_000
            )
        );

        Assert.Equal(
            operationalBeforeExhaustion,
            exhaustion.Destroyed
        );

        Assert.Equal(
            0,
            french.GetCurrentResourceTotals().Tanks.Operational
        );

        // Un tick sans char opérationnel doit
        // réinitialiser les éventuels restes
        // fractionnaires.
        engine.Tick();

        Assert.Equal(
            0,
            french.GetCurrentResourceTotals().Tanks.Operational
        );

        Assert.True(french.IsEngaged);

        // Réparation d'un char endommagé.
        var companies = french.Battalions
            .SelectMany(b => b.Companies)
            .ToArray();

        var damagedPool = companies
            .Select(c => c.Composition!.Tanks)
            .First(p => p.Damaged > 0);

        Assert.Equal(1, damagedPool.Repair(1));

        // Réception de trois chars depuis une réserve.
        var cargo = new ReinforcementCargo(
            Personnel: 0,
            InfantryEquipment: 0,
            Artillery: 0,
            Tanks: 3,
            Trucks: 0
        );

        var reserve = new ReinforcementReserve(
            "Armored Reserve",
            cargo
        );

        var receipt = ReinforcementTransferService.Transfer(
            reserve,
            companies[0],
            cargo
        );

        Assert.Equal(3, receipt.Delivered.Tanks);
        Assert.Equal(0, reserve.Stock.Tanks);

        Assert.Equal(
            4,
            french.GetCurrentResourceTotals().Tanks.Operational
        );

        // Vérifier explicitement qu'aucune ancienne
        // fraction de destruction ne touche les chars
        // nouvellement remis en service.
        var fractional = new TankLossExpectation(
            Damaged: 0,
            Destroyed: 0.4
        );

        Assert.Equal(
            0,
            french.ApplyExpectedTankLosses(fractional).Total
        );

        Assert.Equal(
            0,
            french.ApplyExpectedTankLosses(fractional).Total
        );

        // Au troisième appel : 1,2 char attendu,
        // donc une destruction entière.
        var third = french.ApplyExpectedTankLosses(
            fractional
        );

        Assert.Equal(1, third.Destroyed);
        Assert.Equal(0, third.Damaged);

        Assert.Equal(
            3,
            french.GetCurrentResourceTotals().Tanks.Operational
        );

        // Le moteur doit encore pouvoir
        // poursuivre l'engagement après les
        // pertes, réparations et renforts.
        engine.Tick();

        Assert.True(french.IsEngaged);
        Assert.True(german.IsEngaged);

        // Cohérence entre les stocks des compagnies
        // et les totaux de la division.
        var pools = companies
            .Select(c => c.Composition!.Tanks)
            .ToArray();

        var finalTanks =
            french.GetCurrentResourceTotals().Tanks;

        Assert.Equal(
            pools.Sum(p => p.Operational),
            finalTanks.Operational
        );

        Assert.Equal(
            pools.Sum(p => p.Damaged),
            finalTanks.Damaged
        );

        Assert.Equal(
            pools.Sum(p => p.Destroyed),
            finalTanks.Destroyed
        );
    }
}
