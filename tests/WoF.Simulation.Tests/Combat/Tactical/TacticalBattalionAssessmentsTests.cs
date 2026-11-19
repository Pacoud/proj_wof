
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


    private static TacticalBattalionSectorAssessment SectorReport(
        Scenario s,
        BattleSector sector)
    {
        var reports =
            s.Battle.BattalionAssessments.EvaluateSectors(
                s.Battalion, Profile);

        return Assert.Single(
            reports,
            r => ReferenceEquals(r.Sector, sector));
    }

    private static Division MakeDivision(
        Country country,
        Province province,
        int companyCount,
        int personnelPerCompany = 100)
    {
        var division = new Division(
            $"{country.Name} Division",
            province,
            fuel: personnelPerCompany,
            ammunition: personnelPerCompany,
            country: country,
            composition: new DivisionComposition(
                manpower: companyCount * personnelPerCompany,
                infantryEquipment: companyCount * personnelPerCompany,
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
                    manpower: personnelPerCompany,
                    infantryEquipment: personnelPerCompany)));
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

     [Fact]
    public void BattalionReportsSeparateSectorReadiness()
    {
        var s = CreateScenario();

        var priority = SectorReport(s, s.PrioritySector);
        var secondary = SectorReport(s, s.SecondarySector);

        Assert.Equal(100, priority.FrontLineReadiness, 6);
        Assert.Equal(100, secondary.FrontLineReadiness, 6);

        Assert.True(priority.IsPrioritySector);
        Assert.False(secondary.IsPrioritySector);

        Assert.Equal(1, priority.FrontLineAssignments);
        Assert.Equal(1, secondary.FrontLineAssignments);
        Assert.Equal(1, secondary.ReserveAssignments);

        Assert.Equal(
            TacticalBattalionSectorStatus.Capable,
            priority.Status);
    }

    [Fact]
    public void SecondarySectorLossesDoNotReducePriorityReadiness()
    {
        var s = CreateScenario();

        ApplyLosses(
            s,
            s.SecondaryFront,
            killed: 10,
            wounded: 10);

        var priority = SectorReport(s, s.PrioritySector);
        var secondary = SectorReport(s, s.SecondarySector);

        Assert.Equal(100, priority.FrontLineReadiness, 6);
        Assert.Equal(56, secondary.FrontLineReadiness, 6);

        Assert.Equal(
            TacticalBattalionSectorStatus.AtRisk,
            secondary.Status);

        Assert.Equal(80, secondary.AvailableFrontLinePersonnel);
        Assert.Equal(100, secondary.AuthorizedFrontLinePersonnel);
    }

    [Fact]
    public void DestroyedFrontLineRemainsVisibleInAssessment()
    {
        var s = CreateScenario();

        ApplyLosses(
            s,
            s.PriorityFront,
            killed: 100,
            wounded: 0);

        var report = SectorReport(s, s.PrioritySector);

        Assert.Equal(1, report.FrontLineAssignments);
        Assert.Equal(1, report.StationaryFrontLineCompanies);
        Assert.Equal(100, report.AuthorizedFrontLinePersonnel);
        Assert.Equal(0, report.AvailableFrontLinePersonnel);
        Assert.Equal(0, report.FrontLineReadiness, 6);

        Assert.Equal(
            TacticalBattalionSectorStatus.UnableToHold,
            report.Status);
    }

    [Fact]
    public void PrioritySectorWithoutFrontLineIsUncovered()
    {
        var s = CreateScenario();

        var empty = new BattleSector(
            3, "Unoccupied", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(empty);

        s.Battle.BattalionAssessments.SetPrioritySector(
            s.Battalion, empty);

        var report = SectorReport(s, empty);

        Assert.True(report.IsPrioritySector);
        Assert.Equal(0, report.FrontLineAssignments);
        Assert.Equal(0, report.FrontLineReadiness, 6);

        Assert.Equal(
            TacticalBattalionSectorStatus.Uncovered,
            report.Status);

        // L'ancien diagnostic de mission reste
        // compatible et inchange.
        Assert.Equal(
            TacticalBattalionMissionStatus.UnableToHold,
            Assess(s).MissionStatus);
    }

    [Fact]
    public void CompanyInTransitDoesNotHoldItsSector()
    {
        var s = CreateScenario();

        s.PriorityFront.SetInTransit(true);

        var report = SectorReport(s, s.PrioritySector);

        Assert.Equal(1, report.FrontLineAssignments);
        Assert.Equal(0, report.StationaryFrontLineCompanies);
        Assert.Equal(0, report.AvailableFrontLinePersonnel);

        Assert.Equal(
            TacticalBattalionSectorStatus.Uncovered,
            report.Status);
    }

    [Fact]
    public void TwoCompaniesShareSectorReadinessByAuthorizedStrength()
    {
        var s = CreateScenario();

        s.SecondaryFront.ApplyMovementArrival(
            s.PrioritySector,
            CompanyDeploymentRole.FrontLine);

        s.SecondaryFront.Company.Cohesion = 50;

        var report = SectorReport(s, s.PrioritySector);

        Assert.Equal(2, report.FrontLineAssignments);
        Assert.Equal(200, report.AuthorizedFrontLinePersonnel);
        Assert.Equal(200, report.AvailableFrontLinePersonnel);
        Assert.Equal(75, report.FrontLineReadiness, 6);

        Assert.Equal(
            Assess(s).PrioritySectorReadiness!.Value,
            report.FrontLineReadiness,
            6);

        // La reserve sur le secteur secondaire
        // ne constitue pas une ligne de front.
        var secondary = SectorReport(s, s.SecondarySector);

        Assert.Equal(1, secondary.ReserveAssignments);
        Assert.Equal(
            TacticalBattalionSectorStatus.Uncovered,
            secondary.Status);
    }

    [Fact]
    public void SectorReadinessUsesCurrentPersistentCohesion()
    {
        var s = CreateScenario();

        s.PriorityFront.Company.Cohesion = 40;

        Assert.Equal(
            40,
            SectorReport(s, s.PrioritySector).FrontLineReadiness,
            6);

        // Simule une nouvelle valeur de cohesion
        // persistante sans nouveau stock de bataillon.
        s.PriorityFront.Company.Cohesion = 55;

        Assert.Equal(
            55,
            SectorReport(s, s.PrioritySector).FrontLineReadiness,
            6);
    }

    [Fact]
    public void EvaluateAllSectorsCoversRepresentedBattalions()
    {
        var s = CreateScenario();

        var results =
            s.Battle.BattalionAssessments.EvaluateAllSectors(
                Profile);

        // Deux secteurs pour le bataillon francais,
        // un secteur pour le bataillon allemand.
        Assert.Equal(3, results.Count);

        Assert.Equal(2, results.Count(r =>
            ReferenceEquals(r.Battalion, s.Battalion)));
    }

    [Fact]
    public void ForeignAndPartiallyDeployedBattalionsAreRejected()
    {
        var first = CreateScenario();
        var second = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            first.Battle.BattalionAssessments.EvaluateSectors(
                second.Battalion,
                Profile));

        var partial = CreateScenario(deployReserve: false);

        Assert.Throws<InvalidOperationException>(() =>
            partial.Battle.BattalionAssessments.EvaluateSectors(
                partial.Battalion,
                Profile));
    }

    [Fact]
    public void ReadingSectorAssessmentsDoesNotMutateMilitaryState()
    {
        var s = CreateScenario();

        var before =
            s.FriendlyDivision.GetCurrentResourceTotals();

        double cohesion = s.PriorityFront.Company.Cohesion;

        var first =
            s.Battle.BattalionAssessments.EvaluateSectors(
                s.Battalion,
                Profile);

        var second =
            s.Battle.BattalionAssessments.EvaluateSectors(
                s.Battalion,
                Profile);

        Assert.Equal(first, second);
        Assert.Equal(
            before,
            s.FriendlyDivision.GetCurrentResourceTotals());
        Assert.Equal(
            cohesion,
            s.PriorityFront.Company.Cohesion,
            6);
    }


    [Fact]
    public void DestroyedFrontLineCannotBeAtRiskWithZeroRiskThreshold()
    {
        var s = CreateScenario();

        ApplyLosses(
            s,
            s.PriorityFront,
            killed: 100,
            wounded: 0);

        var profile = new TacticalBattalionAssessmentProfile(
            frontLineWeight: 3,
            supportWeight: 2,
            reserveWeight: 1,
            priorityFrontMultiplier: 2,
            holdThreshold: 60,
            riskThreshold: 0);

        var report = Assert.Single(
            s.Battle.BattalionAssessments.EvaluateSectors(
                s.Battalion, profile),
            r => ReferenceEquals(r.Sector, s.PrioritySector));

        Assert.Equal(0, report.AvailableFrontLinePersonnel);

        Assert.Equal(
            TacticalBattalionSectorStatus.UnableToHold,
            report.Status);
    }


    [Fact]
    public void PrioritySectorMatchesHistoricalFloatingPointResult()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var friendly = MakeDivision(
            france, province, 1, personnelPerCompany: 22);

        var enemy = MakeDivision(germany, province, 1);

        var company = friendly.Battalions[0].Companies[0];
        company.Cohesion = 88;

        company.Composition!.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 7,
                WoundedInAction: 0,
                MissingOrCaptured: 0));

        Assert.Equal(15, company.Composition.Manpower.Current);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { friendly, enemy },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var battlefield = new TacticalBattlefield();
        var sector = new BattleSector(
            1, "Front", TerrainType.Plains);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        battle.DeployCompany(
            company,
            sector,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            enemy.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var battalion = friendly.Battalions[0];

        battle.BattalionAssessments.SetPrioritySector(
            battalion, sector);

        var historical =
            battle.BattalionAssessments.Evaluate(
                battalion, Profile);

        var sectorReport = Assert.Single(
            battle.BattalionAssessments.EvaluateSectors(
                battalion, Profile));

        Assert.Equal(
            historical.PrioritySectorReadiness!.Value,
            sectorReport.FrontLineReadiness);

        Assert.Equal(
            TacticalBattalionMissionStatus.AtRisk,
            historical.MissionStatus);

        Assert.Equal(
            TacticalBattalionSectorStatus.AtRisk,
            sectorReport.Status);
    }



    private static TacticalBattalionFrontContinuityReport
        AssessFront(Scenario s, params BattleSector[] sectors)
    {
        return new TacticalBattalionFrontContinuitySystem(
            s.Battle).Evaluate(
                s.Battalion,
                Profile,
                sectors);
    }

    [Fact]
    public void EmptySectorBetweenTwoPositionsCreatesInteriorGap()
    {
        var s = CreateScenario();
        var third = new BattleSector(
            3, "Third", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(third);
        s.Battle.Battlefield.Connect(
            s.PrioritySector, s.SecondarySector, 100);
        s.Battle.Battlefield.Connect(
            s.SecondarySector, third, 100);

        s.SecondaryFront.ApplyMovementArrival(
            third, CompanyDeploymentRole.FrontLine);

        var result = AssessFront(
            s, s.PrioritySector, s.SecondarySector, third);

        Assert.Equal(2, result.OccupiedComponents.Count);

        var gap = Assert.Single(result.Gaps);
        Assert.Equal(TacticalFrontGapKind.Interior, gap.Kind);
        Assert.Same(s.SecondarySector, Assert.Single(gap.Sectors));

        Assert.Contains(s.PrioritySector,
            gap.AdjacentOccupiedSectors);
        Assert.Contains(third,
            gap.AdjacentOccupiedSectors);

        var empty = Assert.Single(result.Sectors,
            r => ReferenceEquals(
                r.Sector, s.SecondarySector));

        Assert.Equal(1, empty.ReserveAssignments);
        Assert.Equal(0, empty.AvailableFrontLinePersonnel);
        Assert.Equal(
            TacticalBattalionSectorStatus.Uncovered,
            empty.Status);
    }

    [Fact]
    public void TwoConsecutiveEmptySectorsCreateOneGap()
    {
        var s = CreateScenario();

        var third = new BattleSector(
            3, "Third", TerrainType.Plains);
        var fourth = new BattleSector(
            4, "Fourth", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(third);
        s.Battle.Battlefield.AddSector(fourth);

        s.Battle.Battlefield.Connect(
            s.PrioritySector, s.SecondarySector, 100);
        s.Battle.Battlefield.Connect(
            s.SecondarySector, third, 100);
        s.Battle.Battlefield.Connect(
            third, fourth, 100);

        s.SecondaryFront.ApplyMovementArrival(
            fourth, CompanyDeploymentRole.FrontLine);

        var result = AssessFront(
            s, s.PrioritySector, s.SecondarySector,
            third, fourth);

        var gap = Assert.Single(result.Gaps);

        Assert.Equal(TacticalFrontGapKind.Interior, gap.Kind);
        Assert.Equal(2, gap.Sectors.Count);
        Assert.Contains(s.SecondarySector, gap.Sectors);
        Assert.Contains(third, gap.Sectors);
        Assert.Equal(2, gap.AdjacentOccupiedSectors.Count);
    }

    [Fact]
    public void UncoveredEndIsClassifiedAsBoundary()
    {
        var s = CreateScenario();

        var third = new BattleSector(
            3, "Third", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(third);

        s.Battle.Battlefield.Connect(
            s.PrioritySector, s.SecondarySector, 100);
        s.Battle.Battlefield.Connect(
            s.SecondarySector, third, 100);

        s.SecondaryFront.ApplyMovementArrival(
            s.PrioritySector,
            CompanyDeploymentRole.FrontLine);

        var result = AssessFront(
            s, s.PrioritySector, s.SecondarySector, third);

        Assert.Single(result.OccupiedComponents);

        var gap = Assert.Single(result.Gaps);

        Assert.Equal(TacticalFrontGapKind.Boundary, gap.Kind);
        Assert.Equal(2, gap.Sectors.Count);
        Assert.Same(
            s.PrioritySector,
            Assert.Single(gap.AdjacentOccupiedSectors));
    }

    [Fact]
    public void EntirelyUnoccupiedResponsibilityIsUnanchored()
    {
        var s = CreateScenario();

        var third = new BattleSector(
            3, "Third", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(third);
        s.Battle.Battlefield.Connect(
            s.SecondarySector, third, 100);

        s.SecondaryFront.ApplyMovementArrival(
            s.PrioritySector,
            CompanyDeploymentRole.FrontLine);

        var result = AssessFront(
            s, s.SecondarySector, third);

        Assert.Empty(result.OccupiedComponents);

        var gap = Assert.Single(result.Gaps);

        Assert.Equal(TacticalFrontGapKind.Unanchored, gap.Kind);
        Assert.Empty(gap.AdjacentOccupiedSectors);
        Assert.Equal(2, gap.Sectors.Count);
    }

    [Fact]
    public void DisconnectedOccupiedSectorsRemainSeparate()
    {
        var s = CreateScenario();

        // Aucune connexion entre les deux secteurs.
        var result = AssessFront(
            s, s.PrioritySector, s.SecondarySector);

        Assert.Equal(2, result.OccupiedComponents.Count);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void ZeroCohesionDoesNotRemovePhysicalOccupation()
    {
        var s = CreateScenario();

        s.Battle.Battlefield.Connect(
            s.PrioritySector, s.SecondarySector, 100);

        s.PriorityFront.Company.Cohesion = 0;

        var result = AssessFront(
            s, s.PrioritySector, s.SecondarySector);

        Assert.Single(result.OccupiedComponents);
        Assert.Empty(result.Gaps);

        var priority = Assert.Single(result.Sectors,
            r => ReferenceEquals(
                r.Sector, s.PrioritySector));

        Assert.Equal(0, priority.FrontLineReadiness, 6);
        Assert.Equal(
            TacticalBattalionSectorStatus.UnableToHold,
            priority.Status);
    }

    [Fact]
    public void CompanyInTransitCannotCloseTheFrontGap()
    {
        var s = CreateScenario();

        s.Battle.Battlefield.Connect(
            s.PrioritySector, s.SecondarySector, 100);

        s.SecondaryFront.SetInTransit(true);

        var result = AssessFront(
            s, s.PrioritySector, s.SecondarySector);

        var gap = Assert.Single(result.Gaps);

        Assert.Equal(TacticalFrontGapKind.Boundary, gap.Kind);
        Assert.Same(
            s.SecondarySector,
            Assert.Single(gap.Sectors));
    }

    [Fact]
    public void OnlyDeclaredFrontSectorsAreAnalyzed()
    {
        var s = CreateScenario();
        var third = new BattleSector(
            3, "Third", TerrainType.Plains);

        s.Battle.Battlefield.AddSector(third);
        s.Battle.Battlefield.Connect(
            s.PrioritySector, s.SecondarySector, 100);
        s.Battle.Battlefield.Connect(
            s.SecondarySector, third, 100);

        s.SecondaryFront.ApplyMovementArrival(
            third, CompanyDeploymentRole.FrontLine);

        // B n'est pas dans la responsabilite declaree.
        var result = AssessFront(
            s, s.PrioritySector, third);

        Assert.Empty(result.Gaps);
        Assert.Equal(2, result.OccupiedComponents.Count);
        Assert.Equal(2, result.Sectors.Count);
    }

    [Fact]
    public void InvalidFrontResponsibilityIsRejected()
    {
        var s = CreateScenario();

        var foreign = new BattleSector(
            99, "Foreign", TerrainType.Plains);

        Assert.Throws<ArgumentException>(() =>
            AssessFront(s));

        Assert.Throws<ArgumentException>(() =>
            AssessFront(
                s, s.PrioritySector, s.PrioritySector));

        Assert.Throws<ArgumentException>(() =>
            AssessFront(s, foreign));
    }

    [Fact]
    public void ContinuityEvaluationDoesNotChangeMilitaryState()
    {
        var s = CreateScenario();

        s.Battle.Battlefield.Connect(
            s.PrioritySector, s.SecondarySector, 100);

        var resourcesBefore =
            s.FriendlyDivision.GetCurrentResourceTotals();

        double cohesionBefore =
            s.PriorityFront.Company.Cohesion;

        var first = AssessFront(
            s, s.PrioritySector, s.SecondarySector);

        var second = AssessFront(
            s, s.PrioritySector, s.SecondarySector);

        Assert.Equal(
            first.Sectors.ToArray(),
            second.Sectors.ToArray());

        Assert.Equal(
            resourcesBefore,
            s.FriendlyDivision.GetCurrentResourceTotals());

        Assert.Equal(
            cohesionBefore,
            s.PriorityFront.Company.Cohesion,
            6);
    }



}
