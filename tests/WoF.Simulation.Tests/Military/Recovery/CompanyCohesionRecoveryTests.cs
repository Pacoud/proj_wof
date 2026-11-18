
using WoF.Simulation.Core;
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.Military.Recovery;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Recovery;

public sealed class CompanyCohesionRecoveryTests
{
    private static (Division Division, Company Company)
        CreateDivision(
            Country country,
            Province province,
            double cohesion = 40,
            bool hierarchical = true)
    {
        var division = new Division(
            $"{country.Name} Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: 100,
                infantryEquipment: 100,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Infantry Battalion",
            BattalionType.Infantry);

        var company = new Company(
            1,
            "Infantry Company",
            CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 100,
                ammunition: new AmmunitionTotals(
                    smallArms: 1000)));

        company.Cohesion = cohesion ;

        battalion.AddCompany(company);
        division.AddBattalion(battalion);

        if (hierarchical)
            division.EnableHierarchicalResources();

        return (division, company);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidRecoveryRateIsRejected(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CompanyCohesionRecoverySystem(
                new DiplomacySystem(),
                rate));
    }

    [Fact]
    public void DefaultEngineDoesNotRecoverCompanyCohesion()
    {
        var france = new Country(1, "France");
        var province = new Province(
            1, "Home", owner: france);

        var (division, company) =
            CreateDivision(france, province);

        var engine = new SimulationEngine();
        engine.AddDivision(division);

        engine.Tick();
        engine.Tick();

        Assert.Equal(40.0, company.Cohesion, 6);
    }

    [Fact]
    public void SafeCompanyRecoversIndependentlyOfDivisionOrganization()
    {
        var france = new Country(1, "France");
        var province = new Province(
            1, "Home", owner: france);

        var (division, company) =
            CreateDivision(france, province);

        var engine = new SimulationEngine(
            cohesionRecoveryPerHour: 3);

        engine.AddDivision(division);

        // L'organisation divisionnaire est deja maximale.
        Assert.Equal(
            division.MaxOrganization,
            division.Organization);

        engine.Tick();

        Assert.Equal(43.0, company.Cohesion, 6);

        engine.Tick();

        Assert.Equal(46.0, company.Cohesion, 6);

        Assert.Equal(
            division.MaxOrganization,
            division.Organization);
    }

    [Theory]
    [InlineData(0.0, 3.0)]
    [InlineData(99.0, 100.0)]
    [InlineData(100.0, 100.0)]
    public void RecoveryRespectsCohesionBounds(
        double initial,
        double expected)
    {
        var france = new Country(1, "France");
        var province = new Province(
            1, "Home", owner: france);

        var (division, company) =
            CreateDivision(france, province, initial);

        var engine = new SimulationEngine(3);
        engine.AddDivision(division);

        engine.Tick();

        Assert.Equal(expected, company.Cohesion, 6);
    }

    [Fact]
    public void CompanyWithoutManpowerDoesNotRecover()
    {
        var france = new Country(1, "France");
        var province = new Province(
            1, "Home", owner: france);

        var (division, company) =
            CreateDivision(france, province);

        company.Composition!.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 100,
                WoundedInAction: 0,
                MissingOrCaptured: 0));

        var engine = new SimulationEngine(3);
        engine.AddDivision(division);

        engine.Tick();

        Assert.Equal(0,
            company.Composition.Manpower.Current);

        Assert.Equal(40.0, company.Cohesion, 6);
    }

    [Fact]
    public void LegacyDivisionDoesNotRecoverCompanyCohesion()
    {
        var france = new Country(1, "France");
        var province = new Province(
            1, "Home", owner: france);

        var (division, company) = CreateDivision(
            france,
            province,
            hierarchical: false);

        var engine = new SimulationEngine(3);
        engine.AddDivision(division);

        engine.Tick();

        Assert.Equal(
            DivisionResourceMode.Legacy,
            division.ResourceMode);

        Assert.Equal(40.0, company.Cohesion, 6);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NearbyEnemyPreventsRecovery(
        bool sameProvince)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var home = new Province(
            1, "Home", owner: france);

        var neighbor = new Province(
            2, "Neighbor", owner: germany);

        home.ConnectTo(neighbor);

        var (french, company) =
            CreateDivision(france, home);

        var (german, _) = CreateDivision(
            germany,
            sameProvince ? home : neighbor,
            cohesion: 100);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var recovery =
            new CompanyCohesionRecoverySystem(
                diplomacy, 3);

        recovery.ProcessRecovery(
            2,
            new[] { french, german },
            Array.Empty<Engagement>());

        Assert.Equal(40.0, company.Cohesion, 6);
    }

    [Fact]
    public void SameHourCannotRecoverTwice()
    {
        var france = new Country(1, "France");
        var province = new Province(
            1, "Home", owner: france);

        var (division, company) =
            CreateDivision(france, province);

        var recovery =
            new CompanyCohesionRecoverySystem(
                new DiplomacySystem(), 3);

        recovery.ProcessRecovery(
            2,
            new[] { division },
            Array.Empty<Engagement>());

        Assert.Equal(43.0, company.Cohesion, 6);

        Assert.Throws<InvalidOperationException>(
            () => recovery.ProcessRecovery(
                2,
                new[] { division },
                Array.Empty<Engagement>()));

        Assert.Throws<InvalidOperationException>(
            () => recovery.ProcessRecovery(
                1,
                new[] { division },
                Array.Empty<Engagement>()));

        Assert.Equal(43.0, company.Cohesion, 6);
    }

    [Fact]
    public void InvalidPhaseDoesNotPartiallyRecoverCompanies()
    {
        var france = new Country(1, "France");
        var province = new Province(
            1, "Home", owner: france);

        var (division, company) =
            CreateDivision(france, province);

        var recovery =
            new CompanyCohesionRecoverySystem(
                new DiplomacySystem(), 3);

        Assert.Throws<ArgumentException>(
            () => recovery.ProcessRecovery(
                2,
                new Division[] { division, null! },
                Array.Empty<Engagement>()));

        Assert.Equal(40.0, company.Cohesion, 6);

        // L'heure doit encore pouvoir etre traitee
        // puisque la tentative precedente a echoue.
        recovery.ProcessRecovery(
            2,
            new[] { division },
            Array.Empty<Engagement>());

        Assert.Equal(43.0, company.Cohesion, 6);
    }

    [Fact]
    public void ClosingEngagementPreventsRecoveryUntilNextHour()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(
            1, "Battlefield", owner: germany);

        var (french, _) =
            CreateDivision(france, province);

        var (german, company) =
            CreateDivision(germany, province);

        var engine = new SimulationEngine(3);

        engine.AddDivision(french);
        engine.AddDivision(german);
        engine.DeclareWar(france, germany);

        // Creation du premier engagement.
        engine.Tick();

        var engagement = Assert.Single(
            engine.Engagements.ActiveEngagements);

        Assert.Equal(40.0, company.Cohesion, 6);

        // Provoquer la fin de l'engagement
        // sans modifier les regles de victoire.
        french.LoseOrganization(
            french.Organization);

        engine.Tick();

        Assert.False(engagement.IsActive);
        Assert.NotNull(engagement.EndHour);

        // Pas de recuperation pendant
        // l'heure de cloture.
        Assert.Equal(40.0, company.Cohesion, 6);

        // Recuperation a partir de l'heure suivante,
        // si le territoire est reste sur.
        engine.Tick();

        Assert.Equal(43.0, company.Cohesion, 6);
    }



    [Fact]
    public void EngagedDivisionCannotRecoverEvenWithoutNearbyEnemy()
    {
        var france = new Country(1, "France");
        var home = new Province(1, "Home", owner: france);

        var (division, company) =
            CreateDivision(france, home);

        var engagement = new Engagement(1, 1, home);
        engagement.AddParticipant(division, 1);

        Assert.True(division.IsEngaged);

        var recovery = new CompanyCohesionRecoverySystem(
            new DiplomacySystem(), 3);

        recovery.ProcessRecovery(
            2,
            new[] { division },
            Array.Empty<Engagement>());

        Assert.Equal(40.0, company.Cohesion, 6);
    }

    [Fact]
    public void TransitBlocksRecoveryUntilSafeArrival()
    {
        var france = new Country(1, "France");
        var origin = new Province(1, "Origin", owner: france);
        var destination = new Province(
            2, "Destination", owner: france);

        origin.ConnectTo(destination);

        var (division, company) =
            CreateDivision(france, origin);

        var engine = new SimulationEngine(3);
        engine.AddDivision(division);

        Assert.True(division.TryStartMovement(
            destination,
            new MovementPlan(
                DurationHours: 2,
                FuelPerHour: 0)));

        engine.Tick();

        Assert.True(division.IsInTransit);
        Assert.Equal(40.0, company.Cohesion, 6);

        engine.Tick();

        Assert.False(division.IsInTransit);
        Assert.Same(destination, division.CurrentProvince);
        Assert.Equal(43.0, company.Cohesion, 6);
    }

    [Fact]
    public void RetreatingDivisionCannotRecoverCohesion()
    {
        var france = new Country(1, "France");
        var home = new Province(1, "Home", owner: france);
        var rear = new Province(2, "Rear", owner: france);

        home.ConnectTo(rear);

        var (division, company) =
            CreateDivision(france, home);

        division.BeginRetreat(new[] { rear });

        Assert.True(division.IsRetreating);

        var recovery = new CompanyCohesionRecoverySystem(
            new DiplomacySystem(), 3);

        recovery.ProcessRecovery(
            2,
            new[] { division },
            Array.Empty<Engagement>());

        Assert.Equal(40.0, company.Cohesion, 6);
    }

    [Fact]
    public void CohesionRecoveryDoesNotModifyDivisionOrganization()
    {
        var france = new Country(1, "France");
        var home = new Province(1, "Home", owner: france);

        var (controlDivision, controlCompany) =
            CreateDivision(france, home);

        var (testDivision, testCompany) =
            CreateDivision(france, home);

        controlDivision.LoseOrganization(20);
        testDivision.LoseOrganization(20);

        var controlEngine = new SimulationEngine();
        var testEngine = new SimulationEngine(3);

        controlEngine.AddDivision(controlDivision);
        testEngine.AddDivision(testDivision);

        controlEngine.Tick();
        testEngine.Tick();

        Assert.Equal(
            controlDivision.Organization,
            testDivision.Organization,
            6);

        Assert.Equal(40.0, controlCompany.Cohesion, 6);
        Assert.Equal(43.0, testCompany.Cohesion, 6);
    }

    [Fact]
    public void CompaniesInDifferentBattalionsRecoverIndependently()
    {
        var france = new Country(1, "France");
        var home = new Province(1, "Home", owner: france);

        var division = new Division(
            "Division",
            home,
            fuel: 100,
            ammunition: 100,
            country: france,
            composition: new DivisionComposition(
                manpower: 200,
                infantryEquipment: 200,
                artillery: 0,
                tanks: 0));

        var firstBattalion =
            new Battalion(1, "First", BattalionType.Infantry);

        var secondBattalion =
            new Battalion(2, "Second", BattalionType.Infantry);

        var firstCompany = new Company(
            1, "First company", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 100,
                ammunition: new AmmunitionTotals(
                    smallArms: 1000)));

        var secondCompany = new Company(
            1, "Second company", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 100,
                ammunition: new AmmunitionTotals(
                    smallArms: 1000)));

        firstCompany.Cohesion = 40;
        secondCompany.Cohesion = 99;

        firstBattalion.AddCompany(firstCompany);
        secondBattalion.AddCompany(secondCompany);

        division.AddBattalion(firstBattalion);
        division.AddBattalion(secondBattalion);
        division.EnableHierarchicalResources();

        var engine = new SimulationEngine(3);
        engine.AddDivision(division);

        engine.Tick();

        Assert.Equal(43.0, firstCompany.Cohesion, 6);
        Assert.Equal(100.0, secondCompany.Cohesion, 6);
    }

}
