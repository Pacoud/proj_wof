
using System.Diagnostics;
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Client.Prototype;

public sealed class TacticalIntelligenceDemoScenario : IDisposable
{
    private static readonly long[] Moments =
    {
        60, 120, 125, 130, 135, 180, 220, 225, 280
    };

    private static readonly string[] Events =
    {
        "Déploiement initial et première observation",
        "Activité de tir allemande — signal acoustique",
        "Réception du premier rapport au bataillon",
        "Neutralisation des radios de la compagnie Signals",
        "Échec de la transmission vers la division",
        "Nouvelle observation : localisation visuelle",
        "Réception du rapport par messager au bataillon",
        "Rétablissement partiel des radios",
        "Réception du rapport au QG divisionnaire"
    };

    private int _momentIndex;

    public long CurrentMinute => Moments[_momentIndex];
    public bool CanAdvance => _momentIndex < Moments.Length - 1;
    public string DeveloperEvent => Events[_momentIndex];

    public TacticalBattle Battle { get; }
    public TacticalGeometryMap Geometry { get; }
    public TacticalPerceptionState Perception { get; }
    public TacticalCommunicationNetwork Network { get; }
    public TacticalMessageRouter Router { get; }
    public TacticalNetworkIntelligenceRelay Relay { get; }

    public Division FrenchDivision { get; }
    public Division GermanDivision { get; }
    public Battalion FrenchBattalion { get; }

    public Company SignalsCompany { get; }
    public CompanyDeployment FrenchDeployment { get; }
    public CompanyDeployment GermanDeployment { get; }

    public TacticalCommunicationLink CompanyRadio { get; }
    public TacticalCommunicationLink BattalionRadio { get; }

    private readonly DiplomacySystem _diplomacy;
    private readonly TacticalDetectionProfile _profile =
        new(
            maxVisualRangeMeters: 200,
            maxAcousticRangeMeters: 300,
            fieldOfViewDegrees: 120,
            localizationCellMeters: 50);

    public double LastDetectionMs { get; private set; }
    public double LastCommunicationMs { get; private set; }

    public TacticalIntelligenceDemoScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Allemagne");
        var province = new Province(
            100, "Secteur d'essai", TerrainType.Plains);

        FrenchDivision = new Division(
            "Division française", province,
            fuel: 100,
            ammunition: 100,
            country: france,
            composition: new DivisionComposition(
                manpower: 130,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0,
                radios: 4));

        GermanDivision = new Division(
            "Division allemande", province,
            fuel: 100,
            ammunition: 100,
            country: germany,
            composition: new DivisionComposition(
                manpower: 100,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        FrenchBattalion = new Battalion(
            1, "Ier Bataillon", BattalionType.Infantry);

        var frenchInfantry = new Company(
            1, "Compagnie d'infanterie",
            CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                radios: 2));

        SignalsCompany = new Company(
            2, "Compagnie Signals",
            CompanyType.Signals,
            new CompanyComposition(
                manpower: 30,
                radios: 2));

        FrenchBattalion.AddCompany(frenchInfantry);
        FrenchBattalion.AddCompany(SignalsCompany);

        FrenchDivision.AddBattalion(FrenchBattalion);
        FrenchDivision.EnableHierarchicalResources();

        var germanBattalion = new Battalion(
            1, "Bataillon allemand", BattalionType.Infantry);

        var germanInfantry = new Company(
            1, "Compagnie allemande",
            CompanyType.Infantry,
            new CompanyComposition(manpower: 100));

        germanBattalion.AddCompany(germanInfantry);
        GermanDivision.AddBattalion(germanBattalion);
        GermanDivision.EnableHierarchicalResources();

        _diplomacy = new DiplomacySystem();
        _diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(_diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { FrenchDivision, GermanDivision },
            currentHour: 1);

        var engagement =
            engagements.ActiveEngagements.Single();

        var battlefield = new TacticalBattlefield();
        var sector = new BattleSector(
            1, "Plaine d'essai", TerrainType.Plains);

        battlefield.AddSector(sector);

        Battle = new TacticalBattle(
            engagement, battlefield);

        FrenchDeployment = Battle.DeployCompany(
            frenchInfantry,
            sector,
            CompanyDeploymentRole.FrontLine);

        GermanDeployment = Battle.DeployCompany(
            germanInfantry,
            sector,
            CompanyDeploymentRole.FrontLine);

        Geometry = new TacticalGeometryMap(Battle);
        Geometry.DefineSector(sector, 90, 0, 300);

        Geometry.PlaceCompany(
            FrenchDeployment,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        Geometry.PlaceCompany(
            GermanDeployment,
            new TacticalPoint(180, 0, 100),
            facingDegrees: 270,
            frontageMeters: 40);

        Perception = new TacticalPerceptionState(Battle);

        Network = new TacticalCommunicationNetwork(Battle);

        var companyNode =
            Network.ForCompany(FrenchDeployment);

        var battalionNode =
            Network.ForBattalion(FrenchBattalion);

        var divisionNode =
            Network.ForDivision(FrenchDivision);

        CompanyRadio = Network.AddDirectedLink(
            companyNode, battalionNode,
            TacticalCommunicationMedium.Radio,
            delayMinutes: 5);

        Network.AddDirectedLink(
            companyNode, battalionNode,
            TacticalCommunicationMedium.Courier,
            delayMinutes: 40);

        BattalionRadio = Network.AddDirectedLink(
            battalionNode, divisionNode,
            TacticalCommunicationMedium.Radio,
            delayMinutes: 10);

        Network.AddDirectedLink(
            battalionNode, divisionNode,
            TacticalCommunicationMedium.Courier,
            delayMinutes: 60);

        Network.BindRadioOperator(
            companyNode, frenchInfantry);

        Network.BindRadioOperator(
            battalionNode, SignalsCompany);

        Network.BindRadioOperator(
            divisionNode, SignalsCompany);

        Network.EnablePhysicalRadioChecks();

        Router = new TacticalMessageRouter(Network);

        Relay = new TacticalNetworkIntelligenceRelay(
            Battle, Perception, Network, Router);

        // Une compagnie allemande dissimulée.
        // Les Français ne la détectent pas encore,
        // mais les Allemands peuvent voir les Français.
        Detect(
            1,
            TacticalActivity.Stationary,
            TacticalConcealment.Strong);
    }

    private void Detect(
        long hour,
        TacticalActivity germanActivity,
        TacticalConcealment germanConcealment)
    {
        var watch = Stopwatch.StartNew();

        var frame = new TacticalSignatureFrame(Battle);

        frame.Set(
            FrenchDeployment,
            new TacticalUnitSignature(
                TacticalActivity.Stationary,
                TacticalConcealment.None));

        frame.Set(
            GermanDeployment,
            new TacticalUnitSignature(
                germanActivity,
                germanConcealment));

        TacticalDetectionSystem.Update(
            Battle, Geometry, _diplomacy,
            Perception, frame, hour, _profile);

        watch.Stop();
        LastDetectionMs = watch.Elapsed.TotalMilliseconds;
    }

    private void PublishFrenchObservation(long minute)
    {
        foreach (var report in Perception.GetReports(
            FrenchDeployment))
        {
            if (report.IsCurrentlyObserved
                && report.LastObservedHour == minute / 60)
            {
                Relay.Publish(
                    FrenchDeployment, report, minute);
            }
        }
    }

    public void Advance()
    {
        if (!CanAdvance)
            return;

        _momentIndex++;
        long minute = CurrentMinute;

        // Les événements physiques sont appliqués
        // dans l'ordre chronologique explicite.
        switch (minute)
        {
            case 120:
                Detect(
                    2,
                    TacticalActivity.Firing,
                    TacticalConcealment.Strong);

                PublishFrenchObservation(minute);
                break;

            case 130:
                SignalsCompany.Composition!.Radios.ApplyLoss(2);
                break;

            case 180:
                Detect(
                    3,
                    TacticalActivity.Firing,
                    TacticalConcealment.Partial);

                PublishFrenchObservation(minute);
                break;

            case 225:
                SignalsCompany.Composition!.Radios.Reinforce(1);
                break;
        }

        var watch = Stopwatch.StartNew();

        // Le relais avance à chaque événement.
        // Les dates intermédiaires ne sont pas ignorées.
        Relay.Step(minute);

        watch.Stop();
        LastCommunicationMs = watch.Elapsed.TotalMilliseconds;
    }

    public void Dispose()
    {
        Network.Dispose();
    }
}
