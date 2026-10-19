
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalRadioReadinessTests
{
    private sealed record Scenario(
        Division French,
        Division German,
        Company Infantry,
        Company Signals,
        CompanyDeployment Source,
        CompanyDeployment Enemy,
        TacticalPerceptionState Perception,
        TacticalCommunicationNetwork Network,
        TacticalMessageRouter Router,
        TacticalNetworkIntelligenceRelay Relay,
        TacticalCommunicationNode CompanyNode,
        TacticalCommunicationNode BattalionNode,
        TacticalCommunicationNode DivisionNode,
        TacticalCommunicationLink CompanyRadio,
        TacticalCommunicationLink BattalionRadio);

    private static Scenario CreateScenario(
        bool enablePhysicalChecks = true)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Champ de bataille");

        var french = new Division(
            "Division française",
            province,
            fuel: 100,
            ammunition: 100,
            country: france,
            composition: new DivisionComposition(
                manpower: 130,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0,
                radios: 4));

        var german = new Division(
            "Division allemande",
            province,
            fuel: 100,
            ammunition: 100,
            country: germany,
            composition: new DivisionComposition(
                manpower: 100,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Bataillon français",
            BattalionType.Infantry);

        var infantry = new Company(
            1,
            "Infanterie",
            CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                radios: 2));

        var signals = new Company(
            2,
            "Transmissions",
            CompanyType.Signals,
            new CompanyComposition(
                manpower: 30,
                radios: 2));

        battalion.AddCompany(infantry);
        battalion.AddCompany(signals);
        french.AddBattalion(battalion);
        french.EnableHierarchicalResources();

        var germanBattalion = new Battalion(
            1, "Bataillon allemand",
            BattalionType.Infantry);

        var germanInfantry = new Company(
            1, "Infanterie allemande",
            CompanyType.Infantry,
            new CompanyComposition(manpower: 100));

        germanBattalion.AddCompany(germanInfantry);
        german.AddBattalion(germanBattalion);
        german.EnableHierarchicalResources();

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

        var battle = new TacticalBattle(engagement, field);

        var source = battle.DeployCompany(
            infantry,
            sector,
            CompanyDeploymentRole.FrontLine);

        var enemy = battle.DeployCompany(
            germanInfantry,
            sector,
            CompanyDeploymentRole.FrontLine);

        var perception = new TacticalPerceptionState(battle);
        var network = new TacticalCommunicationNetwork(battle);

        var companyNode = network.ForCompany(source);
        var battalionNode = network.ForBattalion(battalion);
        var divisionNode = network.ForDivision(french);

        var companyRadio = network.AddDirectedLink(
            companyNode,
            battalionNode,
            TacticalCommunicationMedium.Radio,
            delayMinutes: 5);

        network.AddDirectedLink(
            companyNode,
            battalionNode,
            TacticalCommunicationMedium.Courier,
            delayMinutes: 40);

        var battalionRadio = network.AddDirectedLink(
            battalionNode,
            divisionNode,
            TacticalCommunicationMedium.Radio,
            delayMinutes: 10);

        network.AddDirectedLink(
            battalionNode,
            divisionNode,
            TacticalCommunicationMedium.Courier,
            delayMinutes: 60);

        if (enablePhysicalChecks)
        {
            network.BindRadioOperator(companyNode, infantry);
            network.BindRadioOperator(battalionNode, signals);
            network.BindRadioOperator(divisionNode, signals);

            network.EnablePhysicalRadioChecks();
        }

        var router = new TacticalMessageRouter(network);

        var relay = new TacticalNetworkIntelligenceRelay(
            battle,
            perception,
            network,
            router);

        return new Scenario(
            french,
            german,
            infantry,
            signals,
            source,
            enemy,
            perception,
            network,
            router,
            relay,
            companyNode,
            battalionNode,
            divisionNode,
            companyRadio,
            battalionRadio);
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
                new TacticalPoint(100, 0, 100),
                ObservedSectorId: 1,
                KnowledgeLevel: TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 40)
        });

        return Assert.Single(
            s.Perception.GetReports(s.Source));
    }

    [Fact]
    public void RadioRequiresBoundOperationalEquipment()
    {
        var s = CreateScenario(
            enablePhysicalChecks: false);

        s.Network.EnablePhysicalRadioChecks();

        // Aucun operateur n'est encore attribue.
        Assert.False(s.Network.CanUse(s.CompanyRadio));

        var route = Assert.IsType<TacticalCommunicationRoute>(
            s.Router.FindRoute(
                s.CompanyNode,
                s.BattalionNode));

        Assert.Equal(40L, route.TotalMinutes);

        s.Network.BindRadioOperator(
            s.CompanyNode, s.Infantry);

        s.Network.BindRadioOperator(
            s.BattalionNode, s.Signals);

        s.Network.BindRadioOperator(
            s.DivisionNode, s.Signals);

        Assert.True(s.Network.CanUse(s.CompanyRadio));

        var improved = Assert.IsType<TacticalCommunicationRoute>(
            s.Router.FindRoute(
                s.CompanyNode,
                s.BattalionNode));

        Assert.Equal(5L, improved.TotalMinutes);
    }

    [Fact]
    public void LosingLastRadio_InvalidatesOnlyRelevantCache()
    {
        var s = CreateScenario();

        var first = Assert.IsType<TacticalCommunicationRoute>(
            s.Router.FindRoute(
                s.CompanyNode, s.DivisionNode));

        Assert.Equal(15L, first.TotalMinutes);

        long searches = s.Router.SearchCount;
        long version = s.Network.Version;

        // Il reste encore une radio operationnelle.
        s.Infantry.Composition!.Radios.ApplyLoss(1);

        Assert.Equal(version, s.Network.Version);

        var cached = s.Router.FindRoute(
            s.CompanyNode, s.DivisionNode);

        Assert.Same(first, cached);
        Assert.Equal(searches, s.Router.SearchCount);

        // La derniere radio est detruite.
        s.Infantry.Composition.Radios.ApplyLoss(1);

        Assert.True(s.Network.Version > version);
        Assert.False(s.Network.CanUse(s.CompanyRadio));

        var fallback = Assert.IsType<TacticalCommunicationRoute>(
            s.Router.FindRoute(
                s.CompanyNode, s.DivisionNode));

        // Messager jusqu'au bataillon : 40 min.
        // Radio du bataillon a la division : 10 min.
        Assert.Equal(50L, fallback.TotalMinutes);
        Assert.Equal(searches + 1, s.Router.SearchCount);

        // Le remplacement d'une radio retablit le reseau.
        s.Infantry.Composition.Radios.Reinforce(1);

        var restored = Assert.IsType<TacticalCommunicationRoute>(
            s.Router.FindRoute(
                s.CompanyNode, s.DivisionNode));

        Assert.Equal(15L, restored.TotalMinutes);
    }

    [Fact]
    public void DestroyedSignalsEquipment_ChangesBothHeadquarters()
    {
        var s = CreateScenario();

        Assert.Equal(
            15L,
            s.Router.FindRoute(
                s.CompanyNode, s.DivisionNode)!
                .TotalMinutes);

        long previousVersion = s.Network.Version;

        // Les deux noeuds de QG utilisent
        // la meme compagnie Signals.
        s.Signals.Composition!.Radios.ApplyLoss(2);

        Assert.Equal(
            previousVersion + 1,
            s.Network.Version);

        Assert.False(s.Network.CanUse(s.CompanyRadio));
        Assert.False(s.Network.CanUse(s.BattalionRadio));

        var route = Assert.IsType<TacticalCommunicationRoute>(
            s.Router.FindRoute(
                s.CompanyNode, s.DivisionNode));

        // Les communications subsistent par messagers.
        Assert.Equal(100L, route.TotalMinutes);

        s.Signals.Composition.Radios.Reinforce(1);

        Assert.True(s.Network.CanUse(s.CompanyRadio));
        Assert.True(s.Network.CanUse(s.BattalionRadio));

        Assert.Equal(
            15L,
            s.Router.FindRoute(
                s.CompanyNode, s.DivisionNode)!
                .TotalMinutes);
    }

    [Fact]
    public void LossOfOperators_DisablesRadioWithoutEquipmentLoss()
    {
        var s = CreateScenario();

        int radiosBefore =
            s.Signals.Composition!.Radios.Current;

        s.Signals.Composition.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 0,
                WoundedInAction: 30,
                MissingOrCaptured: 0));

        Assert.Equal(
            radiosBefore,
            s.Signals.Composition.Radios.Current);

        Assert.False(s.Network.CanUse(s.BattalionRadio));

        Assert.Equal(
            100L,
            s.Router.FindRoute(
                s.CompanyNode, s.DivisionNode)!
                .TotalMinutes);

        // Un operateur revient au service.
        s.Signals.Composition.Manpower.ReturnWoundedToDuty(1);

        Assert.True(s.Network.CanUse(s.BattalionRadio));

        Assert.Equal(
            15L,
            s.Router.FindRoute(
                s.CompanyNode, s.DivisionNode)!
                .TotalMinutes);
    }

    [Fact]
    public void HierarchicalRadioLosses_UpdateSinglePhysicalStock()
    {
        var s = CreateScenario();

        var before =
            s.French.GetCurrentResourceTotals();

        Assert.Equal(4, before.Radios.Available);
        Assert.Equal(4, before.Radios.Authorized);

        var allocations = HierarchicalEquipmentLossApplier.Apply(
            s.French,
            new EquipmentLossReport(
                InfantryEquipment: 0,
                Artillery: 0,
                Trucks: 0,
                Radios: 4));

        Assert.Equal(
            4,
            allocations.Sum(x => x.Losses.Radios));

        Assert.Equal(
            0, s.Infantry.Composition!.Radios.Current);

        Assert.Equal(
            0, s.Signals.Composition!.Radios.Current);

        var after = s.French.GetCurrentResourceTotals();

        Assert.Equal(4, after.Radios.Authorized);
        Assert.Equal(0, after.Radios.Available);

        // Aucun deuxieme stock de radios independant
        // n'a ete cree dans la composition legacy.
        Assert.Equal(
            4, s.French.Composition.Radios.Current);

        Assert.Equal(
            100L,
            s.Router.FindRoute(
                s.CompanyNode, s.DivisionNode)!
                .TotalMinutes);
    }

    [Fact]
    public void ImpossibleRadioLosses_DoNotModifyResources()
    {
        var s = CreateScenario();

        var before =
            s.French.GetCurrentResourceTotals();

        Assert.Throws<InvalidOperationException>(() =>
            HierarchicalEquipmentLossApplier.Apply(
                s.French,
                new EquipmentLossReport(
                    InfantryEquipment: 0,
                    Artillery: 0,
                    Trucks: 0,
                    Radios: 5)));

        Assert.Equal(
            before,
            s.French.GetCurrentResourceTotals());

        Assert.Equal(
            15L,
            s.Router.FindRoute(
                s.CompanyNode, s.DivisionNode)!
                .TotalMinutes);
    }

    [Fact]
    public void InFlightRadioFailure_DropsCurrentPacket()
    {
        var s = CreateScenario();

        var firstReport = Observe(s, 1);

        Assert.True(s.Relay.Publish(
            s.Source, firstReport, currentMinute: 60));

        // Le paquet est parti par radio.
        // La station cesse de fonctionner
        // avant l'heure d'arrivee du premier segment.
        s.Infantry.Composition!.Radios.ApplyLoss(2);

        s.Relay.Step(65);

        Assert.Equal(1L, s.Relay.DroppedHopCount);

        Assert.Empty(
            s.Relay.GetBattalionReports(
                s.Infantry.ParentBattalion!));

        // Un rapport plus recent peut etre
        // transmis par un autre moyen.
        var secondReport = Observe(s, 2);

        Assert.True(s.Relay.Publish(
            s.Source, secondReport, currentMinute: 120));

        s.Relay.Step(160);

        Assert.Equal(
            160L,
            Assert.Single(
                s.Relay.GetBattalionReports(
                    s.Infantry.ParentBattalion!))
                .ReceivedMinute);

        s.Relay.Step(170);

        Assert.Equal(
            170L,
            Assert.Single(
                s.Relay.GetDivisionReports(s.French))
                .ReceivedMinute);
    }

    [Fact]
    public void ForeignOperatorBinding_IsRejected()
    {
        var s = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            s.Network.BindRadioOperator(
                s.CompanyNode,
                s.Signals));

        Assert.Throws<InvalidOperationException>(() =>
            s.Network.BindRadioOperator(
                s.DivisionNode,
                s.Infantry));

        // Une affectation valide et deja existante
        // ne cree pas de changement de version.
        long before = s.Network.Version;

        s.Network.BindRadioOperator(
            s.BattalionNode,
            s.Signals);

        Assert.Equal(before, s.Network.Version);
    }

    [Fact]
    public void NetworkDispose_DetachesResourceSubscriptions()
    {
        var s = CreateScenario();

        var route = s.Router.FindRoute(
            s.CompanyNode,
            s.DivisionNode);

        Assert.NotNull(route);

        s.Network.Dispose();

        // Les ressources restent modifiables :
        // le reseau ne doit plus reagir.
        s.Infantry.Composition!.Radios.ApplyLoss(1);
        s.Signals.Composition!.Radios.ApplyLoss(1);

        Assert.Throws<ObjectDisposedException>(() =>
            s.Network.CanUse(s.CompanyRadio));
    }
}
