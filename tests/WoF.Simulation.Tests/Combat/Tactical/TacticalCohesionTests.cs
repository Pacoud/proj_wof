
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalCohesionTests
{
    private static readonly TacticalSuppressionProfile Suppression =
        new(
            dangerMarginMeters: 10,
            pressurePerThreateningRound: 2,
            recoveryPerHour: 10,
            maxFireReductionFraction: 0.8);

    private static readonly TacticalCohesionProfile Cohesion =
        new(
            lossPerCasualtyPercent: 1.5,
            sustainedSuppressionThreshold: 40,
            lossPerExcessPressurePoint: 0.1,
            recoveryPerQuietHour: 3,
            recoverySuppressionCeiling: 20,
            maxFireReductionFraction: 0.5);

    private sealed record Scenario(
        TacticalBattle Battle,
        CompanyDeployment First,
        CompanyDeployment Second,
        Division FirstDivision,
        Division SecondDivision);

    private static Division MakeDivision(
        Country country,
        Province province)
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

        battalion.AddCompany(new Company(
            1, "Infantry Company", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 100,
                ammunition: new AmmunitionTotals(
                    smallArms: 1000))));

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var first = MakeDivision(france, province);
        var second = MakeDivision(germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { first, second },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var field = new TacticalBattlefield();
        var sector = new BattleSector(
            1, "Sector", TerrainType.Plains);

        field.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, field);

        var a = battle.DeployCompany(
            first.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var b = battle.DeployCompany(
            second.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        return new Scenario(
            battle, a, b, first, second);
    }

    private static TacticalCasualtyPhaseResult
        EmptyCasualties(long hour)
    {
        return new TacticalCasualtyPhaseResult(
            hour,
            Array.Empty<TacticalCompanyCasualtyApplication>());
    }

    private static TacticalCasualtyPhaseResult
        ApplyPhysicalLosses(
            CompanyDeployment company,
            long hour,
            int killed,
            int wounded)
    {
        var pool = company.Company.Composition!.Manpower;
        int before = pool.Current;

        var report = new PersonnelCasualtyReport(
            KilledInAction: killed,
            WoundedInAction: wounded,
            MissingOrCaptured: 0);

        pool.ApplyCasualties(report);

        return new TacticalCasualtyPhaseResult(
            hour,
            new[]
            {
                new TacticalCompanyCasualtyApplication(
                    company,
                    report,
                    before,
                    pool.Current)
            });
    }

    private static TacticalSuppressionPhaseResult
        ApplySuppression(
            Scenario s,
            long hour,
            params TacticalSuppressionThreat[] threats)
    {
        return s.Battle.Suppression.ApplyHour(
            hour,
            threats,
            Suppression);
    }

    [Fact]
    public void EveryCompanyStartsWithFullCohesion()
    {
        var s = CreateScenario();

        Assert.Equal(100, s.First.Company.Cohesion);
        Assert.Equal(100, s.Second.Company.Cohesion);
        Assert.Equal(100, s.Battle.Cohesion.Get(s.First));
        Assert.Equal(100, s.Battle.Cohesion.Get(s.Second));
    }

    [Fact]
    public void CasualtiesReduceCohesionWithoutFurtherManpowerLoss()
    {
        var s = CreateScenario();

        var casualties = ApplyPhysicalLosses(
            s.Second,
            hour: 2,
            killed: 4,
            wounded: 6);

        var pressure = ApplySuppression(s, 2);

        var phase = s.Battle.Cohesion.ApplyHour(
            2, casualties, pressure, Suppression, Cohesion);

        var company = Assert.Single(
            phase.Companies,
            c => ReferenceEquals(c.Company, s.Second));

        // 10 % de pertes * 1,5 = 15 points.
        Assert.Equal(15, company.LossFromCasualties, 6);
        Assert.Equal(0, company.LossFromSustainedFire);
        Assert.Equal(85, company.CohesionAfter, 6);

        // Aucune deuxieme application de victimes.
        Assert.Equal(
            90,
            s.Second.Company.Composition!.Manpower.Current);

        Assert.Equal(
            90,
            s.SecondDivision.GetCurrentResourceTotals()
                .Personnel.Available);

        Assert.Equal(100, s.Battle.Cohesion.Get(s.First));
    }

    [Fact]
    public void FirstSuppressionHourDoesNotImmediatelyDamageCohesion()
    {
        var s = CreateScenario();

        var suppression = ApplySuppression(
            s, 2,
            new TacticalSuppressionThreat(
                s.Second, 50, 1, 50));

        s.Battle.Cohesion.ApplyHour(
            2,
            EmptyCasualties(2),
            suppression,
            Suppression,
            Cohesion);

        Assert.Equal(100, s.Battle.Cohesion.Get(s.Second));
        Assert.Equal(
            100,
            s.SecondDivision.GetCurrentResourceTotals()
                .Personnel.Available);
    }

    [Fact]
    public void SustainedFireDamagesCohesionWithoutCasualties()
    {
        var s = CreateScenario();

        var first = ApplySuppression(
            s, 2,
            new TacticalSuppressionThreat(
                s.Second, 50, 1, 50));

        s.Battle.Cohesion.ApplyHour(
            2, EmptyCasualties(2),
            first, Suppression, Cohesion);

        // Pression en debut d'heure suivante :
        // 100 - 10 de recuperation = 90.
        var second = ApplySuppression(
            s, 3,
            new TacticalSuppressionThreat(
                s.Second, 50, 1, 50));

        var effects = s.Battle.Cohesion.ApplyHour(
            3, EmptyCasualties(3),
            second, Suppression, Cohesion);

        var report = Assert.Single(
            effects.Companies,
            x => ReferenceEquals(x.Company, s.Second));

        // (90 - seuil 40) * 0,1 = 5.
        Assert.Equal(5, report.LossFromSustainedFire, 6);
        Assert.Equal(95, s.Battle.Cohesion.Get(s.Second), 6);

        // Ce sont des pertes de cohesion,
        // pas de nouvelles pertes humaines.
        Assert.Equal(
            100,
            s.SecondDivision.GetCurrentResourceTotals()
                .Personnel.Available);
    }

    [Fact]
    public void CohesionRecoversGraduallyDuringQuietHours()
    {
        var s = CreateScenario();

        var casualties = ApplyPhysicalLosses(
            s.Second, 2, killed: 4, wounded: 6);

        var first = ApplySuppression(s, 2);

        s.Battle.Cohesion.ApplyHour(
            2, casualties, first, Suppression, Cohesion);

        Assert.Equal(85, s.Battle.Cohesion.Get(s.Second));

        // Aucune nouvelle perte ni tir menaçant.
        var second = ApplySuppression(s, 3);

        s.Battle.Cohesion.ApplyHour(
            3, EmptyCasualties(3),
            second, Suppression, Cohesion);

        Assert.Equal(88, s.Battle.Cohesion.Get(s.Second), 6);
    }

    [Fact]
    public void HighSuppressionPreventsCohesionRecovery()
    {
        var s = CreateScenario();

        var casualties = ApplyPhysicalLosses(
            s.Second, 2, killed: 4, wounded: 6);

        var threat = ApplySuppression(
            s, 2,
            new TacticalSuppressionThreat(
                s.Second, 30, 1, 30));

        s.Battle.Cohesion.ApplyHour(
            2, casualties, threat, Suppression, Cohesion);

        Assert.Equal(85, s.Battle.Cohesion.Get(s.Second));

        // Suppression 60 -> 50, encore trop forte
        // pour autoriser la recuperation de cohesion.
        var quiet = ApplySuppression(s, 3);

        s.Battle.Cohesion.ApplyHour(
            3, EmptyCasualties(3),
            quiet, Suppression, Cohesion);

        Assert.Equal(85, s.Battle.Cohesion.Get(s.Second));
    }

    [Fact]
    public void LowCohesionReducesFireWithoutDestroyingWeapons()
    {
        var s = CreateScenario();

        var casualties = ApplyPhysicalLosses(
            s.Second, 2, killed: 4, wounded: 6);

        var quiet = ApplySuppression(s, 2);

        s.Battle.Cohesion.ApplyHour(
            2, casualties, quiet, Suppression, Cohesion);

        int effective = s.Battle.Cohesion.GetEffectiveWeapons(
            s.Second, 10, Cohesion);

        // Cohesion 85 :
        // floor(10 * (1 - 0.5 * 0.15)) = 9.
        Assert.Equal(9, effective);

        Assert.Equal(
            100,
            s.Second.Company.Composition!
                .InfantryEquipment.Current);
    }

    [Fact]
    public void SameHourCannotApplyCohesionTwice()
    {
        var s = CreateScenario();
        var quiet = ApplySuppression(s, 2);
        var casualties = ApplyPhysicalLosses(s.Second, 2, killed: 4, wounded: 6);

        s.Battle.Cohesion.ApplyHour(
            2, casualties, quiet, Suppression, Cohesion);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.Cohesion.ApplyHour(
                2, casualties, quiet, Suppression, Cohesion));

        Assert.Equal(85, s.Second.Company.Cohesion);
    }

    [Fact]
    public void WrongHourIsRejectedWithoutChangingCohesion()
    {
        var s = CreateScenario();
        var quiet = ApplySuppression(s, 2);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.Cohesion.ApplyHour(
                3, EmptyCasualties(3),
                quiet, Suppression, Cohesion));

        Assert.Equal(100, s.Battle.Cohesion.Get(s.Second));
    }

    [Fact]
    public void InvalidCasualtyReportLeavesBothCompaniesUnchanged()
    {
        var s = CreateScenario();
        s.Second.Company.Cohesion = 70;
        var quiet = ApplySuppression(s, 2);
        var valid = ApplyPhysicalLosses(s.First, 2, killed: 4, wounded: 6);
        var invalid = new TacticalCompanyCasualtyApplication(
            s.Second, new PersonnelCasualtyReport(1, 0, 0),
            AvailableBefore: 100, AvailableAfter: 99);
        var phase = new TacticalCasualtyPhaseResult(2, [valid.Companies[0], invalid]);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.Cohesion.ApplyHour(2, phase, quiet, Suppression, Cohesion));

        Assert.Equal(100, s.First.Company.Cohesion);
        Assert.Equal(70, s.Second.Company.Cohesion);

        s.Battle.Cohesion.ApplyHour(2, valid, quiet, Suppression, Cohesion);

        Assert.Equal(85, s.First.Company.Cohesion);
        Assert.Equal(73, s.Second.Company.Cohesion);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity)]
    public void CompanyRejectsInvalidCohesionWithoutChangingIt(double value)
    {
        var s = CreateScenario();
        s.Second.Company.Cohesion = 0;

        Assert.Throws<ArgumentOutOfRangeException>(() => s.Second.Company.Cohesion = value);

        Assert.Equal(0, s.Second.Company.Cohesion);
        s.Second.Company.Cohesion = 100;
        Assert.Equal(100, s.Second.Company.Cohesion);
    }

    [Fact]
    public void ForeignCompanyCannotUseAnotherBattlesCohesion()
    {
        var first = CreateScenario();
        var other = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            first.Battle.Cohesion.Get(other.Second));
    }

    [Fact]
    public void InvalidProfilesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalCohesionProfile(
                -1, 40, 0.1, 3, 20, 0.5));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalCohesionProfile(
                1.5, 101, 0.1, 3, 20, 0.5));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalCohesionProfile(
                1.5, 40, 0.1, double.NaN, 20, 0.5));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalCohesionProfile(
                1.5, 40, 0.1, 3, 20, 1.5));
    }


    [Fact]
    public void CompanyCohesionSurvivesNewTacticalBattleState()
    {
        var s = CreateScenario();

        var casualties = ApplyPhysicalLosses(
            s.Second,
            hour: 2,
            killed: 4,
            wounded: 6);

        var suppression = ApplySuppression(s, 2);

        s.Battle.Cohesion.ApplyHour(
            2,
            casualties,
            suppression,
            Suppression,
            Cohesion);

        // Dix pour cent de pertes :
        // 100 - 10 * 1.5 = 85.
        Assert.Equal(
            85.0,
            s.Second.Company.Cohesion,
            6);

        // Construire un nouveau contexte tactique
        // a partir du meme objet Company.
        var field = new TacticalBattlefield();

        var sector = new BattleSector(
            99,
            "Second battlefield",
            TerrainType.Plains);

        field.AddSector(sector);

        var nextBattle = new TacticalBattle(
            s.Battle.Engagement,
            field);

        var newDeployment = nextBattle.DeployCompany(
            s.Second.Company,
            sector,
            CompanyDeploymentRole.FrontLine);

        // Le nouveau contexte lit la valeur
        // persistante de la compagnie.
        Assert.Equal(
            85.0,
            nextBattle.Cohesion.Get(newDeployment),
            6);

        var nextSuppression = nextBattle.Suppression.ApplyHour(
            3, Array.Empty<TacticalSuppressionThreat>(), Suppression);
        var nextPhase = nextBattle.Cohesion.ApplyHour(
            3, EmptyCasualties(3), nextSuppression, Suppression, Cohesion);

        var report = Assert.Single(nextPhase.Companies);
        Assert.Equal(85, report.CohesionBefore, 6);
        Assert.Equal(88, report.CohesionAfter, 6);

        // L'ancien deploiement lit aussi la valeur mise a jour.
        Assert.Equal(88, s.Battle.Cohesion.Get(s.Second), 6);

        // Une compagnie differente n'herite
        // pas de cette cohesion.
        Assert.Equal(
            100.0,
            s.First.Company.Cohesion,
            6);
    }

}
