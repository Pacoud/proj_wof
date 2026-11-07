
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalBattalionAssessmentTests
{
    private static readonly TacticalBattalionAssessmentProfile Profile =
        new(
            frontLineWeight: 3,
            supportWeight: 2,
            reserveWeight: 1,
            priorityFrontMultiplier: 2,
            holdThreshold: 60,
            riskThreshold: 30);

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
        Battalion Battalion,
        CompanyDeployment PriorityFront,
        CompanyDeployment SecondaryFront,
        CompanyDeployment? Reserve,
        Division FriendlyDivision,
        BattleSector PrioritySector,
        BattleSector SecondarySector);

    private static Division MakeDivision(
        Country country,
        Province province,
        int companyCount)
    {
        var division = new Division(
            $"{country.Name} Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: companyCount * 100,
                infantryEquipment: companyCount * 100,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Infantry Battalion",
            BattalionType.Infantry);

        for (int id = 1; id <= companyCount; id++)
        {
            battalion.AddCompany(new Company(
                id,
                $"Company {id}",
                CompanyType.Infantry,
                new CompanyComposition(
                    manpower: 100,
                    infantryEquipment: 100)));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario(
        bool deployReserve = true,
        bool assignPriority = true)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var friendly = MakeDivision(france, province, 3);
        var enemy = MakeDivision(germany, province, 1);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { friendly, enemy },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var field = new TacticalBattlefield();

        var priority = new BattleSector(
            1, "Priority", TerrainType.Plains);

        var secondary = new BattleSector(
            2, "Secondary", TerrainType.Plains);

        field.AddSector(priority);
        field.AddSector(secondary);

        var battle = new TacticalBattle(engagement, field);
        var battalion = friendly.Battalions[0];

        var first = battle.DeployCompany(
            battalion.Companies[0],
            priority,
            CompanyDeploymentRole.FrontLine);

        var second = battle.DeployCompany(
            battalion.Companies[1],
            secondary,
            CompanyDeploymentRole.FrontLine);

        CompanyDeployment? reserve = null;

        if (deployReserve)
        {
            reserve = battle.DeployCompany(
                battalion.Companies[2],
                secondary,
                CompanyDeploymentRole.Reserve);
        }

        battle.DeployCompany(
            enemy.Battalions[0].Companies[0],
            priority,
            CompanyDeploymentRole.FrontLine);

        if (assignPriority)
        {
            battle.BattalionAssessments.SetPrioritySector(
                battalion, priority);
        }

        return new Scenario(
            battle,
            battalion,
            first,
            second,
            reserve,
            friendly,
            priority,
            secondary);
    }

    /// <summary>
    /// Applique physiquement les pertes puis utilise
    /// le moteur de cohesion deja developpe.
    /// Il n'y a aucun deuxieme tirage de pertes.
    /// </summary>
    private static void ApplyLosses(
        Scenario scenario,
        CompanyDeployment deployment,
        int killed,
        int wounded)
    {
        var pool = deployment.Company.Composition!.Manpower;

        int before = pool.Current;

        var casualtyReport = new PersonnelCasualtyReport(
            KilledInAction: killed,
            WoundedInAction: wounded,
            MissingOrCaptured: 0);

        pool.ApplyCasualties(casualtyReport);

        var casualties = new TacticalCasualtyPhaseResult(
            2,
            new[]
            {
                new TacticalCompanyCasualtyApplication(
                    deployment,
                    casualtyReport,
                    before,
                    pool.Current)
            });

        var suppression = scenario.Battle.Suppression.ApplyHour(
            2,
            Array.Empty<TacticalSuppressionThreat>(),
            Suppression);

        scenario.Battle.Cohesion.ApplyHour(
            2,
            casualties,
            suppression,
            Suppression,
            Cohesion);
    }

    private static TacticalBattalionAssessment Assess(Scenario s)
    {
        return s.Battle.BattalionAssessments.Evaluate(
            s.Battalion, Profile);
    }

    [Fact]
    public void IntactBattalionCanHoldPrioritySector()
    {
        var s = CreateScenario();

        var result = Assess(s);

        Assert.Equal(3, result.CompanyCount);
        Assert.Equal(300, result.AuthorizedPersonnel);
        Assert.Equal(300, result.AvailablePersonnel);

        Assert.Equal(100, result.Cohesion, 6);
        Assert.Equal(100, result.CombatReadiness, 6);
        Assert.Equal(
            100, result.PrioritySectorReadiness!.Value, 6);

        Assert.Equal(
            TacticalBattalionMissionStatus.Capable,
            result.MissionStatus);
    }

    [Fact]
    public void PriorityCompanyLossesPutMissionAtRisk()
    {
        var s = CreateScenario();

        ApplyLosses(
            s, s.PriorityFront,
            killed: 10,
            wounded: 10);

        var result = Assess(s);

        // Compagnie prioritaire :
        // 80 hommes restants, cohesion 70.
        // Capacite de combat = 80% * 70 = 56.
        Assert.Equal(
            70,
            s.Battle.Cohesion.Get(s.PriorityFront), 6);

        Assert.Equal(280, result.AvailablePersonnel);

        Assert.Equal(82, result.Cohesion, 6);
        Assert.Equal(73.6, result.CombatReadiness, 6);

        Assert.Equal(
            56, result.PrioritySectorReadiness!.Value, 6);

        Assert.Equal(
            TacticalBattalionMissionStatus.AtRisk,
            result.MissionStatus);
    }

    [Fact]
    public void SecondaryCompanyLossesDoNotInventPriorityFailure()
    {
        var s = CreateScenario();

        ApplyLosses(
            s, s.SecondaryFront,
            killed: 10,
            wounded: 10);

        var result = Assess(s);

        Assert.Equal(280, result.AvailablePersonnel);
        Assert.True(result.CombatReadiness < 100);

        // Le front prioritaire est intact.
        Assert.Equal(
            100, result.PrioritySectorReadiness!.Value, 6);

        Assert.Equal(
            TacticalBattalionMissionStatus.Capable,
            result.MissionStatus);
    }

    [Fact]
    public void DestroyedPriorityCompanyCannotDisappearFromAssessment()
    {
        var s = CreateScenario();

        ApplyLosses(
            s, s.PriorityFront,
            killed: 100,
            wounded: 0);

        var result = Assess(s);

        Assert.Equal(200, result.AvailablePersonnel);
        Assert.Equal(0, result.PrioritySectorReadiness!.Value);

        Assert.Equal(
            TacticalBattalionMissionStatus.UnableToHold,
            result.MissionStatus);

        // Le bataillon dispose encore de deux
        // compagnies, mais sa mission prioritaire
        // est compromise.
        Assert.True(result.CombatReadiness > 0);
        Assert.True(result.CombatReadiness < 100);
    }

    [Fact]
    public void PrioritySectorWithoutFrontlineCannotBeHeld()
    {
        var s = CreateScenario();

        var empty = new BattleSector(
            3, "Unoccupied", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(empty);

        s.Battle.BattalionAssessments.SetPrioritySector(
            s.Battalion,
            empty);

        var result = Assess(s);

        Assert.Equal(0, result.PrioritySectorReadiness!.Value);

        Assert.Equal(
            TacticalBattalionMissionStatus.UnableToHold,
            result.MissionStatus);
    }

    [Fact]
    public void UnassignedMissionDoesNotPretendToBeCapable()
    {
        var s = CreateScenario(assignPriority: false);

        var result = Assess(s);

        Assert.Equal(
            TacticalBattalionMissionStatus.Unassigned,
            result.MissionStatus);

        Assert.Null(result.PrioritySector);
        Assert.Null(result.PrioritySectorReadiness);

        Assert.Equal(100, result.CombatReadiness, 6);
    }

    [Fact]
    public void PartiallyDeployedBattalionIsRejected()
    {
        var s = CreateScenario(deployReserve: false);

        Assert.Throws<InvalidOperationException>(() =>
            Assess(s));

        Assert.Equal(
            300,
            s.FriendlyDivision.GetCurrentResourceTotals()
                .Personnel.Available);
    }

    [Fact]
    public void AssessmentIsRecomputedAfterPersonnelRecovery()
    {
        var s = CreateScenario();

        ApplyLosses(
            s, s.PriorityFront,
            killed: 0,
            wounded: 20);

        var before = Assess(s);

        Assert.Equal(
            56, before.PrioritySectorReadiness!.Value, 6);

        // Dix blesses reviennent au service.
        // La cohesion reste de 70 pour le moment.
        var pool = s.PriorityFront.Company.Composition!.Manpower;

        Assert.Equal(10, pool.ReturnWoundedToDuty(10));

        var after = Assess(s);

        Assert.Equal(290, after.AvailablePersonnel);

        Assert.Equal(
            63, after.PrioritySectorReadiness!.Value, 6);

        Assert.Equal(
            TacticalBattalionMissionStatus.Capable,
            after.MissionStatus);
    }

    [Fact]
    public void ReadingAssessmentDoesNotModifyResources()
    {
        var s = CreateScenario();

        var before =
            s.FriendlyDivision.GetCurrentResourceTotals();

        var first = Assess(s);
        var second = Assess(s);

        Assert.Equal(first, second);

        Assert.Equal(
            before,
            s.FriendlyDivision.GetCurrentResourceTotals());
    }

    [Fact]
    public void EvaluateAllReportsEachRepresentedBattalionOnce()
    {
        var s = CreateScenario();

        var results =
            s.Battle.BattalionAssessments.EvaluateAll(Profile);

        // Un bataillon francais et un allemand.
        Assert.Equal(2, results.Count);

        Assert.Single(results, r =>
            ReferenceEquals(r.Battalion, s.Battalion));
    }

    [Fact]
    public void ForeignBattalionAndSectorAreRejected()
    {
        var first = CreateScenario();
        var second = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            first.Battle.BattalionAssessments.Evaluate(
                second.Battalion, Profile));

        Assert.Throws<InvalidOperationException>(() =>
            first.Battle.BattalionAssessments.SetPrioritySector(
                second.Battalion,
                first.PrioritySector));

        Assert.Throws<InvalidOperationException>(() =>
            first.Battle.BattalionAssessments.SetPrioritySector(
                first.Battalion,
                second.PrioritySector));
    }

    [Fact]
    public void InvalidAssessmentProfilesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalBattalionAssessmentProfile(
                0, 2, 1, 2, 60, 30));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalBattalionAssessmentProfile(
                3, 2, 1, double.NaN, 60, 30));

        Assert.Throws<ArgumentException>(() =>
            new TacticalBattalionAssessmentProfile(
                3, 2, 1, 2, 30, 60));
    }
}
