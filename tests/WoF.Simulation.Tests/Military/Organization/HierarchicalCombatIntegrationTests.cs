
using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat.History;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Organization;

public class HierarchicalCombatIntegrationTests
{
    private static Division CreateHierarchicalDivision(
        Country? country = null,
        Province? province = null)
    {
        var composition = new DivisionComposition(
            manpower: 2000,
            infantryEquipment: 1800,
            artillery: 300,
            tanks: 10,
            trucks: 40
        );

        var division = new Division(
            name: "Hierarchical Division",
            position: province ?? new Province(1, "Test"),
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: composition
        );

        var battalion = new Battalion(
            1,
            "Test Battalion",
            BattalionType.Infantry
        );

        battalion.AddCompany(new Company(
            1,
            "Company A",
            CompanyType.Infantry,
            new CompanyComposition(
                manpower: 1200,
                infantryEquipment: 1080,
                artillery: 180,
                tanks: 6,
                trucks: 24
            )
        ));

        battalion.AddCompany(new Company(
            2,
            "Company B",
            CompanyType.Infantry,
            new CompanyComposition(
                manpower: 800,
                infantryEquipment: 720,
                artillery: 120,
                tanks: 4,
                trucks: 16
            )
        ));

        division.AddBattalion(battalion);

        return division;
    }

    [Fact]
    public void Activation_RequiresMatchingResources()
    {
        var division = CreateHierarchicalDivision();

        var legacy = FormationResourceTotals.Capture(
            division.Composition
        );

        var hierarchical =
            division.GetHierarchicalResourceTotals();

        Assert.Equal(legacy, hierarchical);

        division.EnableHierarchicalResources();

        Assert.Equal(
            DivisionResourceMode.Hierarchical,
            division.ResourceMode);

        Assert.Equal(
            hierarchical,
            division.GetCurrentResourceTotals());
    }

    [Fact]
    public void IncompleteHierarchy_CannotBeActivated()
    {
        var division = new Division(
            "Incomplete",
            new Province(1, "Test"),
            fuel: 100,
            ammunition: 100
        );

        Assert.Throws<InvalidOperationException>(
            () => division.EnableHierarchicalResources());

        Assert.Equal(
            DivisionResourceMode.Legacy,
            division.ResourceMode);
    }

    [Fact]
    public void LossesModifyCompanies_NotLegacyPools()
    {
        var division = CreateHierarchicalDivision();

        division.EnableHierarchicalResources();

        division.ApplyPersonnelCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 5,
                WoundedInAction: 7,
                MissingOrCaptured: 2
            )
        );

        var tankLosses = division.ApplyExpectedTankLosses(
            new TankLossExpectation(
                Damaged: 1,
                Destroyed: 2
            )
        );

        var current = division.GetCurrentResourceTotals();

        Assert.Equal(1986, current.Personnel.Available);
        Assert.Equal(5, current.Personnel.KilledInAction);
        Assert.Equal(7, current.Personnel.TotalWoundedInAction);
        Assert.Equal(2, current.Personnel.MissingOrCaptured);

        Assert.Equal(7, current.Tanks.Operational);
        Assert.Equal(1, current.Tanks.Damaged);
        Assert.Equal(2, current.Tanks.Destroyed);

        Assert.Equal(1, tankLosses.Damaged);
        Assert.Equal(2, tankLosses.Destroyed);

        // Les anciens stocks n'ont pas été modifiés.
        Assert.Equal(
            2000,
            division.Composition.Manpower.Current);

        Assert.Equal(
            10,
            division.Composition.Tanks.Operational);

        // Le Battle Ledger doit consulter la source active.
        var snapshot = ParticipantBattleSnapshot.Capture(
            division
        );

        Assert.Equal(1986, snapshot.Manpower);
        Assert.Equal(2, snapshot.DestroyedTanks);
    }

    [Fact]
    public void RealCombat_UsesHierarchicalResources()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(
            1,
            "Battlefield",
            owner: france
        );

        var french = CreateHierarchicalDivision(
            france,
            province
        );

        french.EnableHierarchicalResources();

        var german = new Division(
            name: "Legacy Division",
            position: province,
            fuel: 100,
            ammunition: 100,
            country: germany,
            composition: new DivisionComposition(
                manpower: 2000,
                infantryEquipment: 1800,
                artillery: 300,
                tanks: 10,
                trucks: 40
            )
        );

        var simulation = new SimulationEngine();

        simulation.AddDivision(french);
        simulation.AddDivision(german);

        simulation.DeclareWar(france, germany);

        // Détection du contact.
        simulation.Tick();

        Assert.True(french.IsEngaged);
        Assert.True(german.IsEngaged);

        // Première heure réelle de combat.
        simulation.Tick();

        var totals = french.GetCurrentResourceTotals();

        // Les compagnies ont réellement subi des pertes.
        Assert.True(totals.Personnel.Available < 2000);

        // La source de vérité est bien hiérarchique.
        Assert.Equal(
            french.GetHierarchicalResourceTotals(),
            totals);

        // L'ancien stock reste inactif.
        Assert.Equal(
            2000,
            french.Composition.Manpower.Current);

        // Forcer une conclusion pour tester l'archivage.
        french.LoseOrganization(100);
        german.LoseOrganization(100);

        simulation.Tick();

        var report = Assert.Single(
            simulation.Engagements.History.Reports);

        var participant = Assert.Single(
            report.Participants,
            p => p.DivisionName == french.Name);

        Assert.Equal(2000, participant.Initial.Manpower);

        Assert.Equal(
            french.GetCurrentResourceTotals()
                .Personnel.Available,
            participant.Final.Manpower);
    }
}
