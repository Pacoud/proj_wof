
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalDetectionTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        DiplomacySystem Diplomacy,
        TacticalPerceptionState Perception,
        CompanyDeployment Observer,
        CompanyDeployment SecondObserver,
        CompanyDeployment Target,
        Division French,
        Division German);

    private static Division MakeDivision(
        string name,
        Country country,
        Province province,
        int companies)
    {
        var division = new Division(
            name, province,
            fuel: 100, ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: companies * 100,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Bataillon", BattalionType.Infantry);

        for (int i = 1; i <= companies; i++)
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

    private static Scenario CreateScenario(
        double targetX = 123,
        double targetY = 17,
        double observerFacing = 90,
        bool placeTarget = true)
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

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var field = new TacticalBattlefield();
        var sector = new BattleSector(
            1, "Secteur", TerrainType.Hills);

        field.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, field);

        var observer = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var secondObserver = battle.DeployCompany(
            french.Battalions[0].Companies[1],
            sector,
            CompanyDeploymentRole.FrontLine);

        var target = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);
        geometry.DefineSector(
            sector, 0, 0, radiusMeters: 500);

        geometry.PlaceCompany(
            observer,
            new TacticalPoint(0, 0, 100),
            observerFacing,
            frontageMeters: 40);

        if (placeTarget)
        {
            geometry.PlaceCompany(
                target,
                new TacticalPoint(targetX, targetY, 100),
                facingDegrees: 90,
                frontageMeters: 40);
        }

        return new Scenario(
            battle,
            geometry,
            diplomacy,
            new TacticalPerceptionState(battle),
            observer,
            secondObserver,
            target,
            french,
            german);
    }

    private static TacticalSignatureFrame Frame(
        Scenario s,
        TacticalActivity activity,
        TacticalConcealment concealment)
    {
        var frame = new TacticalSignatureFrame(s.Battle);

        frame.Set(
            s.Target,
            new TacticalUnitSignature(
                activity,
                concealment));

        return frame;
    }

    private static TacticalDetectionProfile Profile(
        double visual = 200,
        double acoustic = 300,
        double fov = 120)
    {
        return new TacticalDetectionProfile(
            maxVisualRangeMeters: visual,
            maxAcousticRangeMeters: acoustic,
            fieldOfViewDegrees: fov,
            localizationCellMeters: 50);
    }

    private static void Update(
        Scenario s,
        TacticalSignatureFrame frame,
        long hour,
        TacticalDetectionProfile? profile = null)
    {
        TacticalDetectionSystem.Update(
            s.Battle,
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            frame,
            hour,
            profile ?? Profile());
    }

    private static void AddRidge(Scenario s)
    {
        s.Geometry.AddLineObstacle(
            new TacticalLineObstacle(
                1,
                "Crête",
                TacticalLineObstacleKind.Ridge,
                50, -30,
                50, 30,
                topElevationMeters: 120));
    }

    [Fact]
    public void MissingSignature_DoesNotAssumeExposedUnit()
    {
        var s = CreateScenario();

        var emptyFrame = new TacticalSignatureFrame(
            s.Battle);

        Update(s, emptyFrame, hour: 1);

        Assert.Empty(s.Perception.GetReports(s.Observer));
    }

    [Fact]
    public void ConcealedStationaryUnit_CanBeRevealedByFiring()
    {
        var s = CreateScenario();

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Stationary,
                TacticalConcealment.Strong),
            hour: 1,
            profile: Profile(acoustic: 0));

        // Portee effective :
        // 200 * 1.0 * 0.25 = 50 m.
        Assert.Empty(s.Perception.GetReports(s.Observer));

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Firing,
                TacticalConcealment.Strong),
            hour: 2,
            profile: Profile(acoustic: 0));

        // Portee effective :
        // 200 * 2.5 * 0.25 = 125 m.
        var report = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(
            TacticalKnowledgeLevel.Located,
            report.CurrentObservationLevel);

        Assert.Equal(2L, report.LastLocatedHour);
    }

    [Fact]
    public void FiringBehindRidge_IsHeardButNotLocalized()
    {
        var s = CreateScenario();
        AddRidge(s);

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Firing,
                TacticalConcealment.None),
            hour: 1);

        var report = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(
            TacticalKnowledgeLevel.Suspected,
            report.BestKnownLevel);

        Assert.Null(report.LastKnownPosition);
        Assert.Null(report.LastLocatedHour);
        Assert.Null(report.EstimatedCompanyType);

        Assert.True(report.IsCurrentlyObserved);
    }

    [Fact]
    public void FiringOutsideView_IsAudibleWithoutVisualContact()
    {
        var s = CreateScenario(observerFacing: 270);

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Firing,
                TacticalConcealment.None),
            hour: 1);

        var report = Assert.Single(
            s.Perception.GetReports(s.Observer));

        // L'observateur regarde a l'ouest,
        // la cible se trouve a l'est.
        Assert.Equal(
            TacticalKnowledgeLevel.Suspected,
            report.CurrentObservationLevel);

        Assert.Null(report.LastKnownPosition);
    }

    [Fact]
    public void VisualObservation_DoesNotRevealExactCoordinates()
    {
        var s = CreateScenario();

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Stationary,
                TacticalConcealment.None),
            hour: 1);

        var report = Assert.Single(
            s.Perception.GetReports(s.Observer));

        var realPoint =
            s.Geometry.GetPosition(s.Target)!.Point;

        Assert.Equal(
            new TacticalPoint(123, 17, 100),
            realPoint);

        Assert.Equal(
            new TacticalPoint(100, 0, 100),
            report.LastKnownPosition);

        Assert.NotEqual(
            realPoint,
            report.LastKnownPosition);

        Assert.Equal(
            50.0,
            report.PositionUncertaintyMeters);

        Assert.Null(report.EstimatedCompanyType);
        Assert.Equal(1L, report.LastLocatedHour);
    }

    [Fact]
    public void VisualAndAcousticSignals_ProduceOnlyOneTrack()
    {
        var s = CreateScenario();

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Firing,
                TacticalConcealment.None),
            hour: 1);

        var reports =
            s.Perception.GetReports(s.Observer);

        var report = Assert.Single(reports);

        // La vision fournit une localisation.
        // Elle prend le pas sur la suspicion sonore.
        Assert.Equal(
            TacticalKnowledgeLevel.Located,
            report.CurrentObservationLevel);

        Assert.NotNull(report.LastKnownPosition);
    }

    [Fact]
    public void Movement_IncreasesVisualDetectability()
    {
        var s = CreateScenario(
            targetX: 170,
            targetY: 0);

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Stationary,
                TacticalConcealment.None),
            hour: 1,
            profile: Profile(
                visual: 150,
                acoustic: 0));

        Assert.Empty(s.Perception.GetReports(s.Observer));

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Moving,
                TacticalConcealment.None),
            hour: 2,
            profile: Profile(
                visual: 150,
                acoustic: 0));

        // Portee visuelle : 150 * 1.4 = 210 m.
        Assert.Single(
            s.Perception.GetReports(s.Observer));
    }

    [Fact]
    public void BeyondVisualAndAcousticRange_NoContactIsCreated()
    {
        var s = CreateScenario(
            targetX: 350,
            targetY: 0);

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Firing,
                TacticalConcealment.None),
            hour: 1,
            profile: Profile(
                visual: 100,
                acoustic: 200));

        // Vision : 100 * 2.5 = 250 m.
        // Audition : 200 m.
        // Distance reelle : 350 m.
        Assert.Empty(s.Perception.GetReports(s.Observer));
    }

    [Fact]
    public void SilentAfterFiring_MakesOldReportStale()
    {
        var s = CreateScenario();
        AddRidge(s);

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Firing,
                TacticalConcealment.Strong),
            hour: 1);

        var initial = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(
            TacticalKnowledgeLevel.Suspected,
            initial.BestKnownLevel);

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Stationary,
                TacticalConcealment.Strong),
            hour: 2);

        var stale = Assert.Single(
            s.Perception.GetReports(s.Observer));

        Assert.Equal(initial.TrackId, stale.TrackId);
        Assert.Equal(1L, stale.LastObservedHour);
        Assert.False(stale.IsCurrentlyObserved);
        Assert.Null(stale.CurrentObservationLevel);
    }

    [Fact]
    public void Observation_DoesNotSpreadOrDamageMilitaryUnits()
    {
        var s = CreateScenario();

        var frenchBefore =
            s.French.GetCurrentResourceTotals();

        var germanBefore =
            s.German.GetCurrentResourceTotals();

        Update(
            s,
            Frame(
                s,
                TacticalActivity.Firing,
                TacticalConcealment.None),
            hour: 1);

        Assert.Single(s.Perception.GetReports(s.Observer));

        // Le second observateur n'a pas de position
        // geometrique, ni de rapport automatiquement
        // partage par le premier.
        Assert.Empty(
            s.Perception.GetReports(s.SecondObserver));

        Assert.Equal(
            frenchBefore,
            s.French.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.German.GetCurrentResourceTotals());

        Assert.Equal(3, s.Battle.Deployments.Count);
    }

    [Fact]
    public void InvalidInputs_AreRejectedBeforeUpdatingMemory()
    {
        var s = CreateScenario();
        var other = CreateScenario();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalUnitSignature(
                (TacticalActivity)999,
                TacticalConcealment.None));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Profile(visual: -10));

        var foreignFrame = Frame(
            other,
            TacticalActivity.Firing,
            TacticalConcealment.None);

        Assert.Throws<InvalidOperationException>(() =>
            Update(s, foreignFrame, hour: 1));

        var localFrame = Frame(
            s,
            TacticalActivity.Stationary,
            TacticalConcealment.None);

        Assert.Throws<InvalidOperationException>(() =>
            localFrame.Set(
                other.Target,
                new TacticalUnitSignature(
                    TacticalActivity.Firing,
                    TacticalConcealment.None)));

        Assert.Empty(s.Perception.GetReports(s.Observer));

        // On verifie aussi la progression temporelle.
        Update(s, localFrame, hour: 5);

        Assert.Throws<InvalidOperationException>(() =>
            Update(s, localFrame, hour: 4));
    }
}
