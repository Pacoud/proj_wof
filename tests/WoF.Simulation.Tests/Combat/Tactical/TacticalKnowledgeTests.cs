
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalKnowledgeTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalPerceptionState Perception,
        DiplomacySystem Diplomacy,
        CompanyDeployment Observer,
        CompanyDeployment SecondObserver,
        CompanyDeployment Target,
        Division French,
        Division German,
        BattleSector MainSector,
        BattleSector OtherSector);

    private static Division MakeDivision(
        string name,
        Country country,
        Province province,
        int companyCount)
    {
        var division = new Division(
            name, province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: companyCount * 100,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Bataillon",
            BattalionType.Infantry);

        for (int i = 1; i <= companyCount; i++)
        {
            battalion.AddCompany(new Company(
                i,
                $"Compagnie {i}",
                CompanyType.Infantry,
                new CompanyComposition(manpower: 100)));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Champ de bataille");

        var french = MakeDivision(
            "Division française", france, province, 2);

        var german = MakeDivision(
            "Division allemande", germany, province, 1);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var battlefield = new TacticalBattlefield();

        var main = new BattleSector(
            1, "Colline", TerrainType.Hills);

        var other = new BattleSector(
            2, "Village", TerrainType.Urban);

        battlefield.AddSector(main);
        battlefield.AddSector(other);
        battlefield.Connect(main, other, 300);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var observer = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            main,
            CompanyDeploymentRole.FrontLine);

        var secondObserver = battle.DeployCompany(
            french.Battalions[0].Companies[1],
            main,
            CompanyDeploymentRole.FrontLine);

        var target = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            main,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(main, 0, 0, 200);
        geometry.DefineSector(other, 300, 0, 150);

        geometry.PlaceCompany(
            observer,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        geometry.PlaceCompany(
            target,
            new TacticalPoint(100, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        // Le deuxième observateur n'est pas positionné.
        // Il ne doit recevoir aucun rapport par magie.

        return new Scenario(
            battle,
            geometry,
            new TacticalPerceptionState(battle),
            diplomacy,
            observer,
            secondObserver,
            target,
            french,
            german,
            main,
            other);
    }

    private static TacticalObservationEvent Evidence(
        Scenario scenario,
        TacticalKnowledgeLevel level,
        TacticalPoint? point = null,
        double? uncertainty = null,
        CompanyType? category = null,
        int? sectorId = null)
    {
        return new TacticalObservationEvent(
            scenario.Observer,
            scenario.Target,
            point,
            sectorId ?? scenario.MainSector.Id,
            level,
            uncertainty,
            category);
    }

    [Fact]
    public void ExistingVisualDetection_ProducesLocatedKnowledge()
    {
        var s = CreateScenario();

        // Utilise le système réel de C2.2c.1.
        TacticalPerceptionSystem.Update(
            s.Battle,
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            currentHour: 1,
            new TacticalObservationProfile(200, 120));

        var report = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(
            TacticalKnowledgeLevel.Located,
            report.BestKnownLevel);

        Assert.Equal(
            TacticalKnowledgeLevel.Located,
            report.CurrentObservationLevel);

        Assert.Equal(
            new TacticalPoint(100, 0, 100),
            report.LastKnownPosition);

        Assert.Equal(1L, report.LastLocatedHour);
        Assert.Equal(0.0, report.PositionUncertaintyMeters);

        // Voir l'emplacement ne révèle pas
        // automatiquement la catégorie de la cible.
        Assert.Null(report.EstimatedCompanyType);
    }

    [Fact]
    public void Suspicion_DoesNotExposeAnExactPosition()
    {
        var s = CreateScenario();

        s.Perception.Apply(1, new[]
        {
            Evidence(s, TacticalKnowledgeLevel.Suspected)
        });

        var report = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(
            TacticalKnowledgeLevel.Suspected,
            report.BestKnownLevel);

        Assert.Equal(
            s.MainSector.Id,
            report.LastKnownSectorId);

        Assert.Null(report.LastKnownPosition);
        Assert.Null(report.PositionUncertaintyMeters);
        Assert.Null(report.LastLocatedHour);
        Assert.Null(report.EstimatedCompanyType);

        Assert.True(report.IsCurrentlyObserved);

        Assert.Empty(
            s.Perception.GetReports(s.SecondObserver));

        Assert.Empty(
            s.Perception.GetReports(s.Target));
    }

    [Fact]
    public void Evidence_ProgressesFromSuspicionToIdentification()
    {
        var s = CreateScenario();

        s.Perception.Apply(1, new[]
        {
            Evidence(s, TacticalKnowledgeLevel.Suspected)
        });

        int trackId = Assert.Single(
            s.Perception.GetReports(s.Observer)).TrackId;

        s.Perception.Apply(2, new[]
        {
            Evidence(
                s,
                TacticalKnowledgeLevel.Located,
                point: new TacticalPoint(105, 20, 100),
                uncertainty: 75)
        });

        var located = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(trackId, located.TrackId);

        Assert.Equal(
            TacticalKnowledgeLevel.Located,
            located.BestKnownLevel);

        Assert.Equal(75.0, located.PositionUncertaintyMeters);
        Assert.Null(located.EstimatedCompanyType);

        s.Perception.Apply(3, new[]
        {
            Evidence(
                s,
                TacticalKnowledgeLevel.Identified,
                point: new TacticalPoint(106, 24, 100),
                uncertainty: 35,
                category: CompanyType.Armor)
        });

        var identified = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(trackId, identified.TrackId);

        Assert.Equal(
            TacticalKnowledgeLevel.Identified,
            identified.BestKnownLevel);

        Assert.Equal(
            TacticalKnowledgeLevel.Identified,
            identified.CurrentObservationLevel);

        Assert.Equal(
            new TacticalPoint(106, 24, 100),
            identified.LastKnownPosition);

        Assert.Equal(35.0, identified.PositionUncertaintyMeters);
        Assert.Equal(3L, identified.LastLocatedHour);

        // Cette identification peut être erronée :
        // la véritable cible de test est une infanterie.
        Assert.Equal(
            CompanyType.Armor,
            identified.EstimatedCompanyType);

        Assert.Equal(
            CompanyType.Infantry,
            s.Target.Company.Type);
    }

    [Fact]
    public void WeakerEvidence_DoesNotRefreshOldPosition()
    {
        var s = CreateScenario();

        var oldPosition = new TacticalPoint(100, 0, 100);

        s.Perception.Apply(1, new[]
        {
            Evidence(
                s,
                TacticalKnowledgeLevel.Identified,
                point: oldPosition,
                uncertainty: 20,
                category: CompanyType.Infantry)
        });

        // À l'heure 2, un bruit est signalé au village,
        // mais aucune nouvelle position n'est obtenue.
        s.Perception.Apply(2, new[]
        {
            Evidence(
                s,
                TacticalKnowledgeLevel.Suspected,
                sectorId: s.OtherSector.Id)
        });

        var report = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(
            TacticalKnowledgeLevel.Identified,
            report.BestKnownLevel);

        Assert.Equal(
            TacticalKnowledgeLevel.Suspected,
            report.CurrentObservationLevel);

        Assert.Equal(
            oldPosition,
            report.LastKnownPosition);

        // La position demeure celle de l'heure 1.
        Assert.Equal(1L, report.LastLocatedHour);

        // Mais la dernière activité date de l'heure 2.
        Assert.Equal(2L, report.LastObservedHour);

        Assert.Equal(
            s.MainSector.Id,
            report.LastKnownSectorId);

        Assert.Equal(
            s.OtherSector.Id,
            report.CurrentObservedSectorId);

        Assert.Equal(
            CompanyType.Infantry,
            report.EstimatedCompanyType);
    }

    [Fact]
    public void NewLocalization_PreservesPreviouslyKnownCategory()
    {
        var s = CreateScenario();

        s.Perception.Apply(1, new[]
        {
            Evidence(
                s,
                TacticalKnowledgeLevel.Identified,
                point: new TacticalPoint(100, 0, 100),
                uncertainty: 30,
                category: CompanyType.Infantry)
        });

        s.Perception.Apply(2, new[]
        {
            Evidence(
                s,
                TacticalKnowledgeLevel.Located,
                point: new TacticalPoint(120, 15, 100),
                uncertainty: 50)
        });

        var report = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(
            TacticalKnowledgeLevel.Identified,
            report.BestKnownLevel);

        Assert.Equal(
            TacticalKnowledgeLevel.Located,
            report.CurrentObservationLevel);

        Assert.Equal(
            new TacticalPoint(120, 15, 100),
            report.LastKnownPosition);

        Assert.Equal(2L, report.LastLocatedHour);

        Assert.Equal(
            CompanyType.Infantry,
            report.EstimatedCompanyType);
    }

    [Fact]
    public void LostAndReacquiredContact_RetainsTrackIdentity()
    {
        var s = CreateScenario();

        s.Perception.Apply(1, new[]
        {
            Evidence(s, TacticalKnowledgeLevel.Suspected)
        });

        var initial = Assert.Single(
            s.Perception.GetReports(s.Observer));

        s.Perception.Apply(
            2,
            Array.Empty<TacticalObservationEvent>());

        var lost = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.False(lost.IsCurrentlyObserved);
        Assert.Null(lost.CurrentObservationLevel);
        Assert.Null(lost.CurrentObservedSectorId);

        Assert.Equal(1L, lost.LastObservedHour);

        s.Perception.Apply(3, new[]
        {
            Evidence(
                s,
                TacticalKnowledgeLevel.Located,
                point: new TacticalPoint(110, 10, 100),
                uncertainty: 40)
        });

        var reacquired = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(initial.TrackId, reacquired.TrackId);
        Assert.True(reacquired.IsCurrentlyObserved);
        Assert.Equal(3L, reacquired.LastLocatedHour);

        // Le premier rapport était une copie indépendante.
        Assert.True(initial.IsCurrentlyObserved);
    }

    [Fact]
    public void InvalidEvidence_IsRejectedWithoutPartialUpdate()
    {
        var s = CreateScenario();

        // Une suspicion ne peut pas contenir
        // une position précise.
        Assert.Throws<InvalidOperationException>(() =>
            s.Perception.Apply(1, new[]
            {
                Evidence(
                    s,
                    TacticalKnowledgeLevel.Suspected,
                    point: new TacticalPoint(100, 0, 100))
            }));

        // Une identification exige une catégorie.
        Assert.Throws<InvalidOperationException>(() =>
            s.Perception.Apply(1, new[]
            {
                Evidence(
                    s,
                    TacticalKnowledgeLevel.Identified,
                    point: new TacticalPoint(100, 0, 100),
                    uncertainty: 20)
            }));

        // L'incertitude ne peut pas être négative.
        Assert.Throws<InvalidOperationException>(() =>
            s.Perception.Apply(1, new[]
            {
                Evidence(
                    s,
                    TacticalKnowledgeLevel.Located,
                    point: new TacticalPoint(100, 0, 100),
                    uncertainty: -10)
            }));

        // Le secteur renseigné doit exister.
        Assert.Throws<InvalidOperationException>(() =>
            s.Perception.Apply(1, new[]
            {
                Evidence(
                    s,
                    TacticalKnowledgeLevel.Suspected,
                    sectorId: 999)
            }));

        var valid = Evidence(
            s,
            TacticalKnowledgeLevel.Suspected);

        // Une passe contenant un doublon ne doit
        // enregistrer aucune des deux observations.
        Assert.Throws<InvalidOperationException>(() =>
            s.Perception.Apply(1, new[] { valid, valid }));

        Assert.Empty(s.Perception.GetReports(s.Observer));
    }

    [Fact]
    public void ObserversAndBattles_KeepSeparateKnowledge()
    {
        var s = CreateScenario();

        s.Perception.Apply(1, new[]
        {
            Evidence(
                s,
                TacticalKnowledgeLevel.Located,
                point: new TacticalPoint(100, 0, 100),
                uncertainty: 30)
        });

        Assert.Single(s.Perception.GetReports(s.Observer));

        Assert.Empty(
            s.Perception.GetReports(s.SecondObserver));

        var other = CreateScenario();

        Assert.Empty(
            other.Perception.GetReports(other.Observer));

        Assert.Throws<InvalidOperationException>(() =>
            s.Perception.GetReports(other.Observer));

        Assert.Throws<InvalidOperationException>(() =>
            s.Perception.Apply(2, new[]
            {
                new TacticalObservationEvent(
                    s.Observer,
                    other.Target,
                    null,
                    s.MainSector.Id,
                    TacticalKnowledgeLevel.Suspected,
                    null)
            }));

        Assert.Single(s.Perception.GetReports(s.Observer));
    }

    [Fact]
    public void KnowledgeUpdates_DoNotModifyMilitaryResources()
    {
        var s = CreateScenario();

        var frenchBefore =
            s.French.GetCurrentResourceTotals();

        var germanBefore =
            s.German.GetCurrentResourceTotals();

        s.Perception.Apply(2, new[]
        {
            Evidence(
                s,
                TacticalKnowledgeLevel.Located,
                point: new TacticalPoint(100, 0, 100),
                uncertainty: 25)
        });

        var original = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Throws<InvalidOperationException>(() =>
            s.Perception.Apply(
                1,
                Array.Empty<TacticalObservationEvent>()));

        var after = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(original, after);

        Assert.Equal(
            frenchBefore,
            s.French.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.German.GetCurrentResourceTotals());

        Assert.Equal(3, s.Battle.Deployments.Count);
    }
}
