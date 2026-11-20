
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

    
    private static (
        Scenario S,
        CompanyDeployment A,
        CompanyDeployment B,
        CompanyDeployment Enemy)
        CreateIntelligenceScenario()
    {
        var s = CreateScenario();

        var a = s.DeployFrench(0);
        var b = s.DeployFrench(1);
        var enemy = s.DeployGerman();

        return (s, a, b, enemy);
    }

    private static TacticalObservationEvent Located(
        CompanyDeployment observer,
        CompanyDeployment target,
        BattleSector sector,
        double x = 100)
    {
        return new TacticalObservationEvent(
            observer,
            target,
            new TacticalPoint(x, 0, 100),
            sector.Id,
            TacticalKnowledgeLevel.Located,
            PositionUncertaintyMeters: 10);
    }

    private static TacticalObservationEvent Suspected(
        CompanyDeployment observer,
        CompanyDeployment target,
        BattleSector sector)
    {
        return new TacticalObservationEvent(
            observer,
            target,
            ObservedPosition: null,
            ObservedSectorId: sector.Id,
            KnowledgeLevel: TacticalKnowledgeLevel.Suspected,
            PositionUncertaintyMeters: null);
    }

    private static TacticalBattalionIntelligenceSnapshot
        IntelligenceSnapshot(
            Scenario s,
            long hour,
            long maxAgeHours = 0)
    {
        return new TacticalBattalionIntelligenceSystem(
            s.Battle,
            s.Perception).Capture(
                s.French.Battalions[0],
                hour,
                maxAgeHours);
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



    [Fact]
    public void SectorIdLookupReflectsBattlefieldUpdates()
    {
        var field = new TacticalBattlefield();

        var first = new BattleSector(
            1, "Premier", TerrainType.Plains);

        var second = new BattleSector(
            2, "Second", TerrainType.Forest);

        Assert.False(field.ContainsSectorId(1));
        Assert.False(field.ContainsSectorId(2));

        field.AddSector(first);

        Assert.True(field.ContainsSectorId(1));
        Assert.False(field.ContainsSectorId(2));

        field.AddSector(second);

        Assert.True(field.ContainsSectorId(1));
        Assert.True(field.ContainsSectorId(2));
        Assert.False(field.ContainsSectorId(999));
    }


    [Fact]
    public void InvalidSectorObservationDoesNotMutateTracks()
    {
        var s = CreateScenario();
        var (french, german) = PlacePair(s);

        var valid = new TacticalObservationEvent(
            french,
            german,
            new TacticalPoint(100, 0, 100),
            s.Sector.Id);

        s.Perception.Apply(1, [valid]);

        var before = Assert.Single(
            s.Perception.GetReports(french));

        var invalid = valid with
        {
            ObservedSectorId = 999
        };

        Assert.Throws<InvalidOperationException>(() =>
            s.Perception.Apply(
                2,
                [valid, invalid]));

        var after = Assert.Single(
            s.Perception.GetReports(french));

        Assert.Equal(before, after);
    }



    [Fact]
    public void MultipleObservationsPreserveTracksAndAtomicity()
    {
        var s = CreateScenario();

        var frenchA = s.DeployFrench(0);
        var frenchB = s.DeployFrench(1);
        var germanA = s.DeployGerman();

        var germanB = s.Battle.DeployCompany(
            s.German.Battalions[0].Companies[1],
            s.Sector,
            CompanyDeploymentRole.FrontLine);

        TacticalObservationEvent[] observations =
        [
            new(frenchA, germanA,
                new TacticalPoint(100, 0, 100), s.Sector.Id),

            new(frenchA, germanB,
                new TacticalPoint(110, 0, 100), s.Sector.Id),

            new(frenchB, germanA,
                new TacticalPoint(100, 0, 100), s.Sector.Id),

            new(frenchB, germanB,
                new TacticalPoint(110, 0, 100), s.Sector.Id)
        ];

        // Quatre paires distinctes.
        s.Perception.Apply(1, observations);

        var beforeA = s.Perception.GetReports(frenchA).ToArray();
        var beforeB = s.Perception.GetReports(frenchB).ToArray();

        Assert.Equal(2, beforeA.Length);
        Assert.Equal(2, beforeB.Length);

        // La deuxieme passe contient un doublon.
        // L'etat ne doit pas etre partiellement modifie.
        Assert.Throws<InvalidOperationException>(() =>
            s.Perception.Apply(
                2,
                [observations[0], observations[0]]));

        Assert.Equal(
            beforeA,
            s.Perception.GetReports(frenchA).ToArray());

        Assert.Equal(
            beforeB,
            s.Perception.GetReports(frenchB).ToArray());

        // La passe valide doit reutiliser les pistes.
        s.Perception.Apply(2, observations);

        var afterA = s.Perception.GetReports(frenchA);
        var afterB = s.Perception.GetReports(frenchB);

        Assert.Equal(
            beforeA.Select(r => r.TrackId),
            afterA.Select(r => r.TrackId));

        Assert.Equal(
            beforeB.Select(r => r.TrackId),
            afterB.Select(r => r.TrackId));

        Assert.All(
            afterA,
            r => Assert.Equal(2L, r.LastObservedHour));

        Assert.All(
            afterB,
            r => Assert.Equal(2L, r.LastObservedHour));
    }




    [Fact]
    public void BattalionReceivesCurrentCompanyObservation()
    {
        var (s, a, _, enemy) = CreateIntelligenceScenario();

        s.Perception.Apply(
            1,
            new[] { Located(a, enemy, s.Sector) });

        var snapshot = IntelligenceSnapshot(s, 1);
        var contact = Assert.Single(snapshot.Contacts);

        Assert.Equal(a.Company.Id, contact.ObserverCompanyId);
        Assert.Equal(0, contact.AgeHours);

        Assert.Equal(
            TacticalBattalionContactStatus.CurrentLocalized,
            contact.Status);

        Assert.Equal(s.Sector.Id,
            contact.Contact.LastKnownSectorId);

        Assert.Equal(
            new TacticalPoint(100, 0, 100),
            contact.Contact.LastKnownPosition);
    }

    [Fact]
    public void TwoObserversKeepIndependentTracks()
    {
        var (s, a, b, enemy) = CreateIntelligenceScenario();

        s.Perception.Apply(
            1,
            new[]
            {
                Located(a, enemy, s.Sector, 100),
                Located(b, enemy, s.Sector, 110)
            });

        var snapshot = IntelligenceSnapshot(s, 1);

        Assert.Equal(2, snapshot.Contacts.Count);

        Assert.NotEqual(
            snapshot.Contacts[0].ObserverCompanyId,
            snapshot.Contacts[1].ObserverCompanyId);

        Assert.NotEqual(
            snapshot.Contacts[0].Contact.TrackId,
            snapshot.Contacts[1].Contact.TrackId);

        // Deux pistes ne prouvent pas la presence
        // de deux compagnies ennemies distinctes.
    }

    [Fact]
    public void CurrentSuspicionDoesNotRefreshOldLocalization()
    {
        var (s, a, _, enemy) = CreateIntelligenceScenario();

        s.Perception.Apply(
            1,
            new[] { Located(a, enemy, s.Sector) });

        s.Perception.Apply(
            2,
            new[] { Suspected(a, enemy, s.Sector) });

        var contact = Assert.Single(
            IntelligenceSnapshot(s, 2).Contacts);

        Assert.Equal(
            TacticalBattalionContactStatus.CurrentSuspected,
            contact.Status);

        Assert.Equal(2, contact.Contact.LastObservedHour);
        Assert.Equal(1L, contact.Contact.LastLocatedHour);

        // La position est encore memorisee,
        // mais elle date de H1, pas de H2.
        Assert.Equal(
            new TacticalPoint(100, 0, 100),
            contact.Contact.LastKnownPosition);

        Assert.Equal(
            TacticalKnowledgeLevel.Suspected,
            contact.Contact.CurrentObservationLevel);
    }

    [Fact]
    public void OldContactIsMarkedStaleAndExpires()
    {
        var (s, a, _, enemy) = CreateIntelligenceScenario();

        s.Perception.Apply(
            1,
            new[] { Located(a, enemy, s.Sector) });

        s.Perception.Apply(
            2,
            Array.Empty<TacticalObservationEvent>());

        var retained = Assert.Single(
            IntelligenceSnapshot(
                s, 2, maxAgeHours: 1).Contacts);

        Assert.Equal(
            TacticalBattalionContactStatus.Stale,
            retained.Status);

        Assert.Equal(1, retained.AgeHours);
        Assert.False(retained.Contact.IsCurrentlyObserved);

        Assert.Empty(
            IntelligenceSnapshot(
                s, 2, maxAgeHours: 0).Contacts);
    }

    [Fact]
    public void IntelligenceSnapshotRemainsFrozenAfterNextHour()
    {
        var (s, a, _, enemy) = CreateIntelligenceScenario();

        s.Perception.Apply(
            1,
            new[] { Located(a, enemy, s.Sector) });

        var original = IntelligenceSnapshot(s, 1);
        var firstContact = Assert.Single(original.Contacts);

        s.Perception.Apply(
            2,
            Array.Empty<TacticalObservationEvent>());

        var later = IntelligenceSnapshot(
            s, 2, maxAgeHours: 1);

        Assert.Equal(
            TacticalBattalionContactStatus.CurrentLocalized,
            firstContact.Status);

        Assert.True(firstContact.Contact.IsCurrentlyObserved);

        Assert.Equal(
            TacticalBattalionContactStatus.Stale,
            Assert.Single(later.Contacts).Status);

        // Impossible de recalculer H1 a partir
        // de la memoire de perception de H2.
        Assert.Throws<InvalidOperationException>(() =>
            IntelligenceSnapshot(s, 1));
    }

    [Fact]
    public void NoObservationProducesNoInventedEnemyContact()
    {
        var (s, _, _, _) = CreateIntelligenceScenario();

        s.Perception.Apply(
            1,
            Array.Empty<TacticalObservationEvent>());

        var snapshot = IntelligenceSnapshot(s, 1);

        Assert.Empty(snapshot.Contacts);

        // Aucun contact connu ne signifie pas
        // que le secteur est physiquement vide.
    }

    [Fact]
    public void EnemyObserversDoNotFeedFriendlyBattalion()
    {
        var (s, a, _, enemy) = CreateIntelligenceScenario();

        // Ici, seul l'ennemi observe une compagnie amie.
        s.Perception.Apply(
            1,
            new[] { Located(enemy, a, s.Sector) });

        var frenchIntelligence = IntelligenceSnapshot(s, 1);

        Assert.Empty(frenchIntelligence.Contacts);
        Assert.Single(s.Perception.GetReports(enemy));
    }

    [Fact]
    public void IntelligenceRejectsWrongHourAndForeignBattalion()
    {
        var (s, a, _, enemy) = CreateIntelligenceScenario();

        var system = new TacticalBattalionIntelligenceSystem(
            s.Battle,
            s.Perception);

        Assert.Throws<InvalidOperationException>(() =>
            IntelligenceSnapshot(s, 1));

        s.Perception.Apply(
            1,
            new[] { Located(a, enemy, s.Sector) });

        Assert.Throws<InvalidOperationException>(() =>
            IntelligenceSnapshot(s, 2));

        var foreign = new Battalion(
            99, "Foreign Battalion", BattalionType.Infantry);

        Assert.Throws<InvalidOperationException>(() =>
            system.Capture(foreign, 1, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            system.Capture(s.French.Battalions[0], 1, -1));
    }

    [Fact]
    public void SharingIntelligenceDoesNotMutateMilitaryState()
    {
        var (s, a, _, enemy) = CreateIntelligenceScenario();

        s.Perception.Apply(
            1,
            new[] { Located(a, enemy, s.Sector) });

        var resourcesBefore =
            s.French.GetCurrentResourceTotals();

        double cohesionBefore = a.Company.Cohesion;
        long revisionBefore = s.Battle.DeploymentRevision;

        var first = IntelligenceSnapshot(s, 1);
        var second = IntelligenceSnapshot(s, 1);

        Assert.Equal(
            first.Contacts.ToArray(),
            second.Contacts.ToArray());

        Assert.Equal(
            resourcesBefore,
            s.French.GetCurrentResourceTotals());

        Assert.Equal(
            cohesionBefore,
            a.Company.Cohesion,
            6);

        Assert.Equal(
            revisionBefore,
            s.Battle.DeploymentRevision);

        Assert.Equal(1L, s.Perception.LastUpdateHour);
    }


    [Fact]
    public void BattalionIntelligenceExposesOnlyFriendlyMetadata()
    {
        var (s, a, _, enemy) =
            CreateIntelligenceScenario();

        s.Perception.Apply(
            1,
            new[] { Located(a, enemy, s.Sector) });

        var snapshot = IntelligenceSnapshot(s, 1);

        var battalion = s.French.Battalions[0];

        Assert.Equal(
            battalion.Id,
            snapshot.BattalionId);

        Assert.Equal(
            battalion.Name,
            snapshot.BattalionName);

        Assert.Equal(
            s.French.Name,
            snapshot.FriendlyDivisionName);

        Assert.Equal(1, snapshot.Hour);
        Assert.Single(snapshot.Contacts);

        // L'ancien acces direct a l'objet Battalion
        // ne doit plus exister dans le contrat.
        Assert.Null(
            typeof(TacticalBattalionIntelligenceSnapshot)
                .GetProperty("Battalion"));
    }

    [Fact]
    public void IntelligenceDtoGraphContainsOnlyValueTypes()
    {
        static bool IsSafeType(Type type)
        {
            var actual =
                Nullable.GetUnderlyingType(type) ?? type;

            return actual.IsPrimitive
                || actual.IsEnum
                || actual == typeof(string)
                || actual == typeof(TacticalPoint)
                || actual ==
                    typeof(TacticalEnemyContactReport)
                || actual ==
                    typeof(IReadOnlyList<
                        TacticalBattalionContactReport>);
        }

        var dtoTypes = new[]
        {
            typeof(TacticalBattalionIntelligenceSnapshot),
            typeof(TacticalBattalionContactReport),
            typeof(TacticalEnemyContactReport),
            typeof(TacticalPoint)
        };

        foreach (var dto in dtoTypes)
        {
            foreach (var property in dto.GetProperties())
            {
                Assert.True(
                    IsSafeType(property.PropertyType),
                    $"{dto.Name}.{property.Name} exposes " +
                    $"un type non autorise : " +
                    $"{property.PropertyType.Name}");
            }
        }
    }

}
