using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class EngagementBattleConstructionTests
{
    private static EngagementBattlePreparation Prepare(EngagementBattleTestScenario s)
    {
        Assert.True(EngagementBattleBuilder.TryBuild(s.Field, s.Battle, out var ready, out _, s.Diplomacy));
        return ready;
    }

    [Theory]
    [InlineData(100, 25, 75)]
    [InlineData(8, 2, 6)]
    [InlineData(7, 1, 6)]
    [InlineData(5, 1, 4)]
    [InlineData(3, 1, 2)]
    [InlineData(2, 1, 1)]
    [InlineData(1, 1, 0)]
    public void InitialExposureUsesCurrentManpowerIncludingReserves(int remaining, int exposed, int reserve)
    {
        // Les pertes avant la préparation distinguent l'effectif courant de l'effectif autorisé.
        var s = new EngagementBattleTestScenario(menPerCompany: 104);
        foreach (var company in s.First.Battalions[0].Companies)
            company.Composition!.Manpower.ApplyCasualties(new PersonnelCasualtyReport(104 - remaining, 0, 0));

        var built = EngagementBattleBuilder.BuildPrepared(Prepare(s));
        var team = built.Battle.Deployments.Where(d => d.Company.ParentBattalion == s.First.Battalions[0]).ToArray();
        Assert.Equal(4, team.Length);
        Assert.Contains(team, d => d.Role == CompanyDeploymentRole.Reserve);
        foreach (var deployment in team)
        {
            var state = built.Battle.PersonnelExposure.Get(deployment);
            Assert.Equal((exposed, 0, reserve), (state.Exposed, state.Sheltered, state.Reserve));
        }
    }

    [Fact]
    public void ZeroManCompanyIsNotDeployed()
    {
        var s = new EngagementBattleTestScenario();
        var empty = s.First.Battalions[0].Companies[0];
        EngagementBattleTestScenario.Empty(empty);
        var built = EngagementBattleBuilder.BuildPrepared(Prepare(s));
        Assert.Equal(7, built.Battle.Deployments.Count);
        Assert.DoesNotContain(built.Battle.Deployments, d => d.Company == empty);
    }

    [Fact]
    public void ExposureDepthEqualsPreparedFrontage()
    {
        // Assez de compagnies sur le même secteur pour réduire effectivement un front.
        var s = new EngagementBattleTestScenario(companiesPerBattalion: 24);
        var ready = Prepare(s);
        Assert.Contains(ready.Companies, p => p.FrontageMeters < CompanyPlacementOptions.DefaultCompanyFrontageMeters);
        var built = EngagementBattleBuilder.BuildPrepared(ready);
        foreach (var (p, d) in ready.Companies.Zip(built.Battle.Deployments))
            Assert.Equal(p.FrontageMeters, built.Battle.PersonnelExposure.Get(d).ExposedDepthMeters);
    }

    [Fact]
    public void EveryDeploymentHasStationaryUnconcealedSignature()
    {
        var s = new EngagementBattleTestScenario();
        var built = EngagementBattleBuilder.BuildPrepared(Prepare(s));
        Assert.All(built.Battle.Deployments, d =>
        {
            Assert.True(built.Signatures.TryGet(d, out var signature));
            Assert.Equal(new TacticalUnitSignature(TacticalActivity.Stationary, TacticalConcealment.None), signature);
        });
    }

    [Fact]
    public void ConstructionLeavesEngagementInLegacyAndPreservesPhysicalState()
    {
        var s = new EngagementBattleTestScenario();
        var ready = Prepare(s);
        var participants = s.Battle.Participants.ToArray();
        var totals = participants.Select(d => d.GetCurrentResourceTotals()).ToArray();
        var logistics = participants.Select(d => (d.Organization, d.Fuel, d.Ammunition)).ToArray();
        var resources = ready.Companies.Select(p =>
            (p.Company, p.Company.Composition, Totals: FormationResourceTotals.Capture(p.Company.Composition!))).ToArray();
        var roles = participants.Select(d => (s.Battle.GetRole(d), s.Battle.GetEntryProvince(d))).ToArray();
        int cached = s.Cache.Count;
        var result = EngagementBattleBuilder.BuildPrepared(ready);

        Assert.Equal(EngagementCombatMode.LegacyOperational, s.System.GetCombatMode(s.Battle));
        Assert.True(s.Battle.IsActive);
        Assert.Same(s.Battle, result.Battle.Engagement);
        Assert.Equal(participants, s.Battle.Participants.ToArray());
        Assert.All(participants, d => Assert.Same(s.Battle, d.CurrentEngagement));
        Assert.Equal(roles, participants.Select(d => (s.Battle.GetRole(d), s.Battle.GetEntryProvince(d))).ToArray());
        Assert.Equal(logistics, participants.Select(d => (d.Organization, d.Fuel, d.Ammunition)).ToArray());
        Assert.Equal(totals, participants.Select(d => d.GetCurrentResourceTotals()).ToArray());
        foreach (var (company, composition, companyTotals) in resources)
        {
            Assert.Same(composition, company.Composition);
            Assert.Equal(companyTotals, FormationResourceTotals.Capture(company.Composition!));
        }
        Assert.Equal(cached, s.Cache.Count);
        Assert.Same(s.Field, s.Cache.GetOrGenerate(s.Battle));
        Assert.Same(s.Field.Grid, result.Layout.Grid);
        Assert.Same(s.Field.Partition, result.Layout.Partition);
    }

    [Fact]
    public void SameCompanyIdInTwoDivisionsCreatesTwoDistinctDeployments()
    {
        var s = new EngagementBattleTestScenario();
        var first = s.First.Battalions[0].Companies[0];
        var second = s.Second.Battalions[0].Companies[0];
        Assert.Equal(first.Id, second.Id);
        var built = EngagementBattleBuilder.BuildPrepared(Prepare(s));
        var a = Assert.Single(built.Battle.Deployments, d => d.Company == first);
        var b = Assert.Single(built.Battle.Deployments, d => d.Company == second);
        Assert.NotSame(a, b);
        Assert.Same(s.First, a.Company.ParentBattalion!.ParentDivision);
        Assert.Same(s.Second, b.Company.ParentBattalion!.ParentDivision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvinceAndMeetingUsePreparedPlacements(bool meeting)
    {
        var s = new EngagementBattleTestScenario(meeting);
        Assert.Equal(meeting ? EngagementType.MeetingEngagement : EngagementType.ProvinceBattle, s.Battle.Type);
        var ready = Prepare(s);
        var built = EngagementBattleBuilder.BuildPrepared(ready);
        Assert.Equal(ready.BattlefieldSectorIds, built.Layout.RequestedGrandSectorIds);
        Assert.Same(built.Layout.Battlefield, built.Battle.Battlefield);
        Assert.Equal(ready.Companies.Count, built.Battle.Deployments.Count);
        Assert.True(EngagementDeploymentZones.TryCompute(s.Field, s.Battle, out var zones, out _, diplomacy: s.Diplomacy));

        foreach (var (p, d) in ready.Companies.Zip(built.Battle.Deployments))
        {
            Assert.Same(p.Company, d.Company);
            Assert.Same(p.Battalion, d.Company.ParentBattalion);
            Assert.Equal(p.Role, d.Role);
            Assert.Same(built.Layout.GetSector(p.Cell), d.Sector);
            var actual = Assert.IsType<CompanyTacticalPosition>(built.Map.GetPosition(d));
            Assert.Equal((p.Point, p.FacingDegrees, p.FrontageMeters), (actual.Point, actual.FacingDegrees, actual.FrontageMeters));
            if (d.Role == CompanyDeploymentRole.Reserve)
            {
                var rear = zones!.EnteringDivisions.Contains(p.Battalion.ParentDivision!)
                    ? zones.EnteringRearSectorIds : zones.FacingRearSectorIds;
                Assert.Contains(s.Field.Partition.GetSectorOfCell(built.Layout.GetCellIndex(d.Sector)).Id, rear);
            }
        }
    }

    [Fact]
    public void SecondBuildProducesIdenticalPlacementsInAnIndependentBattle()
    {
        var ready = Prepare(new EngagementBattleTestScenario());
        var first = EngagementBattleBuilder.BuildPrepared(ready);
        var second = EngagementBattleBuilder.BuildPrepared(ready);
        Assert.NotSame(first.Battle, second.Battle);
        Assert.NotSame(first.Battle.Battlefield, second.Battle.Battlefield);
        Assert.Same(first.Layout.Grid, second.Layout.Grid);
        Assert.Equal(first.Layout.CellIndices, second.Layout.CellIndices);
        Assert.Equal(ready.Companies.Count, first.Battle.Deployments.Count);
        Assert.Equal(ready.Companies.Count, second.Battle.Deployments.Count);
        foreach (var (a, b) in first.Battle.Deployments.Zip(second.Battle.Deployments))
        {
            Assert.Same(a.Company, b.Company);
            Assert.NotSame(a.Sector, b.Sector);
            Assert.False(second.Battle.ContainsDeployment(a));
            Assert.Equal((first.Layout.GetCellIndex(a.Sector), a.Role), (second.Layout.GetCellIndex(b.Sector), b.Role));
            var pa = Assert.IsType<CompanyTacticalPosition>(first.Map.GetPosition(a));
            var pb = Assert.IsType<CompanyTacticalPosition>(second.Map.GetPosition(b));
            Assert.Equal((pa.Point, pa.FacingDegrees, pa.FrontageMeters), (pb.Point, pb.FacingDegrees, pb.FrontageMeters));
            Assert.Equal(first.Battle.PersonnelExposure.Get(a), second.Battle.PersonnelExposure.Get(b));
        }
        first.Signatures.Set(first.Battle.Deployments[0], new(TacticalActivity.Moving, TacticalConcealment.Strong));
        Assert.True(second.Signatures.TryGet(second.Battle.Deployments[0], out var unchanged));
        Assert.Equal(new TacticalUnitSignature(TacticalActivity.Stationary, TacticalConcealment.None), unchanged);
    }

    private static void AssertStale(EngagementBattlePreparation preparation)
    {
        var error = Assert.Throws<InvalidOperationException>(() => EngagementBattleBuilder.BuildPrepared(preparation));
        Assert.Contains("Prepare again", error.Message);
    }

    [Fact]
    public void EndedEngagementIsRejected()
    {
        var s = new EngagementBattleTestScenario();
        var ready = Prepare(s);
        s.Battle.End(null, 2);
        AssertStale(ready);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedParticipantsAreRejected(bool reinforcement)
    {
        var s = new EngagementBattleTestScenario();
        var ready = Prepare(s);
        if (reinforcement)
            s.Battle.AddParticipant(EngagementBattleTestScenario.Formation(s.Province, s.Blue), 2);
        else
            s.Second.LeaveEngagement(s.Battle);
        AssertStale(ready);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedCompanyStrengthIsRejected(bool previouslyEmpty)
    {
        var s = new EngagementBattleTestScenario(menPerCompany: 7);
        var company = s.Second.Battalions[0].Companies[0];
        if (previouslyEmpty)
            EngagementBattleTestScenario.Empty(company);
        var ready = Prepare(s);
        var pool = company.Composition!.Manpower;
        if (previouslyEmpty)
            Assert.Equal(1, pool.Reinforce(1));
        else
            pool.ApplyCasualties(new PersonnelCasualtyReport(1, 0, 0));
        AssertStale(ready);
    }
}