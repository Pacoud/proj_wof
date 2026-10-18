
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalPerceptionTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        DiplomacySystem Diplomacy,
        TacticalPerceptionState Perception,
        Division French,
        Division German,
        BattleSector Sector)
    {
        public CompanyDeployment DeployFrench(int index = 0) =>
            Battle.DeployCompany(
                French.Battalions[0].Companies[index],
                Sector,
                CompanyDeploymentRole.FrontLine);

        public CompanyDeployment DeployGerman() =>
            Battle.DeployCompany(
                German.Battalions[0].Companies[0],
                Sector,
                CompanyDeploymentRole.FrontLine);

        public void Update(
            long hour,
            double range = 200,
            double fieldOfView = 120)
        {
            TacticalPerceptionSystem.Update(
                Battle,
                Geometry,
                Diplomacy,
                Perception,
                hour,
                new TacticalObservationProfile(
                    range, fieldOfView));
        }
    }

    private static Division MakeDivision(
        string name,
        Country country,
        Province province)
    {
        var division = new Division(
            name, province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: 200,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Bataillon",
            BattalionType.Infantry);

        for (int i = 1; i <= 2; i++)
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
        var province = new Province(1, "Test");

        var french = MakeDivision(
            "France", france, province);

        var german = MakeDivision(
            "Germany", germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var system = new EngagementSystem(diplomacy);

        system.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            Assert.Single(system.ActiveEngagements);

        var battlefield = new TacticalBattlefield();
        var sector = new BattleSector(
            1, "Secteur", TerrainType.Plains);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var geometry = new TacticalGeometryMap(battle);
        geometry.DefineSector(
            sector, 50, 0, radiusMeters: 400);

        return new Scenario(
            battle,
            geometry,
            diplomacy,
            new TacticalPerceptionState(battle),
            french,
            german,
            sector);
    }

    private static (
        CompanyDeployment French,
        CompanyDeployment German)
        PlacePair(
            Scenario s,
            double germanX = 100,
            double germanFacing = 90)
    {
        var french = s.DeployFrench();
        var german = s.DeployGerman();

        s.Geometry.PlaceCompany(
            french,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        s.Geometry.PlaceCompany(
            german,
            new TacticalPoint(germanX, 0, 100),
            facingDegrees: germanFacing,
            frontageMeters: 40);

        return (french, german);
    }

    [Fact]
    public void ForwardObservation_IsNotAutomaticallyReciprocal()
    {
        var s = CreateScenario();

        // Les deux compagnies regardent vers l'est.
        var (french, german) = PlacePair(s);

        s.Update(hour: 1);

        var report = Assert.Single(
            s.Perception.GetReports(french));

        Assert.Equal(
            new TacticalPoint(100, 0, 100),
            report.LastKnownPosition);

        Assert.Equal(s.Sector.Id, report.LastKnownSectorId);
        Assert.Equal(1L, report.LastObservedHour);
        Assert.True(report.IsCurrentlyObserved);

        // L'Allemand regarde dans la mauvaise direction.
        Assert.Empty(s.Perception.GetReports(german));

        // Le rapport public ne contient aucune
        // référence à un déploiement ennemi.
        Assert.DoesNotContain(
            typeof(TacticalEnemyContactReport).GetProperties(),
            property =>
                property.PropertyType == typeof(CompanyDeployment));
    }

    [Fact]
    public void FacingEachOther_AllowsIndependentObservations()
    {
        var s = CreateScenario();

        var (french, german) = PlacePair(
            s, germanFacing: 270);

        s.Update(hour: 1);

        var frenchReport = Assert.Single(
            s.Perception.GetReports(french));

        var germanReport = Assert.Single(
            s.Perception.GetReports(german));

        Assert.Equal(
            new TacticalPoint(100, 0, 100),
            frenchReport.LastKnownPosition);

        Assert.Equal(
            new TacticalPoint(0, 0, 100),
            germanReport.LastKnownPosition);

        Assert.NotEqual(
            frenchReport.TrackId,
            germanReport.TrackId);
    }

    [Fact]
    public void Ridge_PreventsObservation()
    {
        var s = CreateScenario();
        var (french, german) = PlacePair(
            s, germanFacing: 270);

        s.Geometry.AddLineObstacle(
            new TacticalLineObstacle(
                1, "Crête",
                TacticalLineObstacleKind.Ridge,
                50, -20,
                50, 20,
                topElevationMeters: 110));

        s.Update(hour: 1);

        Assert.Empty(s.Perception.GetReports(french));
        Assert.Empty(s.Perception.GetReports(german));
    }

    [Fact]
    public void RangeAndMissingPosition_DoNotCreateKnowledge()
    {
        var distant = CreateScenario();
        var (french, german) = PlacePair(
            distant,
            germanX: 300,
            germanFacing: 270);

        distant.Update(hour: 1, range: 200);

        Assert.Empty(distant.Perception.GetReports(french));
        Assert.Empty(distant.Perception.GetReports(german));

        var missing = CreateScenario();
        var observer = missing.DeployFrench();
        var target = missing.DeployGerman();

        missing.Geometry.PlaceCompany(
            observer,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        // La compagnie ennemie n'a pas de position.
        missing.Update(hour: 1);

        Assert.Empty(missing.Perception.GetReports(observer));
        Assert.Empty(missing.Perception.GetReports(target));
    }

    [Fact]
    public void LostContact_PreservesTrackAndLastObservation()
    {
        var s = CreateScenario();
        var (french, _) = PlacePair(s);

        s.Update(hour: 1, range: 200);

        var initial = Assert.Single(
            s.Perception.GetReports(french));

        // La portée est réduite au deuxième passage.
        s.Update(hour: 2, range: 50);

        var stale = Assert.Single(
            s.Perception.GetReports(french));

        Assert.Equal(initial.TrackId, stale.TrackId);
        Assert.Equal(1L, stale.LastObservedHour);
        Assert.Equal(
            initial.LastKnownPosition,
            stale.LastKnownPosition);

        Assert.False(stale.IsCurrentlyObserved);

        // Le premier rapport était une copie.
        Assert.True(initial.IsCurrentlyObserved);

        // Le contact est retrouvé.
        s.Update(hour: 3, range: 200);

        var reacquired = Assert.Single(
            s.Perception.GetReports(french));

        Assert.Equal(initial.TrackId, reacquired.TrackId);
        Assert.Equal(3L, reacquired.LastObservedHour);
        Assert.True(reacquired.IsCurrentlyObserved);
    }

    [Fact]
    public void Peace_MakesExistingTracksStale()
    {
        var s = CreateScenario();
        var (french, _) = PlacePair(s);

        s.Update(hour: 1);
        Assert.Single(s.Perception.GetReports(french));

        Assert.True(s.Diplomacy.MakePeace(
            s.French.Country!,
            s.German.Country!));

        s.Update(hour: 2);

        var report = Assert.Single(
            s.Perception.GetReports(french));

        Assert.False(report.IsCurrentlyObserved);
        Assert.Equal(1L, report.LastObservedHour);
    }

    [Fact]
    public void Observers_DoNotShareReportsAutomatically()
    {
        var s = CreateScenario();

        var first = s.DeployFrench(index: 0);
        var second = s.DeployFrench(index: 1);
        var german = s.DeployGerman();

        s.Geometry.PlaceCompany(
            first,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        s.Geometry.PlaceCompany(
            second,
            new TacticalPoint(0, 10, 100),
            facingDegrees: 270,
            frontageMeters: 40);

        s.Geometry.PlaceCompany(
            german,
            new TacticalPoint(100, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        s.Update(hour: 1);

        Assert.Single(s.Perception.GetReports(first));

        // Deuxième compagnie : dirigée vers l'ouest.
        // Elle ne bénéficie pas du rapport de la première.
        Assert.Empty(s.Perception.GetReports(second));
    }

    [Fact]
    public void ForeignBattleAndBackwardsTime_AreRejected()
    {
        var s = CreateScenario();
        var other = CreateScenario();

        var (french, _) = PlacePair(s);
        s.Update(hour: 5);

        var before = Assert.Single(
            s.Perception.GetReports(french));

        Assert.Throws<InvalidOperationException>(() =>
            TacticalPerceptionSystem.Update(
                s.Battle,
                s.Geometry,
                s.Diplomacy,
                other.Perception,
                currentHour: 6,
                new TacticalObservationProfile(200, 120)));

        Assert.Throws<InvalidOperationException>(() =>
            s.Update(hour: 4));

        Assert.Equal(
            before,
            Assert.Single(s.Perception.GetReports(french)));
    }

    [Fact]
    public void RepeatedObservation_DoesNotDuplicateOrDamageUnits()
    {
        var s = CreateScenario();
        var (french, german) = PlacePair(s);

        var frenchBefore =
            s.French.GetCurrentResourceTotals();

        var germanBefore =
            s.German.GetCurrentResourceTotals();

        s.Update(hour: 1);

        var initial = Assert.Single(
            s.Perception.GetReports(french));

        s.Update(hour: 1);

        var repeated = Assert.Single(
            s.Perception.GetReports(french));

        Assert.Equal(initial.TrackId, repeated.TrackId);
        Assert.Equal(initial.LastObservedHour,
            repeated.LastObservedHour);

        Assert.Equal(
            frenchBefore,
            s.French.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.German.GetCurrentResourceTotals());

        Assert.Single(s.Perception.GetReports(french));
        Assert.Empty(s.Perception.GetReports(german));
        Assert.Equal(2, s.Battle.Deployments.Count);
    }
}
