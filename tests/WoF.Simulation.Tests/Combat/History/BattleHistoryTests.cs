using System.Text.Json;
using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.History;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.History;

public class BattleHistoryTests
{
    [Fact]
    public void SnapshotCopiesStatsWithoutKeepingLivePools()
    {
        var scenario = CreateProvinceScenario();
        var division = scenario.French;
        var snapshot = ParticipantBattleSnapshot.Capture(division);

        ApplyLosses(division, killed: 3, wounded: 5, missing: 2, damaged: 2, destroyed: 1);
        division.LoseOrganization(4);
        division.ConsumeFuel(7);

        Assert.Equal(1000, snapshot.Manpower);
        Assert.Equal(0, snapshot.KilledInAction);
        Assert.Equal(0, snapshot.TotalWoundedInAction);
        Assert.Equal(0, snapshot.MissingOrCaptured);
        Assert.Equal(20, snapshot.OperationalTanks);
        Assert.Equal(0, snapshot.DamagedTanks);
        Assert.Equal(0, snapshot.DestroyedTanks);
        Assert.Equal(100, snapshot.Organization);
        Assert.Equal(80, snapshot.Fuel);
        Assert.NotEqual(snapshot, ParticipantBattleSnapshot.Capture(division));
    }

    [Fact]
    public void ReportExcludesPreviousLossesAndUsesCumulativeWounded()
    {
        var scenario = CreateProvinceScenario();
        ApplyLosses(scenario.French, 7, 11, 3, damaged: 2, destroyed: 4);
        scenario.French.LoseOrganization(8);
        scenario.French.ConsumeFuel(9);
        var initial = ParticipantBattleSnapshot.Capture(scenario.French);
        var engagement = BeginProvinceBattle(scenario, currentHour: 10);

        ApplyLosses(scenario.French, 2, 5, 1, damaged: 1, destroyed: 2);
        scenario.French.Composition.Manpower.ReturnWoundedToDuty(100);
        BreakAll(engagement);
        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 14);

        var report = Assert.Single(scenario.Simulation.Engagements.History.Reports);
        var french = FindParticipant(report, scenario.French);

        Assert.Equal(initial, french.Initial);
        Assert.Equal(ParticipantBattleSnapshot.Capture(scenario.French), french.Final);
        Assert.Equal(10, french.JoinedAtHour);
        Assert.Equal(EngagementRole.Attacker, french.Role);
        Assert.Equal(2, french.Killed);
        Assert.Equal(5, french.Wounded);
        Assert.Equal(1, french.MissingOrCaptured);
        Assert.Equal(8, french.TotalCasualties);
        Assert.Equal(2, french.TanksDestroyed);
        Assert.Equal(0, scenario.French.Composition.Manpower.WoundedUnavailable);
        Assert.Equal(16, french.Final.TotalWoundedInAction);
        Assert.Equal(2, report.TotalKilled);
        Assert.Equal(5, report.TotalWounded);
        Assert.Equal(1, report.TotalMissingOrCaptured);
        Assert.Equal(8, report.TotalCasualties);
        Assert.Equal(2, report.TotalTanksDestroyed);
    }

    [Fact]
    public void LateProvinceReinforcementKeepsItsOwnEntryHourAndSnapshot()
    {
        var scenario = CreateProvinceScenario();
        var engagement = BeginProvinceBattle(scenario, currentHour: 10);
        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 11);

        var reinforcement = CreateDivision(
            "German reinforcement", scenario.Province, scenario.German.Country!);
        ApplyLosses(reinforcement, 30, 40, 5, destroyed: 2);
        var entry = ParticipantBattleSnapshot.Capture(reinforcement);
        var divisions = new[] { scenario.French, scenario.German, reinforcement };

        scenario.Simulation.Engagements.DetectProvinceEngagements(
            divisions, currentHour: 15, arrivals: new[] { reinforcement });

        Assert.Same(engagement, reinforcement.CurrentEngagement);
        Assert.Equal(EngagementRole.Defender, engagement.GetRole(reinforcement));
        Assert.Equal(15, engagement.GetJoinHour(reinforcement));
        Assert.Equal(entry, engagement.GetInitialSnapshot(reinforcement));

        ApplyLosses(reinforcement, 3, 4, 2, destroyed: 1);
        // A second detection must not overwrite the original entry state.
        scenario.Simulation.Engagements.DetectProvinceEngagements(
            divisions, currentHour: 16, arrivals: new[] { reinforcement });
        BreakAll(engagement);
        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 20);

        var report = Assert.Single(scenario.Simulation.Engagements.History.Reports);
        var participant = FindParticipant(report, reinforcement);
        Assert.Equal(3, report.Participants.Count);
        Assert.Equal(10, report.StartHour);
        Assert.Equal(15, participant.JoinedAtHour);
        Assert.Equal(entry, participant.Initial);
        Assert.Equal(3, participant.Killed);
        Assert.Equal(4, participant.Wounded);
        Assert.Equal(2, participant.MissingOrCaptured);
        Assert.Equal(1, participant.TanksDestroyed);
        Assert.Equal(10, FindParticipant(report, scenario.French).JoinedAtHour);
        Assert.Equal(10, FindParticipant(report, scenario.German).JoinedAtHour);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LateTransitReinforcementIsCapturedAtContactInEitherDirection(bool fromA)
    {
        var scenario = CreateTransitScenario();
        scenario.Simulation.Tick();
        scenario.Simulation.Tick();
        var engagement = Assert.Single(scenario.Simulation.Engagements.ActiveEngagements);

        var reinforcement = CreateDivision(
            "Transit reinforcement",
            fromA ? scenario.A : scenario.B,
            fromA ? scenario.French.Country! : scenario.German.Country!);
        ApplyLosses(reinforcement, 30, 40, 5, destroyed: 2);
        Assert.True(reinforcement.TryStartMovement(
            fromA ? scenario.B : scenario.A,
            new MovementPlan(DurationHours: 5, FuelPerHour: 0)));

        reinforcement.AdvanceOneHour();
        reinforcement.AdvanceOneHour();
        // It is on the right connection, but has not reached the battle yet.
        scenario.Simulation.Engagements.DetectTransitEngagements(
            new[] { scenario.French, scenario.German, reinforcement }, currentHour: 2);
        Assert.False(reinforcement.IsEngaged);

        var entry = ParticipantBattleSnapshot.Capture(reinforcement);
        scenario.Simulation.AddDivision(reinforcement);
        scenario.Simulation.Tick();

        Assert.Same(engagement, reinforcement.CurrentEngagement);
        Assert.Equal(3, engagement.GetJoinHour(reinforcement));
        Assert.Equal(entry, engagement.GetInitialSnapshot(reinforcement));
        Assert.Equal(0.5, reinforcement.Transit!.Progress, 6);
        Assert.True(reinforcement.Transit.IsPaused);

        ApplyLosses(reinforcement, 2, 4, 1, destroyed: 1);
        BreakAll(engagement);
        scenario.Simulation.Tick();

        var report = Assert.Single(scenario.Simulation.Engagements.History.Reports);
        var participant = FindParticipant(report, reinforcement);
        Assert.Equal(EngagementType.MeetingEngagement, report.Type);
        Assert.Equal("A ↔ B", report.Location);
        Assert.Equal(engagement.BattleTerrain, report.Terrain);
        Assert.Equal(2, report.StartHour);
        Assert.Equal(4, report.EndHour);
        Assert.Equal(3, participant.JoinedAtHour);
        Assert.Equal(EngagementRole.None, participant.Role);
        Assert.Equal(2, participant.Killed);
        Assert.Equal(4, participant.Wounded);
        Assert.Equal(1, participant.MissingOrCaptured);
        Assert.Equal(1, participant.TanksDestroyed);
    }

    [Fact]
    public void ReinforcementCrossingWholeConnectionJoinsBeforeArrivalAndCapture()
    {
        var scenario = CreateTransitScenario();
        scenario.Simulation.Tick();
        scenario.Simulation.Tick();
        var engagement = Assert.Single(scenario.Simulation.Engagements.ActiveEngagements);
        var reinforcement = CreateDivision(
            "Fast reinforcement", scenario.B, scenario.German.Country!);
        Assert.True(reinforcement.TryStartMovement(
            scenario.A, new MovementPlan(DurationHours: 1, FuelPerHour: 0)));
        scenario.Simulation.AddDivision(reinforcement);

        scenario.Simulation.Tick();

        Assert.Same(engagement, reinforcement.CurrentEngagement);
        Assert.Equal(3, engagement.GetJoinHour(reinforcement));
        Assert.Null(reinforcement.CurrentProvince);
        Assert.False(reinforcement.Transit!.IsCompleted);
        Assert.True(reinforcement.Transit.IsPaused);
        Assert.Same(scenario.French.Country, scenario.A.Controller);
        Assert.Empty(scenario.Simulation.Engagements.History.Reports);
    }

    [Fact]
    public void ThirdDivisionJoiningNewMeetingHasSameTickEntryHour()
    {
        var scenario = CreateTransitScenario();
        var reinforcement = CreateDivision(
            "Third division", scenario.B, scenario.German.Country!);
        Assert.True(reinforcement.TryStartMovement(
            scenario.A, new MovementPlan(DurationHours: 4, FuelPerHour: 0)));
        scenario.Simulation.AddDivision(reinforcement);

        scenario.Simulation.Tick();
        scenario.Simulation.Tick();

        var engagement = Assert.Single(scenario.Simulation.Engagements.ActiveEngagements);
        Assert.Equal(3, engagement.Participants.Count);
        Assert.Same(engagement, reinforcement.CurrentEngagement);
        Assert.All(engagement.Participants,
            participant => Assert.Equal(2, engagement.GetJoinHour(participant)));
    }

    [Fact]
    public void RepeatedParticipantAdditionDoesNotResetEntryButCanAssignRole()
    {
        var scenario = CreateProvinceScenario();
        var engagement = new Engagement(1, 10, scenario.Province);
        engagement.AddParticipant(scenario.French, currentHour: 10);
        var initial = engagement.GetInitialSnapshot(scenario.French);
        ApplyLosses(scenario.French, 3, 4, 1);

        engagement.AddParticipant(
            scenario.French, currentHour: 15, role: EngagementRole.Attacker);

        Assert.Single(engagement.Participants);
        Assert.Equal(initial, engagement.GetInitialSnapshot(scenario.French));
        Assert.Equal(10, engagement.GetJoinHour(scenario.French));
        Assert.Equal(EngagementRole.Attacker, engagement.GetRole(scenario.French));
    }

    [Fact]
    public void EngineArchivesFinishedBattleWithClockWinnerAndFinalCombatStats()
    {
        var scenario = CreateProvinceScenario();
        scenario.German.LoseOrganization(99.99);
        scenario.Simulation.Tick();
        var engagement = Assert.Single(scenario.Simulation.Engagements.ActiveEngagements);
        Assert.Empty(scenario.Simulation.Engagements.History.Reports);

        // The existing combat formulas cause the German division to break.
        scenario.Simulation.Tick();

        var report = Assert.Single(scenario.Simulation.Engagements.History.Reports);
        Assert.False(engagement.IsActive);
        Assert.Equal(2L, engagement.EndHour);
        Assert.Same(engagement, Assert.Single(scenario.Simulation.Engagements.EndedThisTick));
        Assert.Null(scenario.French.CurrentEngagement);
        Assert.Null(scenario.German.CurrentEngagement);
        Assert.Equal(engagement.Id, report.EngagementId);
        Assert.Equal(EngagementType.ProvinceBattle, report.Type);
        Assert.Equal("Battlefield", report.Location);
        Assert.Equal(TerrainType.Forest, report.Terrain);
        Assert.Equal(1, report.StartHour);
        Assert.Equal(2, report.EndHour);
        Assert.Equal(1, report.DurationHours);
        Assert.Equal(scenario.French.Country!.Id, report.WinnerCountryId);
        Assert.Equal(scenario.French.Country.Name, report.WinnerCountryName);
        Assert.Equal(2, report.Participants.Count);
        Assert.Equal(0, FindParticipant(report, scenario.German).Final.Organization);
        var finalFrench = FindParticipant(report, scenario.French).Final;
        // The engine recovers organization later in the same tick. The ledger
        // must preserve the end-of-combat state, not that later recovery.
        Assert.True(finalFrench.Organization < scenario.French.Organization);
        Assert.Equal(
            ParticipantBattleSnapshot.Capture(scenario.French)
                with { Organization = finalFrench.Organization },
            finalFrench);

        scenario.Simulation.Tick();
        Assert.Same(report, Assert.Single(scenario.Simulation.Engagements.History.Reports));
        Assert.Empty(scenario.Simulation.Engagements.EndedThisTick);
    }

    [Fact]
    public void BattleWithNoCombatCapableCountryArchivesWithoutWinner()
    {
        var scenario = CreateProvinceScenario();
        var engagement = BeginProvinceBattle(scenario);
        BreakAll(engagement);

        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 12);

        var report = Assert.Single(scenario.Simulation.Engagements.History.Reports);
        Assert.Null(report.WinnerCountryId);
        Assert.Null(report.WinnerCountryName);
        Assert.All(report.Participants, participant => Assert.Equal(0, participant.Final.Organization));
        Assert.Equal(12, report.EndHour);
    }

    [Fact]
    public void ResolutionWithOneRemainingParticipantAlsoArchives()
    {
        var scenario = CreateProvinceScenario();
        var engagement = BeginProvinceBattle(scenario);
        scenario.German.LeaveEngagement(engagement);

        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 12);

        var report = Assert.Single(scenario.Simulation.Engagements.History.Reports);
        Assert.False(engagement.IsActive);
        Assert.Equal(scenario.French.Country!.Id, report.WinnerCountryId);
        Assert.Equal(2, report.Participants.Count);
        Assert.Equal(12, report.EndHour);
        Assert.All(report.Participants, participant =>
        {
            Assert.NotSame(participant.Initial, participant.Final);
            Assert.Equal(0, participant.TotalCasualties);
            Assert.Equal(0, participant.TanksDestroyed);
        });
    }

    [Fact]
    public void SuccessiveBattlesCountOnlyTheirOwnLosses()
    {
        var scenario = CreateProvinceScenario();
        BeginProvinceBattle(scenario, currentHour: 10);
        ApplyLosses(scenario.French, 3, 2, 1, destroyed: 1);
        scenario.German.LoseOrganization(scenario.German.Organization);
        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 11);
        var first = Assert.Single(scenario.Simulation.Engagements.History.Reports);
        var firstJson = JsonSerializer.Serialize(first);

        // Restore the defeated formation for a separate engagement.
        scenario.German.CompleteRetreat();
        scenario.German.RecoverOrganization(100);
        var secondEngagement = BeginProvinceBattle(scenario, currentHour: 20);
        ApplyLosses(scenario.French, 4, 7, 2, destroyed: 2);
        BreakAll(secondEngagement);
        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 22);

        var history = scenario.Simulation.Engagements.History.Reports;
        Assert.Equal(2, history.Count);
        Assert.NotEqual(first.EngagementId, history[1].EngagementId);
        Assert.Equal(firstJson, JsonSerializer.Serialize(history[0]));
        var secondFrench = FindParticipant(history[1], scenario.French);
        Assert.Equal(3, secondFrench.Initial.KilledInAction);
        Assert.Equal(2, secondFrench.Initial.TotalWoundedInAction);
        Assert.Equal(1, secondFrench.Initial.MissingOrCaptured);
        Assert.Equal(1, secondFrench.Initial.DestroyedTanks);
        Assert.Equal(4, secondFrench.Killed);
        Assert.Equal(7, secondFrench.Wounded);
        Assert.Equal(2, secondFrench.MissingOrCaptured);
        Assert.Equal(2, secondFrench.TanksDestroyed);
    }

    [Fact]
    public void ArchivedReportRemainsUnchangedAfterLiveDivisionEvolution()
    {
        var scenario = CreateProvinceScenario();
        var engagement = BeginProvinceBattle(scenario);
        ApplyLosses(scenario.French, 3, 4, 1, damaged: 2, destroyed: 1);
        BreakAll(engagement);
        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 12);
        var report = Assert.Single(scenario.Simulation.Engagements.History.Reports);
        var frozenJson = JsonSerializer.Serialize(report);

        ApplyLosses(scenario.French, 5, 6, 2, damaged: 3, destroyed: 2);
        scenario.French.Composition.Manpower.ReturnWoundedToDuty(100);
        scenario.French.Composition.Manpower.Reinforce(100);
        scenario.French.Composition.Tanks.Repair(100);
        scenario.French.Composition.Tanks.Reinforce(100);
        scenario.French.RecoverOrganization(100);
        scenario.French.ConsumeFuel(30);
        scenario.French.ReceiveFuel(50);
        scenario.Province.ChangeController(null);

        Assert.Equal(frozenJson, JsonSerializer.Serialize(report));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ParticipantBattleReport>)report.Participants).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<BattleReport>)scenario.Simulation.Engagements.History.Reports).Clear());
    }

    [Fact]
    public void BattleReportDefensivelyCopiesCallerOwnedParticipantCollection()
    {
        var scenario = CreateProvinceScenario();
        var snapshot = ParticipantBattleSnapshot.Capture(scenario.French);
        var participant = new ParticipantBattleReport(
            scenario.French.Name, 1, "France", EngagementRole.Attacker, 10, snapshot, snapshot);
        var source = new List<ParticipantBattleReport> { participant };
        var report = new BattleReport(
            1, EngagementType.ProvinceBattle, "Battlefield", TerrainType.Forest,
            10, 12, 1, "France", source);
        var history = new BattleHistory();
        history.Add(report);

        source[0] = participant with { DivisionName = "Replaced", Final = snapshot with { Manpower = 0 } };
        source.Clear();

        Assert.Same(participant, Assert.Single(report.Participants));
        Assert.Same(report, Assert.Single(history.Reports));
        Assert.Equal(1000, report.Participants[0].Final.Manpower);
    }

    [Fact]
    public void DamagedTankChangeIsNetStockChangeNotGrossDamage()
    {
        var scenario = CreateProvinceScenario();
        ApplyLosses(scenario.French, damaged: 4);
        var engagement = BeginProvinceBattle(scenario);
        ApplyLosses(scenario.French, damaged: 3);
        Assert.Equal(6, scenario.French.Composition.Tanks.Repair(6));
        BreakAll(engagement);
        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 12);

        var report = Assert.Single(scenario.Simulation.Engagements.History.Reports);
        var participant = FindParticipant(report, scenario.French);
        Assert.Equal(4, participant.Initial.DamagedTanks);
        Assert.Equal(1, participant.Final.DamagedTanks);
        Assert.Equal(-3, participant.DamagedTanksChange);
        Assert.Equal(3, participant.OperationalTanksChange);
        Assert.Equal(0, participant.TanksDestroyed);
    }

    [Fact]
    public void EndedEngagementCannotBeArchivedTwiceOrHaveItsOutcomeChanged()
    {
        var scenario = CreateProvinceScenario();
        var engagement = BeginProvinceBattle(scenario);
        scenario.German.LoseOrganization(scenario.German.Organization);
        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 12);
        var report = Assert.Single(scenario.Simulation.Engagements.History.Reports);

        Assert.False(engagement.End(scenario.German.Country, currentHour: 99));
        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 13);
        Assert.Same(report, Assert.Single(scenario.Simulation.Engagements.History.Reports));
        Assert.Equal(12L, engagement.EndHour);
        Assert.Equal(scenario.French.Country, engagement.WinnerCountry);
        Assert.Throws<InvalidOperationException>(() => scenario.Simulation.Engagements.History.Add(report));
        Assert.Single(scenario.Simulation.Engagements.History.Reports);
    }

    [Fact]
    public void ParticipantCannotJoinEndedEngagement()
    {
        var scenario = CreateProvinceScenario();
        var engagement = BeginProvinceBattle(scenario);
        BreakAll(engagement);
        scenario.Simulation.Engagements.ProcessEngagements(currentHour: 12);
        var reinforcement = CreateDivision("Too late", scenario.Province, scenario.French.Country!);

        Assert.Throws<InvalidOperationException>(() =>
            engagement.AddParticipant(reinforcement, currentHour: 13));
        Assert.False(reinforcement.IsEngaged);
        Assert.Equal(2, engagement.Participants.Count);
    }

    private static Division CreateDivision(string name, Province province, Country country)
    {
        return new Division(
            name, province, fuel: 80, ammunition: 100, country: country,
            composition: new DivisionComposition(1000, 1000, 10, 20, 10));
    }

    private static ProvinceScenario CreateProvinceScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield", TerrainType.Forest, owner: germany);
        var french = CreateDivision("French division", province, france);
        var german = CreateDivision("German division", province, germany);
        var simulation = new SimulationEngine();
        simulation.AddDivision(french);
        simulation.AddDivision(german);
        simulation.DeclareWar(france, germany);
        return new ProvinceScenario(simulation, province, french, german);
    }

    private static Engagement BeginProvinceBattle(ProvinceScenario scenario, long currentHour = 10)
    {
        scenario.Simulation.Engagements.DetectProvinceEngagements(
            new[] { scenario.French, scenario.German }, currentHour,
            arrivals: new[] { scenario.French });
        return Assert.Single(scenario.Simulation.Engagements.ActiveEngagements);
    }

    private static TransitScenario CreateTransitScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var a = new Province(1, "A", TerrainType.Plains, owner: france);
        var b = new Province(2, "B", TerrainType.Forest, owner: germany);
        a.ConnectTo(b);
        var french = CreateDivision("French division", a, france);
        var german = CreateDivision("German division", b, germany);
        var plan = new MovementPlan(DurationHours: 4, FuelPerHour: 0);
        Assert.True(french.TryStartMovement(b, plan));
        Assert.True(german.TryStartMovement(a, plan));
        var simulation = new SimulationEngine();
        simulation.AddDivision(french);
        simulation.AddDivision(german);
        simulation.DeclareWar(france, germany);
        return new TransitScenario(simulation, a, b, french, german);
    }

    private static void ApplyLosses(
        Division division, int killed = 0, int wounded = 0, int missing = 0,
        int damaged = 0, int destroyed = 0)
    {
        division.Composition.Manpower.ApplyCasualties(new PersonnelCasualtyReport(killed, wounded, missing));
        division.Composition.Tanks.ApplyExpectedLosses(new TankLossExpectation(damaged, destroyed));
    }

    private static void BreakAll(Engagement engagement)
    {
        foreach (var division in engagement.Participants)
            division.LoseOrganization(division.Organization);
    }

    private static ParticipantBattleReport FindParticipant(BattleReport report, Division division)
    {
        return Assert.Single(report.Participants,
            participant => participant.DivisionName == division.Name);
    }

    private sealed record ProvinceScenario(
        SimulationEngine Simulation, Province Province, Division French, Division German);

    private sealed record TransitScenario(
        SimulationEngine Simulation, Province A, Province B, Division French, Division German);
}
