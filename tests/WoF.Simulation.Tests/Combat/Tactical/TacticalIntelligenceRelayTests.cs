
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalIntelligenceRelayTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalPerceptionState Perception,
        TacticalIntelligenceRelay Relay,
        CompanyDeployment First,
        CompanyDeployment Second,
        CompanyDeployment Third,
        CompanyDeployment Enemy,
        Battalion FirstBattalion,
        Battalion SecondBattalion,
        Division French,
        Division German,
        BattleSector Sector);

    private static Division MakeDivision(
        string name,
        Country country,
        Province province,
        params int[] companiesPerBattalion)
    {
        int totalCompanies = companiesPerBattalion.Sum();

        var division = new Division(
            name,
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: totalCompanies * 100,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        for (int b = 0; b < companiesPerBattalion.Length; b++)
        {
            var battalion = new Battalion(
                b + 1,
                $"Bataillon {b + 1}",
                BattalionType.Infantry);

            for (int c = 0; c < companiesPerBattalion[b]; c++)
            {
                battalion.AddCompany(new Company(
                    c + 1,
                    $"Compagnie {c + 1}",
                    CompanyType.Infantry,
                    new CompanyComposition(
                        manpower: 100)));
            }

            division.AddBattalion(battalion);
        }

        division.EnableHierarchicalResources();
        return division;
    }

    private static Scenario CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Test");

        var french = MakeDivision(
            "Division française",
            france,
            province,
            2, 1);

        var german = MakeDivision(
            "Division allemande",
            germany,
            province,
            1);

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
            1,
            "Village",
            TerrainType.Urban);

        field.AddSector(sector);

        var battle = new TacticalBattle(
            engagement,
            field);

        var firstBattalion = french.Battalions[0];
        var secondBattalion = french.Battalions[1];

        var first = battle.DeployCompany(
            firstBattalion.Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var second = battle.DeployCompany(
            firstBattalion.Companies[1],
            sector,
            CompanyDeploymentRole.FrontLine);

        var third = battle.DeployCompany(
            secondBattalion.Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var enemy = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var perception = new TacticalPerceptionState(
            battle);

        var relay = new TacticalIntelligenceRelay(
            battle,
            perception,
            new TacticalCommunicationDelays(
                companyToBattalionHours: 1,
                battalionToDivisionHours: 2));

        return new Scenario(
            battle,
            perception,
            relay,
            first,
            second,
            third,
            enemy,
            firstBattalion,
            secondBattalion,
            french,
            german,
            sector);
    }

    private static TacticalObservationEvent Observation(
        Scenario s,
        CompanyDeployment source,
        TacticalKnowledgeLevel level =
            TacticalKnowledgeLevel.Located)
    {
        bool localized =
            level >= TacticalKnowledgeLevel.Located;

        return new TacticalObservationEvent(
            source,
            s.Enemy,
            localized
                ? new TacticalPoint(120, 20, 100)
                : null,
            s.Sector.Id,
            level,
            PositionUncertaintyMeters:
                localized ? 40.0 : null,
            EstimatedCompanyType:
                level == TacticalKnowledgeLevel.Identified
                    ? CompanyType.Armor
                    : null);
    }

    private static void Observe(
        Scenario s,
        long hour,
        CompanyDeployment source,
        TacticalKnowledgeLevel level =
            TacticalKnowledgeLevel.Located)
    {
        s.Perception.Apply(hour, new[]
        {
            Observation(s, source, level)
        });
    }

    [Fact]
    public void ReportsReachBattalionBeforeDivision()
    {
        var s = CreateScenario();

        Observe(s, 1, s.First);

        var local = Assert.Single(
            s.Perception.GetReports(s.First));

        s.Relay.Step(1);

        Assert.Empty(
            s.Relay.GetBattalionReports(s.FirstBattalion));

        Assert.Empty(
            s.Relay.GetDivisionReports(s.French));

        s.Relay.Step(2);

        var battalion = Assert.Single(
            s.Relay.GetBattalionReports(s.FirstBattalion));

        Assert.Equal(2L, battalion.ReceivedHour);
        Assert.Equal(local.TrackId, battalion.Contact.TrackId);
        Assert.Equal(1L, battalion.Contact.LastObservedHour);

        // Le bataillon a recu un rapport.
        // Il n'observe pas lui-meme la cible.
        Assert.False(battalion.Contact.IsCurrentlyObserved);

        s.Relay.Step(3);

        Assert.Empty(
            s.Relay.GetDivisionReports(s.French));

        s.Relay.Step(4);

        var division = Assert.Single(
            s.Relay.GetDivisionReports(s.French));

        Assert.Equal(4L, division.ReceivedHour);
        Assert.Equal(1L, division.Contact.LastObservedHour);

        Assert.Equal(
            local.LastKnownPosition,
            division.Contact.LastKnownPosition);
    }

    [Fact]
    public void SkippingHours_StillProcessesBothHops()
    {
        var s = CreateScenario();

        Observe(s, 1, s.First);
        s.Relay.Step(1);

        // On saute directement de l'heure 1 a l'heure 5.
        s.Relay.Step(5);

        var battalion = Assert.Single(
            s.Relay.GetBattalionReports(s.FirstBattalion));

        var division = Assert.Single(
            s.Relay.GetDivisionReports(s.French));

        // Dates de reception simulees, pas l'heure
        // a laquelle Step a ete effectivement appele.
        Assert.Equal(2L, battalion.ReceivedHour);
        Assert.Equal(4L, division.ReceivedHour);
    }

    [Fact]
    public void ImprovedInformation_MustTraverseHierarchyAgain()
    {
        var s = CreateScenario();

        Observe(
            s,
            1,
            s.First,
            TacticalKnowledgeLevel.Suspected);

        s.Relay.Step(1);
        s.Relay.Step(4);

        var oldDivision = Assert.Single(
            s.Relay.GetDivisionReports(s.French));

        Assert.Equal(
            TacticalKnowledgeLevel.Suspected,
            oldDivision.Contact.BestKnownLevel);

        // La compagnie identifie ensuite la cible.
        Observe(
            s,
            5,
            s.First,
            TacticalKnowledgeLevel.Identified);

        s.Relay.Step(5);

        // L'information reste ancienne a la division.
        Assert.Equal(
            TacticalKnowledgeLevel.Suspected,
            Assert.Single(
                s.Relay.GetDivisionReports(s.French))
                .Contact.BestKnownLevel);

        s.Relay.Step(6);

        var newBattalion = Assert.Single(
            s.Relay.GetBattalionReports(s.FirstBattalion));

        Assert.Equal(
            TacticalKnowledgeLevel.Identified,
            newBattalion.Contact.BestKnownLevel);

        Assert.Equal(6L, newBattalion.ReceivedHour);

        s.Relay.Step(7);

        Assert.Equal(
            TacticalKnowledgeLevel.Suspected,
            Assert.Single(
                s.Relay.GetDivisionReports(s.French))
                .Contact.BestKnownLevel);

        s.Relay.Step(8);

        var newDivision = Assert.Single(
            s.Relay.GetDivisionReports(s.French));

        Assert.Equal(
            TacticalKnowledgeLevel.Identified,
            newDivision.Contact.BestKnownLevel);

        Assert.Equal(5L, newDivision.Contact.LastObservedHour);
        Assert.Equal(8L, newDivision.ReceivedHour);

        // Le premier rapport est reste une copie ancienne.
        Assert.Equal(
            TacticalKnowledgeLevel.Suspected,
            oldDivision.Contact.BestKnownLevel);
    }

    [Fact]
    public void LosingContact_DoesNotCreateNewTransmission()
    {
        var s = CreateScenario();

        Observe(s, 1, s.First);
        s.Relay.Step(1);

        // Plus aucune observation a l'heure 2.
        s.Perception.Apply(
            2,
            Array.Empty<TacticalObservationEvent>());

        s.Relay.Step(2);
        s.Relay.Step(4);

        var received = Assert.Single(
            s.Relay.GetDivisionReports(s.French));

        Assert.Equal(1L, received.Contact.LastObservedHour);
        Assert.Equal(4L, received.ReceivedHour);
        Assert.False(received.Contact.IsCurrentlyObserved);

        // Une observation perdue ne cree pas
        // une nouvelle piste artificielle.
        Assert.Single(
            s.Perception.GetReports(s.First));
    }

    [Fact]
    public void TwoCompaniesReportingSameEnemy_KeepSeparateTracks()
    {
        var s = CreateScenario();

        s.Perception.Apply(1, new[]
        {
            Observation(s, s.First),
            Observation(s, s.Second)
        });

        s.Relay.Step(1);
        s.Relay.Step(4);

        var battalion =
            s.Relay.GetBattalionReports(s.FirstBattalion);

        var division =
            s.Relay.GetDivisionReports(s.French);

        Assert.Equal(2, battalion.Count);
        Assert.Equal(2, division.Count);

        // Pas de fusion automatique de pistes provenant
        // d'observateurs distincts.
        Assert.NotEqual(
            division[0].Contact.TrackId,
            division[1].Contact.TrackId);

        Assert.Equal(1, division[0].SourceCompanyId);
        Assert.Equal(2, division[1].SourceCompanyId);

        // Les deux rapports concernent pourtant
        // la meme compagnie ennemie reelle.
        Assert.Equal(
            division[0].Contact.LastKnownPosition,
            division[1].Contact.LastKnownPosition);
    }

    [Fact]
    public void BattalionsDoNotExchangeReportsAutomatically()
    {
        var s = CreateScenario();

        Observe(s, 1, s.First);

        s.Relay.Step(1);
        s.Relay.Step(4);

        Assert.Single(
            s.Relay.GetBattalionReports(s.FirstBattalion));

        Assert.Empty(
            s.Relay.GetBattalionReports(s.SecondBattalion));

        Assert.Single(
            s.Relay.GetDivisionReports(s.French));

        Assert.Empty(
            s.Relay.GetDivisionReports(s.German));
    }

    [Fact]
    public void ReportsFromDifferentBattalionsReachSameDivision()
    {
        var s = CreateScenario();

        s.Perception.Apply(1, new[]
        {
            Observation(s, s.First),
            Observation(s, s.Third)
        });

        s.Relay.Step(1);
        s.Relay.Step(4);

        Assert.Single(
            s.Relay.GetBattalionReports(s.FirstBattalion));

        Assert.Single(
            s.Relay.GetBattalionReports(s.SecondBattalion));

        var division =
            s.Relay.GetDivisionReports(s.French);

        Assert.Equal(2, division.Count);

        Assert.Equal(1, division[0].SourceBattalionId);
        Assert.Equal(2, division[1].SourceBattalionId);
    }

    [Fact]
    public void DuplicateSteps_DoNotResendSameObservation()
    {
        var s = CreateScenario();

        Observe(s, 1, s.First);

        s.Relay.Step(1);
        s.Relay.Step(1);

        s.Relay.Step(2);
        s.Relay.Step(2);

        s.Relay.Step(4);
        s.Relay.Step(4);

        Assert.Single(
            s.Relay.GetBattalionReports(s.FirstBattalion));

        Assert.Single(
            s.Relay.GetDivisionReports(s.French));

        Assert.Equal(
            4L,
            Assert.Single(
                s.Relay.GetDivisionReports(s.French))
                .ReceivedHour);
    }

    [Fact]
    public void ForeignHierarchyAndBackwardsTime_AreRejected()
    {
        var s = CreateScenario();
        var other = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            new TacticalIntelligenceRelay(
                s.Battle,
                other.Perception,
                new TacticalCommunicationDelays()));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalCommunicationDelays(
                companyToBattalionHours: 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalCommunicationDelays(
                battalionToDivisionHours: -1));

        Assert.Throws<InvalidOperationException>(() =>
            s.Relay.GetBattalionReports(
                other.FirstBattalion));

        Assert.Throws<InvalidOperationException>(() =>
            s.Relay.GetDivisionReports(other.French));

        s.Relay.Step(5);

        Assert.Throws<InvalidOperationException>(() =>
            s.Relay.Step(4));

        Assert.Empty(
            s.Relay.GetDivisionReports(s.French));
    }

    [Fact]
    public void ForwardingDoesNotModifyMilitaryResources()
    {
        var s = CreateScenario();

        var frenchBefore =
            s.French.GetCurrentResourceTotals();

        var germanBefore =
            s.German.GetCurrentResourceTotals();

        Observe(s, 1, s.First);

        var local = Assert.Single(
            s.Perception.GetReports(s.First));

        s.Relay.Step(1);
        s.Relay.Step(4);

        var received = Assert.Single(
            s.Relay.GetDivisionReports(s.French));

        Assert.Equal(
            frenchBefore,
            s.French.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.German.GetCurrentResourceTotals());

        Assert.Equal(4, s.Battle.Deployments.Count);

        // La transmission n'a pas modifie la
        // memoire de la compagnie observatrice.
        Assert.Equal(
            local,
            Assert.Single(
                s.Perception.GetReports(s.First)));

        // La copie transmise n'expose pas
        // de reference a une compagnie ennemie.
        Assert.DoesNotContain(
            typeof(TacticalRelayedContactReport).GetProperties(),
            p => p.PropertyType == typeof(CompanyDeployment));

        Assert.False(received.Contact.IsCurrentlyObserved);
    }
}
