
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalBattalionReactionTests
{
    private static readonly TacticalBattalionAssessmentProfile
        Assessment = new(
            frontLineWeight: 3,
            supportWeight: 2,
            reserveWeight: 1,
            priorityFrontMultiplier: 2,
            holdThreshold: 60,
            riskThreshold: 30);

    private static readonly TacticalBattalionReactionProfile
        Reaction = new(
            criticalConfirmationHours: 2,
            recoveryConfirmationHours: 2,
            recoveryMargin: 10);

    private sealed record Scenario(
        TacticalBattle Battle,
        Battalion Battalion,
        CompanyDeployment Front,
        CompanyDeployment Reserve,
        BattleSector Priority,
        BattleSector Secondary,
        Division Division);

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

        for (int i = 1; i <= companyCount; i++)
        {
            battalion.AddCompany(new Company(
                i,
                $"Company {i}",
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
        bool hasReserve = true,
        bool assigned = true)
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

        var battlefield = new TacticalBattlefield();

        var priority = new BattleSector(
            1, "Priority", TerrainType.Plains);

        var secondary = new BattleSector(
            2, "Secondary", TerrainType.Plains);

        battlefield.AddSector(priority);
        battlefield.AddSector(secondary);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var battalion = friendly.Battalions[0];

        var front = battle.DeployCompany(
            battalion.Companies[0],
            priority,
            CompanyDeploymentRole.FrontLine);

        battle.DeployCompany(
            battalion.Companies[1],
            secondary,
            CompanyDeploymentRole.FrontLine);

        var reserve = battle.DeployCompany(
            battalion.Companies[2],
            secondary,
            hasReserve
                ? CompanyDeploymentRole.Reserve
                : CompanyDeploymentRole.Support);

        battle.DeployCompany(
            enemy.Battalions[0].Companies[0],
            priority,
            CompanyDeploymentRole.FrontLine);

        if (assigned)
        {
            battle.BattalionAssessments.SetPrioritySector(
                battalion, priority);
        }

        return new Scenario(
            battle,
            battalion,
            front,
            reserve,
            priority,
            secondary,
            friendly);
    }

    /// <summary>
    /// Fournit une evaluation controlee pour tester
    /// les seuils et la stabilite de la machine a etats.
    /// Les autres donnees viennent du vrai bataillon.
    /// </summary>
    private static TacticalBattalionAssessment[] Snapshots(
        Scenario s,
        double? priorityReadiness = null)
    {
        var originals =
            s.Battle.BattalionAssessments.EvaluateAll(
                Assessment);

        return originals.Select(a =>
        {
            if (!ReferenceEquals(a.Battalion, s.Battalion)
                || priorityReadiness is not double value)
            {
                return a;
            }

            var status = value >= Assessment.HoldThreshold
                ? TacticalBattalionMissionStatus.Capable
                : value >= Assessment.RiskThreshold
                    ? TacticalBattalionMissionStatus.AtRisk
                    : TacticalBattalionMissionStatus.UnableToHold;

            return a with
            {
                PrioritySectorReadiness = value,
                MissionStatus = status
            };
        }).ToArray();
    }

    private static TacticalBattalionReactionPhaseResult Step(
        Scenario s, long hour, double readiness)
    {
        return s.Battle.BattalionReactions.ApplyHour(
            hour,
            Snapshots(s, readiness),
            Assessment,
            Reaction);
    }

    private static TacticalBattalionReactionReport Report(
        Scenario s,
        TacticalBattalionReactionPhaseResult phase)
    {
        return Assert.Single(
            phase.Battalions,
            r => ReferenceEquals(r.Battalion, s.Battalion));
    }

    [Fact]
    public void IntactBattalionHoldsWithoutRequests()
    {
        var s = CreateScenario();

        var phase = Step(s, 2, 100);
        var result = Report(s, phase);

        Assert.Equal(
            TacticalBattalionFrontState.Holding,
            result.State);

        Assert.Empty(phase.Requests);
        Assert.Equal(300, s.Division.GetCurrentResourceTotals()
            .Personnel.Available);
    }

    [Fact]
    public void ThreatenedBattalionRequestsReorganizationOnce()
    {
        var s = CreateScenario();

        var first = Step(s, 2, 55);

        var request = Assert.Single(
            first.Requests,
            r => ReferenceEquals(r.Battalion, s.Battalion));

        Assert.Equal(
            TacticalBattalionReactionType.Reorganize,
            request.Type);

        Assert.Equal(
            TacticalBattalionFrontState.Threatened,
            Report(s, first).State);

        // 65 est au-dessus du seuil de tenue (60)
        // mais sous le seuil de retour stabilise (70).
        var second = Step(s, 3, 65);

        Assert.Equal(
            TacticalBattalionFrontState.Threatened,
            Report(s, second).State);

        Assert.Empty(second.Requests);

        var third = Step(s, 4, 70);

        Assert.Equal(
            TacticalBattalionFrontState.Holding,
            Report(s, third).State);
    }

    [Fact]
    public void CriticalSituationRequiresTwoConsecutiveHours()
    {
        var s = CreateScenario();

        var first = Step(s, 2, 20);

        Assert.Equal(
            TacticalBattalionFrontState.Threatened,
            Report(s, first).State);

        Assert.Equal(1,
            Report(s, first).ConsecutiveCriticalHours);

        var second = Step(s, 3, 20);
        var report = Report(s, second);

        Assert.Equal(
            TacticalBattalionFrontState.RuptureConfirmed,
            report.State);

        Assert.Equal(2, report.ConsecutiveCriticalHours);

        var request = Assert.Single(
            second.Requests,
            r => ReferenceEquals(r.Battalion, s.Battalion));

        Assert.Equal(
            TacticalBattalionReactionType.Reinforce,
            request.Type);

        Assert.Single(
            request.ReserveCandidates,
            r => ReferenceEquals(r, s.Reserve));

        // Meme etat a l'heure suivante :
        // ne pas demander les memes renforts en boucle.
        var third = Step(s, 4, 20);

        Assert.Equal(
            TacticalBattalionFrontState.RuptureConfirmed,
            Report(s, third).State);

        Assert.Empty(third.Requests);
    }

    [Fact]
    public void RuptureWithoutReserveRequestsWithdrawal()
    {
        var s = CreateScenario(hasReserve: false);

        Step(s, 2, 20);
        var phase = Step(s, 3, 20);

        var request = Assert.Single(
            phase.Requests,
            r => ReferenceEquals(r.Battalion, s.Battalion));

        Assert.Equal(
            TacticalBattalionReactionType.RequestWithdrawal,
            request.Type);

        Assert.Empty(request.ReserveCandidates);

        Assert.Single(
            request.AffectedFrontline,
            d => ReferenceEquals(d, s.Front));

        // Aucune retraite physique n'a eu lieu.
        Assert.Same(s.Priority, s.Front.Sector);
        Assert.Equal(300, s.Division.GetCurrentResourceTotals()
            .Personnel.Available);
    }

    [Fact]
    public void LossOfLastReserveChangesRequestType()
    {
        var s = CreateScenario();

        Step(s, 2, 20);

        var broken = Step(s, 3, 20);

        Assert.Equal(
            TacticalBattalionReactionType.Reinforce,
            Assert.Single(
                broken.Requests,
                r => ReferenceEquals(
                    r.Battalion, s.Battalion)).Type);

        // Les reserves deviennent indisponibles.
        var pool = s.Reserve.Company.Composition!.Manpower;

        pool.ApplyCasualties(new PersonnelCasualtyReport(
            KilledInAction: 100,
            WoundedInAction: 0,
            MissingOrCaptured: 0));

        var next = Step(s, 4, 20);

        Assert.Equal(
            TacticalBattalionReactionType.RequestWithdrawal,
            Assert.Single(
                next.Requests,
                r => ReferenceEquals(
                    r.Battalion, s.Battalion)).Type);

        Assert.Equal(
            TacticalBattalionFrontState.RuptureConfirmed,
            Report(s, next).State);
    }

    [Fact]
    public void RuptureRequiresTwoGoodHoursToRecover()
    {
        var s = CreateScenario();

        Step(s, 2, 20);
        Step(s, 3, 20);

        // 69 n'atteint pas le seuil de retour de 70.
        var unstable = Step(s, 4, 69);

        Assert.Equal(
            TacticalBattalionFrontState.RuptureConfirmed,
            Report(s, unstable).State);

        var recovering = Step(s, 5, 71);

        Assert.Equal(
            TacticalBattalionFrontState.RuptureConfirmed,
            Report(s, recovering).State);

        Assert.Equal(
            1,
            Report(s, recovering).ConsecutiveRecoveryHours);

        var restored = Step(s, 6, 71);

        Assert.Equal(
            TacticalBattalionFrontState.Holding,
            Report(s, restored).State);

        Assert.Empty(restored.Requests);
    }

    [Fact]
    public void ChangingPrioritySectorResetsCriticalCounter()
    {
        var s = CreateScenario();

        Step(s, 2, 20);

        s.Battle.BattalionAssessments.SetPrioritySector(
            s.Battalion, s.Secondary);

        var changed = Step(s, 3, 20);

        Assert.Equal(
            TacticalBattalionFrontState.Unassigned,
            Report(s, changed).PreviousState);

        Assert.Equal(
            TacticalBattalionFrontState.Threatened,
            Report(s, changed).State);

        Assert.Equal(
            1,
            Report(s, changed).ConsecutiveCriticalHours);

        var next = Step(s, 4, 20);

        Assert.Equal(
            TacticalBattalionFrontState.RuptureConfirmed,
            Report(s, next).State);
    }

    [Fact]
    public void GapsBetweenHoursDoNotConfirmRupture()
    {
        var s = CreateScenario();

        Step(s, 2, 20);

        // Heure 3 non traitee par ce suivi.
        var later = Step(s, 4, 20);

        Assert.Equal(
            TacticalBattalionFrontState.Threatened,
            Report(s, later).State);

        Assert.Equal(
            1,
            Report(s, later).ConsecutiveCriticalHours);
    }

    [Fact]
    public void UnassignedMissionDoesNotCreateRequests()
    {
        var s = CreateScenario(assigned: false);

        var phase = s.Battle.BattalionReactions.ApplyHour(
            2, Snapshots(s), Assessment, Reaction);

        Assert.Equal(
            TacticalBattalionFrontState.Unassigned,
            Report(s, phase).State);

        Assert.Empty(phase.Requests);
    }

    [Fact]
    public void InvalidBatchDoesNotPartiallyUpdateState()
    {
        var s = CreateScenario();
        var source = Snapshots(s, 20);

        var french = Assert.Single(
            source,
            a => ReferenceEquals(a.Battalion, s.Battalion));

        // Deux fois le meme bataillon ;
        // l'autre est absent de la phase.
        var invalid = new[] { french, french };

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.BattalionReactions.ApplyHour(
                2, invalid, Assessment, Reaction));

        // La meme heure reste traitable apres l'echec.
        var valid = Step(s, 2, 100);

        Assert.Equal(
            TacticalBattalionFrontState.Holding,
            Report(s, valid).State);
    }

    [Fact]
    public void CannotApplySameReactionHourTwice()
    {
        var s = CreateScenario();

        Step(s, 2, 55);

        Assert.Throws<InvalidOperationException>(() =>
            Step(s, 2, 55));
    }

    [Fact]
    public void ReadingReactionsDoesNotModifyMilitaryResources()
    {
        var s = CreateScenario();

        var before = s.Division.GetCurrentResourceTotals();

        Step(s, 2, 20);
        Step(s, 3, 20);

        Assert.Equal(
            before,
            s.Division.GetCurrentResourceTotals());

        Assert.Same(s.Priority, s.Front.Sector);
        Assert.Equal(
            CompanyDeploymentRole.FrontLine,
            s.Front.Role);
    }

    [Fact]
    public void InvalidReactionProfilesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalBattalionReactionProfile(0, 2, 10));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalBattalionReactionProfile(2, 0, 10));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalBattalionReactionProfile(
                2, 2, double.NaN));

        var impossible = new TacticalBattalionReactionProfile(
            2, 2, 50);

        Assert.Throws<InvalidOperationException>(() =>
            impossible.ValidateFor(Assessment));
    }
}
