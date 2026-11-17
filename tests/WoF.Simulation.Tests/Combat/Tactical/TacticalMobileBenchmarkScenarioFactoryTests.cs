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



    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContactSweepPreservesResultsWhenSegmentsAreReordered(
        bool dense)
    {
        var scenario =
            TacticalMobileBenchmarkScenarioFactory.CreateDetector(
                new(
                    TotalCompanies: 52,
                    MobilityPercent: 100,
                    Density: Density(dense),
                    Seed: 12345));

        long hour = scenario.Battle.Engagement.StartHour;

        var original = TacticalSimultaneousContactDetector.Detect(
            scenario.Battle,
            scenario.Diplomacy,
            scenario.Trajectories,
            hour,
            scenario.ContactRadiusMeters,
            includeFirstContacts: true);

        var reversed = TacticalSimultaneousContactDetector.Detect(
            scenario.Battle,
            scenario.Diplomacy,
            scenario.Trajectories.Reverse().ToArray(),
            hour,
            scenario.ContactRadiusMeters,
            includeFirstContacts: true);

        Assert.Equal(
            original.Statistics,
            reversed.Statistics);

        Assert.Equal(
            original.Encounters,
            reversed.Encounters);

        Assert.Equal(
            original.FirstContacts,
            reversed.FirstContacts);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetectorUsesVelocityOfEachTimedSegment(
        bool dense)
    {
        var scenario =
            TacticalMobileBenchmarkScenarioFactory.CreateDetector(
                new(
                    TotalCompanies: 26,
                    MobilityPercent: 100,
                    Density: Density(dense),
                    Seed: 19));

        var first = scenario.Battle.Deployments[0];

        var firstCountry =
            first.Company.ParentBattalion!
                .ParentDivision!.Country;

        var second = scenario.Battle.Deployments.First(
            d => !ReferenceEquals(
                d.Company.ParentBattalion!
                    .ParentDivision!.Country,
                firstCountry));

        // Premiere compagnie :
        // immobile jusqu'a 0.25,
        // puis mouvement de X=0 vers X=10.
        //
        // Deuxieme compagnie :
        // mouvement inverse entre 0.25 et 0.75.
        TacticalTimedTrajectorySegment[] trajectories =
        [
            new(
                first,
                new TacticalPoint(0, 0, 100),
                new TacticalPoint(0, 0, 100),
                0,
                0.25,
                IsMoving: false),

            new(
                first,
                new TacticalPoint(0, 0, 100),
                new TacticalPoint(10, 0, 100),
                0.25,
                0.75,
                IsMoving: true),

            new(
                second,
                new TacticalPoint(10, 0, 100),
                new TacticalPoint(0, 0, 100),
                0.25,
                0.75,
                IsMoving: true)
        ];

        var result = TacticalSimultaneousContactDetector.Detect(
            scenario.Battle,
            scenario.Diplomacy,
            trajectories,
            scenario.Battle.Engagement.StartHour,
            contactRadiusMeters: 0.5,
            includeFirstContacts: true);

        var encounter = Assert.Single(result.Encounters);

        // Rapprochement maximal à t = 0.5.
        Assert.Equal(
            0.5,
            encounter.TimeFraction,
            precision: 10);

        Assert.Equal(
            new TacticalPoint(5, 0, 100),
            encounter.EstimatedPosition);

        var contact = Assert.Single(result.FirstContacts);

        // Distance initiale : 10 m.
        // Vitesse relative : 40 m par heure.
        // Premier contact à 0.5 m :
        // 0.25 + (10 - 0.5) / 40 = 0.4875.
        Assert.Equal(
            0.4875,
            contact.TimeFraction,
            precision: 10);

        Assert.Equal(
            0.5,
            contact.FirstPosition.HorizontalDistanceTo(
                contact.SecondPosition),
            precision: 8);
    }




    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimultaneousContactsAreOrderedByCompanyIndices(
        bool includeFirstContacts)
    {
        var scenario =
            TacticalMobileBenchmarkScenarioFactory.CreateDetector(
                new(
                    TotalCompanies: 26,
                    MobilityPercent: 100,
                    Density: Density(false),
                    Seed: 19));

        int countryId = scenario.Battle.Deployments[0]
            .Company.ParentBattalion!
            .ParentDivision!.Country!.Id;

        var firstSide = scenario.Battle.Deployments
            .Where(d => d.Company.ParentBattalion!
                .ParentDivision!.Country!.Id == countryId)
            .Take(2)
            .ToArray();

        var secondSide = scenario.Battle.Deployments
            .Where(d => d.Company.ParentBattalion!
                .ParentDivision!.Country!.Id != countryId)
            .Take(2)
            .ToArray();

        Assert.Equal(2, firstSide.Length);
        Assert.Equal(2, secondSide.Length);

        var indices = scenario.Battle.Deployments
            .Select((company, index) => (company, index))
            .ToDictionary(
                x => x.company,
                x => x.index,
                (IEqualityComparer<CompanyDeployment>)ReferenceEqualityComparer.Instance);

        (int First, int Second) Key(
            CompanyDeployment a,
            CompanyDeployment b)
        {
            int x = indices[a];
            int y = indices[b];

            return (Math.Min(x, y), Math.Max(x, y));
        }

        // Les quatre compagnies parcourent exactement
        // la meme trajectoire, au meme moment.
        // Les quatre rencontres ennemies commencent
        // donc toutes a t = 0.
        var start = new TacticalPoint(0, 0, 100);
        var end = new TacticalPoint(10, 0, 100);

        var trajectories = firstSide
            .Concat(secondSide)
            .Reverse()
            .Select(d => new TacticalTimedTrajectorySegment(
                d, start, end, 0, 1, IsMoving: true))
            .ToArray();

        var result = TacticalSimultaneousContactDetector.Detect(
            scenario.Battle,
            scenario.Diplomacy,
            trajectories,
            scenario.Battle.Engagement.StartHour,
            contactRadiusMeters: 1,
            includeFirstContacts: includeFirstContacts);

        var expected = firstSide
            .SelectMany(a => secondSide.Select(b => Key(a, b)))
            .OrderBy(pair => pair.First)
            .ThenBy(pair => pair.Second)
            .ToArray();

        Assert.Equal(4, result.Encounters.Count);

        Assert.Equal(
            expected,
            result.Encounters
                .Select(e => Key(e.First, e.Second))
                .ToArray());

        Assert.All(
            result.Encounters,
            e => Assert.Equal(0.0, e.TimeFraction));

        if (includeFirstContacts)
        {
            Assert.Equal(4, result.FirstContacts.Count);

            Assert.Equal(
                expected,
                result.FirstContacts
                    .Select(e => Key(e.First, e.Second))
                    .ToArray());

            Assert.All(
                result.FirstContacts,
                e => Assert.Equal(0.0, e.TimeFraction));
        }
        else
        {
            Assert.Empty(result.FirstContacts);
        }
    }



    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DisablingPredictionsPreservesFirstContactsAndChecks(
        bool dense,
        bool includeFirstContacts)
    {
        var scenario =
            TacticalMobileBenchmarkScenarioFactory.CreateDetector(
                new(
                    TotalCompanies: 52,
                    MobilityPercent: 100,
                    Density: Density(dense),
                    Seed: 12345));

        long hour = scenario.Battle.Engagement.StartHour;

        var full = TacticalSimultaneousContactDetector.Detect(
            scenario.Battle,
            scenario.Diplomacy,
            scenario.Trajectories,
            hour,
            scenario.ContactRadiusMeters,
            includeFirstContacts: includeFirstContacts);

        var withoutPredictions =
            TacticalSimultaneousContactDetector.Detect(
                scenario.Battle,
                scenario.Diplomacy,
                scenario.Trajectories,
                hour,
                scenario.ContactRadiusMeters,
                includeFirstContacts: includeFirstContacts,
                includePredictedEncounters: false);

        Assert.Empty(withoutPredictions.Encounters);

        Assert.Equal(
            full.FirstContacts,
            withoutPredictions.FirstContacts);

        Assert.Equal(
            full.Statistics.SegmentCount,
            withoutPredictions.Statistics.SegmentCount);

        Assert.Equal(
            full.Statistics.BroadPhaseCandidates,
            withoutPredictions.Statistics.BroadPhaseCandidates);

        Assert.Equal(
            full.Statistics.ExactPairChecks,
            withoutPredictions.Statistics.ExactPairChecks);

        Assert.Equal(
            0,
            withoutPredictions.Statistics.Encounters);
    }



    [Theory]
    [InlineData(false, 1372L, 626L, 437, 374)]
    [InlineData(true, 113406L, 39438L, 27531, 23562)]
    public void StationarySecondPassSkipsRedundantDetection(
        bool dense,
        long expectedCandidates,
        long expectedExactChecks,
        int expectedPredictions,
        int expectedConfirmedContacts)
    {
        var scenario =
            TacticalMobileBenchmarkScenarioFactory.CreateTick(
                new(
                    TotalCompanies: 500,
                    MobilityPercent: 60,
                    Density: Density(dense),
                    Seed: 12345));

        scenario.Engine.Tick();

        var resolution = scenario.Orchestrator
            .LastMovementPhase!
            .SimultaneousResolution!;

        // La seconde passe est atteinte,
        // mais la detection inutile est evitee.
        Assert.Equal(2, resolution.ResolutionPasses);

        // Les candidats correspondent uniquement
        // a la premiere passe.
        Assert.Equal(
            expectedCandidates,
            resolution.BroadPhaseCandidates);

        Assert.Equal(
            expectedExactChecks,
            resolution.ExactPairChecks);

        Assert.Equal(
            expectedPredictions,
            resolution.PredictedEncounters.Count);

        Assert.Equal(
            expectedConfirmedContacts,
            scenario.Orchestrator
                .LastMobileCombatSummary!.Value
                .ConfirmedContacts);
    }



    [Fact]
    public void AllStationarySegmentsProduceNoMobileContacts()
    {
        var scenario =
            TacticalMobileBenchmarkScenarioFactory.CreateDetector(
                new(
                    TotalCompanies: 26,
                    MobilityPercent: 0,
                    Density: Density(true),
                    Seed: 19));

        var result = TacticalSimultaneousContactDetector.Detect(
            scenario.Battle,
            scenario.Diplomacy,
            scenario.Trajectories,
            scenario.Battle.Engagement.StartHour,
            scenario.ContactRadiusMeters,
            includeFirstContacts: true);

        Assert.Equal(0L, result.Statistics.ExactPairChecks);
        Assert.Empty(result.Encounters);
        Assert.Empty(result.FirstContacts);
    }



    [Theory]
    [InlineData(false, 0.45)]
    [InlineData(true, 0.0)]
    public void MobileStationaryContactWorksInBothSweepOrders(
        bool stationaryFirst,
        double expectedContactTime)
    {
        var scenario =
            TacticalMobileBenchmarkScenarioFactory.CreateDetector(
                new(
                    TotalCompanies: 26,
                    MobilityPercent: 0,
                    Density: Density(false),
                    Seed: 19));

        var first = scenario.Battle.Deployments[0];

        int countryId = first.Company.ParentBattalion!
            .ParentDivision!.Country!.Id;

        var second = scenario.Battle.Deployments.First(
            d => d.Company.ParentBattalion!
                .ParentDivision!.Country!.Id != countryId);

        var stationaryPoint = new TacticalPoint(0, 0, 100);

        var from = stationaryFirst
            ? new TacticalPoint(0.5, 0, 100)
            : new TacticalPoint(-10, 0, 100);

        var to = stationaryFirst
            ? new TacticalPoint(10.5, 0, 100)
            : new TacticalPoint(10, 0, 100);

        TacticalTimedTrajectorySegment[] segments =
        [
            new(
                first, from, to,
                0, 1, IsMoving: true),

            new(
                second,
                stationaryPoint,
                stationaryPoint,
                0, 1, IsMoving: false)
        ];

        var result = TacticalSimultaneousContactDetector.Detect(
            scenario.Battle,
            scenario.Diplomacy,
            segments,
            scenario.Battle.Engagement.StartHour,
            contactRadiusMeters: 1,
            includeFirstContacts: true);

        Assert.Single(result.Encounters);

        var contact = Assert.Single(result.FirstContacts);

        Assert.Equal(
            expectedContactTime,
            contact.TimeFraction,
            precision: 8);

        Assert.Equal(1L, result.Statistics.ExactPairChecks);
    }






}

