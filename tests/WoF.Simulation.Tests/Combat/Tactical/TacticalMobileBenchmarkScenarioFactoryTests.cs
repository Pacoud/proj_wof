using WoF.Benchmarks;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalMobileBenchmarkScenarioFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetectorBuildsRealDeploymentsAndAllThreeTrajectoryPatterns(bool dense)
    {
        var scenario = TacticalMobileBenchmarkScenarioFactory.CreateDetector(
            new(26, 100, Density(dense), Seed: 19));

        Assert.Equal(26, scenario.TotalCompanies);
        Assert.Equal(26, scenario.MobileCompanies);
        Assert.Equal(0, scenario.ActiveMovements);
        Assert.True(scenario.TrajectoryCount > scenario.TotalCompanies);
        AssertValidBattle(scenario.Battle, scenario.Geometry);

        var lanes = scenario.Geometry.SectorGeometries.Select(g => g.CenterYMeters)
            .Distinct().Order().ToArray();
        Assert.True(lanes.Length >= 3);
        Assert.Equal(dense, lanes[1] - lanes[0] < scenario.ContactRadiusMeters);

        var firstThree = lanes.Take(3).Select(lane =>
        {
            // Les deux premières compagnies opposées de cette rangée suffisent.
            var pair = scenario.Battle.Deployments
                .Where(d => scenario.Geometry.GetSectorGeometry(d.Sector)!.CenterYMeters == lane)
                .DistinctBy(d => d.Company.ParentBattalion!.ParentDivision!.Country)
                .ToArray();
            Assert.Equal(2, pair.Length);
            return TacticalSimultaneousContactDetector.Detect(
                scenario.Battle, scenario.Diplomacy,
                scenario.Trajectories.Where(s => pair.Contains(s.Company)).ToArray(),
                scenario.Battle.Engagement.StartHour, scenario.ContactRadiusMeters,
                includeFirstContacts: true);
        }).ToArray();

        Assert.Single(firstThree[0].FirstContacts);
        Assert.Empty(firstThree[1].FirstContacts);
        Assert.Empty(firstThree[2].FirstContacts);
        Assert.Null(scenario.Battle.DirectFire.LastPhase);
    }

    [Fact]
    public void DetectorRespectsRequestedMobilityAndSeed()
    {
        TacticalMobileBenchmarkOptions options = new(101, 63, Density(false), Seed: 19);
        var first = TacticalMobileBenchmarkScenarioFactory.CreateDetector(options);
        var second = TacticalMobileBenchmarkScenarioFactory.CreateDetector(options);
        var different = TacticalMobileBenchmarkScenarioFactory.CreateDetector(options with { Seed = 20 });

        Assert.Equal(63, first.MobileCompanies);
        Assert.Equal(63, first.Trajectories.Where(segment => segment.IsMoving)
            .Select(segment => segment.Company).Distinct().Count());
        Assert.Equal(101, first.Trajectories.Select(segment => segment.Company).Distinct().Count());
        Assert.Equal(CaptureTrajectories(first), CaptureTrajectories(second));
        Assert.False(CaptureTrajectories(first).SequenceEqual(CaptureTrajectories(different)));
        AssertValidBattle(first.Battle, first.Geometry);
    }

    [Theory]
    [InlineData(2, 100, false, true, 0)]
    [InlineData(3, 100, false, true, 1)]
    [InlineData(100, 0, false, true, 0)]
    [InlineData(100, 10, false, true, 10)]
    [InlineData(100, 60, true, true, 26)]
    [InlineData(100, 60, false, false, 26)]
    [InlineData(1000, 60, false, true, 250)]
    public void TickUsesApprovedOrdersAndRuns(
        int companies, double requested, bool dense, bool enableMobileCombat, int expectedMovers)
    {
        var scenario = TacticalMobileBenchmarkScenarioFactory.CreateTick(
            new(companies, requested, Density(dense), Seed: 19, enableMobileCombat));

        Assert.Equal(companies, scenario.TotalCompanies);
        Assert.Equal(expectedMovers, scenario.ActiveMovements);
        Assert.Equal(expectedMovers, scenario.MobileCompanies);
        Assert.Equal(100.0 * expectedMovers / companies, scenario.ActualMobilityPercent);
        Assert.Equal(EngagementCombatMode.Tactical,
            scenario.Engine.Engagements.GetCombatMode(scenario.Battle.Engagement));
        AssertValidBattle(scenario.Battle, scenario.Geometry);

        var movements = scenario.Orchestrator.ReactionExecution!.PendingMovements;
        Assert.All(movements, order =>
        {
            Assert.True(order.Company.IsInTransit);
            Assert.Equal(3, order.Route.Count);
            Assert.Same(order.Origin, order.Route[0]);
            Assert.Same(order.Destination, order.Route[^1]);
            Assert.Equal(TacticalBattalionReactionType.Reinforce, order.Reason);
        });

        long measuredHour = scenario.Engine.Clock.CurrentHour + 1;
        scenario.Engine.Tick();

        Assert.Equal(measuredHour, scenario.Engine.Clock.CurrentHour);
        Assert.Equal(measuredHour, scenario.Orchestrator.LastResult!.Value.Hour);
        Assert.Equal(measuredHour, scenario.Battle.DirectFire.LastCasualtyPhase!.Hour);
        Assert.Equal(measuredHour, scenario.Battle.DirectFire.LastSuppressionPhase!.Hour);
        Assert.Equal(measuredHour, scenario.Orchestrator.LastBattalionReactionPhase!.Hour);
        AssertValidBattle(scenario.Battle, scenario.Geometry, initialPositions: false);
        if (enableMobileCombat)
        {
            var summary = scenario.Orchestrator.LastMobileCombatSummary!.Value;
            Assert.Equal(measuredHour, summary.Hour);
            if (expectedMovers >= 2)
            {
                Assert.True(summary.ConfirmedContacts > 0);
                Assert.True(summary.MobileSalvosFired > 0);
            }
        }
        else
        {
            Assert.Null(scenario.Orchestrator.LastMobileCombatSummary);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TickReproducesPositionsOrdersAndFirstMeasuredResult(bool dense)
    {
        TacticalMobileBenchmarkOptions options = new(101, 60, Density(dense), Seed: 19);
        var first = TacticalMobileBenchmarkScenarioFactory.CreateTick(options);
        var second = TacticalMobileBenchmarkScenarioFactory.CreateTick(options);
        Assert.Equal(CaptureDeployments(first.Battle, first.Geometry),
            CaptureDeployments(second.Battle, second.Geometry));
        Assert.Equal(CaptureOrders(first), CaptureOrders(second));

        first.Engine.Tick();
        second.Engine.Tick();

        Assert.Equal(first.Orchestrator.LastResult, second.Orchestrator.LastResult);
        Assert.Equal(first.Orchestrator.LastMobileCombatSummary, second.Orchestrator.LastMobileCombatSummary);
        Assert.Equal(CaptureDeployments(first.Battle, first.Geometry),
            CaptureDeployments(second.Battle, second.Geometry));
    }

    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, -1)]
    [InlineData(2, 101)]
    [InlineData(2, double.NaN)]
    public void InvalidOptionsAreRejected(int companies, double mobility)
    {
        TacticalMobileBenchmarkOptions options = new(companies, mobility, Density(false), Seed: 19);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TacticalMobileBenchmarkScenarioFactory.CreateDetector(options));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TacticalMobileBenchmarkScenarioFactory.CreateTick(options));
    }

    private static TacticalMobileBenchmarkDensity Density(bool dense) =>
        dense ? TacticalMobileBenchmarkDensity.Dense : TacticalMobileBenchmarkDensity.Sparse;

    private static void AssertValidBattle(
        TacticalBattle battle, TacticalGeometryMap geometry, bool initialPositions = true)
    {
        Assert.Equal(2, battle.Engagement.Participants.Select(d => d.Country).Distinct().Count());
        var deployments = battle.Deployments.ToDictionary(d => d.Company);
        Assert.Equal(battle.Deployments.Count, geometry.CompanyPositions.Count);
        foreach (var division in battle.Engagement.Participants)
        {
            Assert.InRange(division.Battalions.Count, 1, 12);
            var totals = division.GetCurrentResourceTotals();
            Assert.Equal(division.Battalions.Sum(b => b.GetResourceTotals().Personnel.Available),
                totals.Personnel.Available);
            Assert.Equal(division.Battalions.Sum(b => b.GetResourceTotals().Ammunition.SmallArms),
                totals.Ammunition.SmallArms);

            foreach (var battalion in division.Battalions)
            {
                Assert.Same(division, battalion.ParentDivision);
                Assert.InRange(battalion.Companies.Count, 1, 4);
                foreach (var company in battalion.Companies)
                {
                    Assert.Same(battalion, company.ParentBattalion);
                    var deployment = deployments[company];
                    Assert.True(battle.ContainsDeployment(deployment));
                    var sector = geometry.GetSectorGeometry(deployment.Sector);
                    var position = geometry.GetPosition(deployment);
                    Assert.NotNull(sector);
                    Assert.NotNull(position);
                    if (initialPositions || !deployment.IsInTransit)
                        Assert.True(sector.Contains(position.Point));

                    var composition = company.Composition!;
                    Assert.InRange(composition.Manpower.Current, 0, composition.Manpower.Authorized);
                    Assert.InRange(composition.Ammunition.GetAvailable(AmmunitionType.SmallArms), 0, 1000);
                    Assert.Equal(composition.Manpower.Current, battle.PersonnelExposure.Get(deployment).Total);
                }
            }
        }

        Assert.Equal(battle.Battlefield.Sectors.Count, geometry.SectorGeometries.Count);
        Assert.Equal(battle.Battlefield.Sectors.Count - 1, battle.Battlefield.Connections.Count);
    }

    private static (int Index, TacticalPoint From, TacticalPoint To, double Start, double End, bool Moving)[]
        CaptureTrajectories(TacticalMobileDetectorScenario scenario)
    {
        var indices = scenario.Battle.Deployments.Select((d, i) => (d, i)).ToDictionary(p => p.d, p => p.i);
        return scenario.Trajectories.Select(s =>
            (indices[s.Company], s.From, s.To, s.StartTime, s.EndTime, s.IsMoving)).ToArray();
    }

    private static (int Sector, TacticalPoint Position, CompanyDeploymentRole Role, bool Moving,
        int Personnel, long Ammunition)[] CaptureDeployments(TacticalBattle battle, TacticalGeometryMap geometry)
    {
        return battle.Deployments.Select(d =>
            (d.Sector.Id, geometry.GetPosition(d)!.Point, d.Role, d.IsInTransit,
             d.Company.Composition!.Manpower.Current,
             d.Company.Composition!.Ammunition.GetAvailable(AmmunitionType.SmallArms))).ToArray();
    }

    private static (int Company, long Issued, long Arrival, double Distance, string Route)[]
        CaptureOrders(TacticalMobileTickScenario scenario)
    {
        var indices = scenario.Battle.Deployments.Select((d, i) => (d, i)).ToDictionary(p => p.d, p => p.i);
        return scenario.Orchestrator.ReactionExecution!.PendingMovements.Select(o =>
            (indices[o.Company], o.IssuedHour, o.ArrivalHour, o.TotalDistanceMeters,
             string.Join(",", o.Route.Select(s => s.Id)))).ToArray();
    }
}

