
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalCommunicationNetworkTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalPerceptionState Perception,
        TacticalCommunicationNetwork Network,
        TacticalMessageRouter Router,
        TacticalNetworkIntelligenceRelay Relay,
        CompanyDeployment Source,
        CompanyDeployment NeighborCompany,
        CompanyDeployment Enemy,
        Battalion BattalionA,
        Battalion BattalionB,
        Division French,
        Division German,
        TacticalCommunicationNode CompanyNode,
        TacticalCommunicationNode BattalionNode,
        TacticalCommunicationNode DivisionNode,
        TacticalCommunicationNode NeighborNode,
        TacticalCommunicationLink Radio,
        TacticalCommunicationLink Courier);

    private static Division MakeDivision(
        string name,
        Country country,
        Province province,
        int battalions)
    {
        var division = new Division(
            name,
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: battalions * 100,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        for (int i = 1; i <= battalions; i++)
        {
            var battalion = new Battalion(
                i,
                $"Bataillon {i}",
                BattalionType.Infantry);

            battalion.AddCompany(new Company(
                1,
                "Compagnie",
                CompanyType.Infantry,
                new CompanyComposition(manpower: 100)));

            division.AddBattalion(battalion);
        }

        division.EnableHierarchicalResources();
        return division;
    }

    private static Scenario CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battle");

        var french = MakeDivision(
            "France", france, province, 2);

        var german = MakeDivision(
            "Germany", germany, province, 1);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement = Assert.Single(
            engagements.ActiveEngagements);

        var field = new TacticalBattlefield();
        var sector = new BattleSector(
            1, "Village", TerrainType.Urban);

        field.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, field);

        var battalionA = french.Battalions[0];
        var battalionB = french.Battalions[1];

        var source = battle.DeployCompany(
            battalionA.Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var neighbor = battle.DeployCompany(
            battalionB.Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var enemy = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var perception = new TacticalPerceptionState(
            battle);

        var network = new TacticalCommunicationNetwork(
            battle);

        var companyNode = network.ForCompany(source);
        var battalionNode = network.ForBattalion(battalionA);
        var divisionNode = network.ForDivision(french);
        var neighborNode = network.ForBattalion(battalionB);

        var radio = network.AddDirectedLink(
            companyNode,
            battalionNode,
            TacticalCommunicationMedium.Radio,
            delayMinutes: 5);

        var courier = network.AddDirectedLink(
            companyNode,
            battalionNode,
            TacticalCommunicationMedium.Courier,
            delayMinutes: 40);

        network.AddDirectedLink(
            battalionNode,
            divisionNode,
            TacticalCommunicationMedium.Radio,
            delayMinutes: 10);

        network.AddDirectedLink(
            battalionNode,
            neighborNode,
            TacticalCommunicationMedium.Radio,
            delayMinutes: 3);

        var router = new TacticalMessageRouter(network);

        var relay = new TacticalNetworkIntelligenceRelay(
            battle,
            perception,
            network,
            router);

        return new Scenario(
            battle,
            perception,
            network,
            router,
            relay,
            source,
            neighbor,
            enemy,
            battalionA,
            battalionB,
            french,
            german,
            companyNode,
            battalionNode,
            divisionNode,
            neighborNode,
            radio,
            courier);
    }

    private static TacticalEnemyContactReport Observe(
        Scenario s,
        long hour)
    {
        s.Perception.Apply(hour, new[]
        {
            new TacticalObservationEvent(
                s.Source,
                s.Enemy,
                new TacticalPoint(120, 20, 100),
                ObservedSectorId: 1,
                KnowledgeLevel: TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 40)
        });

        return Assert.Single(
            s.Perception.GetReports(s.Source));
    }

    [Fact]
    public void Router_PrefersFastestLinkAndCachesResult()
    {
        var s = CreateScenario();

        var fastest = Assert.IsType<TacticalCommunicationRoute>(
            s.Router.FindRoute(
                s.CompanyNode,
                s.BattalionNode));

        Assert.Equal(5L, fastest.TotalMinutes);
        Assert.Same(s.Radio, Assert.Single(fastest.Hops));

        long searches = s.Router.SearchCount;

        var repeated = s.Router.FindRoute(
            s.CompanyNode,
            s.BattalionNode);

        Assert.Same(fastest, repeated);
        Assert.Equal(searches, s.Router.SearchCount);
        Assert.True(s.Router.CacheHitCount >= 1);

        // Coupure de la radio : recalcul du chemin.
        s.Network.SetOperational(s.Radio, false);

        var alternative = Assert.IsType<TacticalCommunicationRoute>(
            s.Router.FindRoute(
                s.CompanyNode,
                s.BattalionNode));

        Assert.Equal(40L, alternative.TotalMinutes);
        Assert.Same(s.Courier, Assert.Single(alternative.Hops));
        Assert.Equal(searches + 1, s.Router.SearchCount);
    }

    [Fact]
    public void Message_ArrivesAtBattalionBeforeDivision()
    {
        var s = CreateScenario();

        var observed = Observe(s, hour: 1);

        Assert.True(
            s.Relay.Publish(s.Source, observed, currentMinute: 60));

        s.Relay.Step(64);

        Assert.Empty(
            s.Relay.GetBattalionReports(s.BattalionA));

        s.Relay.Step(65);

        var battalion = Assert.Single(
            s.Relay.GetBattalionReports(s.BattalionA));

        Assert.Equal(65L, battalion.ReceivedMinute);
        Assert.Equal(1L, battalion.Contact.LastObservedHour);
        Assert.False(battalion.Contact.IsCurrentlyObserved);

        s.Relay.Step(74);

        Assert.Empty(
            s.Relay.GetDivisionReports(s.French));

        s.Relay.Step(75);

        var division = Assert.Single(
            s.Relay.GetDivisionReports(s.French));

        Assert.Equal(75L, division.ReceivedMinute);
        Assert.Equal(1L, division.Contact.LastObservedHour);
        Assert.Equal(0, s.Relay.PendingMessageCount);
        Assert.Equal(2L, s.Relay.DeliveredHopCount);
    }

    [Fact]
    public void SkippedMinutes_PreserveSimulatedArrivalTimes()
    {
        var s = CreateScenario();

        var report = Observe(s, 1);
        s.Relay.Publish(s.Source, report, 60);

        // Le relais simule quand meme les deux etapes.
        s.Relay.Step(200);

        Assert.Equal(
            65L,
            Assert.Single(
                s.Relay.GetBattalionReports(s.BattalionA))
                .ReceivedMinute);

        Assert.Equal(
            75L,
            Assert.Single(
                s.Relay.GetDivisionReports(s.French))
                .ReceivedMinute);
    }

    [Fact]
    public void DestroyedInFlightLink_PreventsDelivery()
    {
        var s = CreateScenario();

        var report = Observe(s, 1);
        s.Relay.Publish(s.Source, report, 60);

        // Coupure avant l'arrivee du premier segment.
        s.Network.SetOperational(s.Radio, false);

        s.Relay.Step(65);

        Assert.Empty(
            s.Relay.GetBattalionReports(s.BattalionA));

        Assert.Empty(
            s.Relay.GetDivisionReports(s.French));

        Assert.Equal(1L, s.Relay.DroppedHopCount);

        // Nouvelle observation : le messager est utilise.
        var newReport = Observe(s, 2);

        Assert.True(s.Relay.Publish(
            s.Source, newReport, currentMinute: 120));

        s.Relay.Step(159);

        Assert.Empty(
            s.Relay.GetBattalionReports(s.BattalionA));

        s.Relay.Step(160);

        Assert.Equal(
            160L,
            Assert.Single(
                s.Relay.GetBattalionReports(s.BattalionA))
                .ReceivedMinute);

        s.Relay.Step(170);

        Assert.Equal(
            170L,
            Assert.Single(
                s.Relay.GetDivisionReports(s.French))
                .ReceivedMinute);
    }

    [Fact]
    public void MissingRoute_CanBeRetriedAfterRepair()
    {
        var s = CreateScenario();

        var report = Observe(s, 1);

        s.Network.SetOperational(s.Radio, false);
        s.Network.SetOperational(s.Courier, false);

        Assert.False(s.Relay.Publish(
            s.Source, report, 60));

        Assert.Equal(1L, s.Relay.RoutingFailureCount);
        Assert.Equal(0, s.Relay.PendingMessageCount);

        s.Network.SetOperational(s.Radio, true);

        Assert.True(s.Relay.Publish(
            s.Source, report, 60));

        Assert.False(s.Relay.Publish(
            s.Source, report, 60));

        s.Relay.Step(65);

        Assert.Single(
            s.Relay.GetBattalionReports(s.BattalionA));
    }

    [Fact]
    public void NeighborReceivesOnlyExplicitlySharedReport()
    {
        var s = CreateScenario();

        var report = Observe(s, 1);
        s.Relay.Publish(s.Source, report, 60);

        s.Relay.Step(65);

        Assert.Empty(
            s.Relay.GetBattalionReports(s.BattalionB));

        Assert.True(s.Relay.ShareWithBattalion(
            s.BattalionA,
            s.BattalionB,
            s.Source,
            report.TrackId,
            currentMinute: 65));

        s.Relay.Step(67);

        Assert.Empty(
            s.Relay.GetBattalionReports(s.BattalionB));

        s.Relay.Step(68);

        var shared = Assert.Single(
            s.Relay.GetBattalionReports(s.BattalionB));

        Assert.Equal(68L, shared.ReceivedMinute);
        Assert.Equal(
            report.LastObservedHour,
            shared.Contact.LastObservedHour);

        // Le message conserve l'origine amie initiale.
        Assert.Equal(
            s.BattalionA.Id,
            shared.SourceBattalionId);
    }

    [Fact]
    public void NoAutomaticSharingOrEnemyKnowledge()
    {
        var s = CreateScenario();

        var report = Observe(s, 1);
        s.Relay.Publish(s.Source, report, 60);
        s.Relay.Step(200);

        Assert.Empty(
            s.Relay.GetBattalionReports(s.BattalionB));

        Assert.Empty(
            s.Relay.GetDivisionReports(s.German));

        // Le renseignement diffuse n'est pas une
        // reference vers la compagnie ennemie reelle.
        Assert.DoesNotContain(
            typeof(TacticalNetworkReport).GetProperties(),
            p => p.PropertyType == typeof(CompanyDeployment));
    }

    [Fact]
    public void ForeignNodesAndEnemyLinks_AreRejected()
    {
        var s = CreateScenario();
        var other = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            s.Router.FindRoute(
                s.CompanyNode,
                other.BattalionNode));

        var enemyDivision =
            s.Network.ForDivision(s.German);

        Assert.Throws<InvalidOperationException>(() =>
            s.Network.AddDirectedLink(
                s.BattalionNode,
                enemyDivision,
                TacticalCommunicationMedium.Radio,
                5));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            s.Network.AddDirectedLink(
                s.CompanyNode,
                s.BattalionNode,
                TacticalCommunicationMedium.Radio,
                0));
    }

    [Fact]
    public void RoutingDoesNotModifyMilitaryResources()
    {
        var s = CreateScenario();

        var frenchBefore =
            s.French.GetCurrentResourceTotals();

        var germanBefore =
            s.German.GetCurrentResourceTotals();

        var observed = Observe(s, 1);

        s.Relay.Publish(s.Source, observed, 60);
        s.Relay.Step(200);

        Assert.Equal(
            frenchBefore,
            s.French.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.German.GetCurrentResourceTotals());

        Assert.Equal(
            observed,
            Assert.Single(
                s.Perception.GetReports(s.Source)));

        Assert.Equal(3, s.Battle.Deployments.Count);
    }
}
