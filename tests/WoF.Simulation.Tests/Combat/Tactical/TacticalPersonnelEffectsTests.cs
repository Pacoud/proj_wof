
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalPersonnelEffectTests
{
    private static readonly TacticalHitProfile HitProfile =
        new(2, 1, 0.6);

    private static readonly TacticalDefenseProfile DefenseProfile =
        new(1, 0.6, 0.3, 0.1, 0.5, 0.7);

    private static readonly TacticalPersonnelEffectProfile EffectProfile =
        new(
            killedProbability: 0.1,
            woundedUnavailableProbability: 0.4);

    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalPerceptionState Perception,
        DiplomacySystem Diplomacy,
        CompanyDeployment Shooter,
        CompanyDeployment? SecondShooter,
        CompanyDeployment Target,
        Division French,
        Division German,
        BattleSector Sector);

    private static Division MakeDivision(
        Country country,
        Province province,
        int battalionCount)
    {
        var division = new Division(
            $"{country.Name} Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: battalionCount * 100,
                infantryEquipment: battalionCount * 100,
                artillery: 0,
                tanks: 0));

        for (int i = 1; i <= battalionCount; i++)
        {
            var battalion = new Battalion(
                i,
                $"Battalion {i}",
                BattalionType.Infantry);

            battalion.AddCompany(new Company(
                1,
                $"Company {i}",
                CompanyType.Infantry,
                new CompanyComposition(
                    manpower: 100,
                    infantryEquipment: 100,
                    ammunition: new AmmunitionTotals(
                        smallArms: 1000))));

            division.AddBattalion(battalion);
        }

        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario(
        int exposed = 80,
        bool secondShooter = false)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battle");

        var french = MakeDivision(
            france, province, secondShooter ? 2 : 1);

        var german = MakeDivision(germany, province, 1);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);
        engagements.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var battlefield = new TacticalBattlefield();

        var sector = new BattleSector(
            1, "Main", TerrainType.Plains,
            features: SectorFeature.Buildings);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var shooter = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        CompanyDeployment? second = null;

        if (secondShooter)
        {
            second = battle.DeployCompany(
                french.Battalions[1].Companies[0],
                sector,
                CompanyDeploymentRole.FrontLine);
        }

        var target = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);

        geometry.DefineSector(
            sector, 100, 0, radiusMeters: 500);

        geometry.PlaceCompany(
            shooter,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        if (second is not null)
        {
            geometry.PlaceCompany(
                second,
                new TacticalPoint(0, 10, 100),
                facingDegrees: 90,
                frontageMeters: 40);
        }

        geometry.PlaceCompany(
            target,
            new TacticalPoint(100, 0, 100),
            facingDegrees: 270,
            frontageMeters: 40);

        var perception = new TacticalPerceptionState(battle);

        var observations = new List<TacticalObservationEvent>
        {
            new(
                shooter,
                target,
                new TacticalPoint(100, 0, 100),
                sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 10)
        };

        if (second is not null)
        {
            observations.Add(new TacticalObservationEvent(
                second,
                target,
                new TacticalPoint(100, 0, 100),
                sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 10));
        }

        perception.Apply(1, observations);

        battle.PersonnelExposure.Set(
            target,
            new TacticalPersonnelExposure(
                exposed,
                100 - exposed,
                0,
                exposedDepthMeters: 30));

        return new Scenario(
            battle, geometry, perception, diplomacy,
            shooter, second, target, french, german, sector);
    }

    private static void Fire(Scenario s, long hour = 1)
    {
        var orders = new List<TacticalFireOrder>
        {
            new(
                s.Shooter,
                s.Target,
                new TacticalDirectFireProfile(250),
                WeaponsCommitted: 10,
                RoundsPerWeapon: 3)
        };

        if (s.SecondShooter is not null)
        {
            orders.Add(new TacticalFireOrder(
                s.SecondShooter,
                s.Target,
                new TacticalDirectFireProfile(250),
                WeaponsCommitted: 10,
                RoundsPerWeapon: 3));
        }

        var result = s.Battle.DirectFire.Execute(
            orders,
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            hour);

        Assert.Equal(orders.Count, result.Events.Count);
    }

    private static TacticalPersonnelContactPhaseResult Contacts(
        Scenario s,
        ulong seed = 42)
    {
        return s.Battle.DirectFire.ResolvePersonnelLastPhase(
            HitProfile,
            DefenseProfile,
            seed);
    }

    private static TacticalPersonnelEffectsPhaseResult Effects(
        Scenario s,
        ulong seed = 123)
    {
        return s.Battle.DirectFire.ResolvePersonnelEffectsLastPhase(
            EffectProfile,
            seed);
    }

    [Fact]
    public void NoContacts_ProduceNoAffectedPeople()
    {
        Assert.Equal(
            0,
            TacticalPersonnelEffectResolver.SampleDistinct(
                100, 0, seed: 1));

        Assert.Equal(
            0,
            TacticalPersonnelEffectResolver.SampleDistinct(
                0, 0, seed: 1));

        Assert.Throws<InvalidOperationException>(() =>
            TacticalPersonnelEffectResolver.SampleDistinct(
                0, 10, seed: 1));
    }

    [Fact]
    public void OneContact_ReachesExactlyOnePerson()
    {
        for (ulong seed = 0; seed < 100; seed++)
        {
            Assert.Equal(
                1,
                TacticalPersonnelEffectResolver.SampleDistinct(
                    100, 1, seed));
        }
    }

    [Fact]
    public void RepeatedContacts_CannotExceedExposedPopulation()
    {
        // Un million de contacts, mais seulement
        // 20 combattants possibles.
        int distinct =
            TacticalPersonnelEffectResolver.SampleDistinct(
                exposed: 20,
                contacts: 1_000_000,
                seed: 5);

        Assert.Equal(20, distinct);
    }

    [Fact]
    public void SampledDistinct_AgreesWithExpectedValue()
    {
        const int exposed = 20;
        const int contacts = 20;
        const int repetitions = 1000;

        double total = 0;

        for (ulong seed = 1; seed <= repetitions; seed++)
        {
            total += TacticalPersonnelEffectResolver.SampleDistinct(
                exposed,
                contacts,
                seed);
        }

        double average = total / repetitions;

        double expected =
            TacticalPersonnelContactResolver.ExpectedDistinct(
                exposed,
                contacts);

        // Le tirage entier doit converger vers
        // l'esperance deja utilisee en C2.4c.4.
        Assert.InRange(
            Math.Abs(average - expected),
            0,
            0.4);
    }

    [Fact]
    public void GuaranteedFatalProfile_KillsAllDistinctStruck()
    {
        var s = CreateScenario();

        var summary = new TacticalPersonnelTargetSummary(
            s.Target,
            ExposedPersonnel: 10,
            ContactEvents: 100,
            ExpectedDistinctPersonnel: 10);

        var profile = new TacticalPersonnelEffectProfile(
            killedProbability: 1,
            woundedUnavailableProbability: 0);

        var result = TacticalPersonnelEffectResolver.Resolve(
            summary,
            profile,
            seed: 10);

        Assert.Equal(10, result.DistinctStruck);
        Assert.Equal(10, result.Killed);
        Assert.Equal(0, result.WoundedUnavailable);
        Assert.Equal(0, result.NoLastingIncapacity);
    }

    [Fact]
    public void GuaranteedWoundedProfile_WoundsAllDistinctStruck()
    {
        var s = CreateScenario();

        var summary = new TacticalPersonnelTargetSummary(
            s.Target,
            ExposedPersonnel: 10,
            ContactEvents: 100,
            ExpectedDistinctPersonnel: 10);

        var profile = new TacticalPersonnelEffectProfile(
            killedProbability: 0,
            woundedUnavailableProbability: 1);

        var result = TacticalPersonnelEffectResolver.Resolve(
            summary,
            profile,
            seed: 11);

        Assert.Equal(10, result.DistinctStruck);
        Assert.Equal(0, result.Killed);
        Assert.Equal(10, result.WoundedUnavailable);
        Assert.Equal(0, result.NoLastingIncapacity);
    }

    [Fact]
    public void NonDisablingProfile_DoesNotCreateCasualties()
    {
        var s = CreateScenario();

        var summary = new TacticalPersonnelTargetSummary(
            s.Target,
            ExposedPersonnel: 10,
            ContactEvents: 100,
            ExpectedDistinctPersonnel: 10);

        var result = TacticalPersonnelEffectResolver.Resolve(
            summary,
            new TacticalPersonnelEffectProfile(0, 0),
            seed: 12);

        Assert.Equal(10, result.DistinctStruck);
        Assert.Equal(0, result.TotalOperationalCasualties);
        Assert.Equal(10, result.NoLastingIncapacity);
    }

    [Fact]
    public void MixedConsequences_ConserveDistinctPopulation()
    {
        var s = CreateScenario();

        var summary = new TacticalPersonnelTargetSummary(
            s.Target,
            ExposedPersonnel: 30,
            ContactEvents: 50,
            ExpectedDistinctPersonnel: 0);

        var result = TacticalPersonnelEffectResolver.Resolve(
            summary,
            EffectProfile,
            seed: 42);

        Assert.InRange(result.DistinctStruck, 0, 30);

        Assert.Equal(
            result.DistinctStruck,
            result.Killed
            + result.WoundedUnavailable
            + result.NoLastingIncapacity);

        Assert.InRange(
            result.TotalOperationalCasualties,
            0,
            result.DistinctStruck);
    }

    [Fact]
    public void TwoShooters_CreateOnlyOneCompanyEffectReport()
    {
        var s = CreateScenario(secondShooter: true);

        Fire(s);

        var contacts = Contacts(s);
        Assert.Equal(2, contacts.Events.Count);
        Assert.Single(contacts.Targets);

        var effects = Effects(s);

        var result = Assert.Single(effects.Targets);

        Assert.Same(s.Target, result.Target);
        Assert.Equal(
            Assert.Single(contacts.Targets).ContactEvents,
            result.ContactEvents);

        Assert.True(result.DistinctStruck <= result.ExposedPersonnel);
    }

    [Fact]
    public void Effects_AreReproducibleWithSameSeed()
    {
        var first = CreateScenario(secondShooter: true);
        var second = CreateScenario(secondShooter: true);

        Fire(first);
        Fire(second);

        Contacts(first, seed: 2026);
        Contacts(second, seed: 2026);

        var a = Assert.Single(Effects(first, seed: 456).Targets);
        var b = Assert.Single(Effects(second, seed: 456).Targets);

        Assert.Equal(a.ContactEvents, b.ContactEvents);
        Assert.Equal(a.DistinctStruck, b.DistinctStruck);
        Assert.Equal(a.Killed, b.Killed);
        Assert.Equal(a.WoundedUnavailable, b.WoundedUnavailable);
        Assert.Equal(a.NoLastingIncapacity, b.NoLastingIncapacity);
    }

    [Fact]
    public void LaterDefensiveChanges_DoNotRewriteEffects()
    {
        var unchanged = CreateScenario();
        var modified = CreateScenario();

        Fire(unchanged);
        Fire(modified);

        Contacts(unchanged, seed: 3);
        Contacts(modified, seed: 3);

        // Changement apres les contacts.
        modified.Battle.PersonnelExposure.Set(
            modified.Target,
            new TacticalPersonnelExposure(
                exposed: 5,
                sheltered: 95,
                reserve: 0,
                exposedDepthMeters: 30));

        modified.Battle.DefensiveState.Set(
            modified.Target,
            new TacticalDefensiveDisposition(
                TacticalStance.Prone,
                TacticalCover.Building));

        modified.Perception.Apply(
            2,
            Array.Empty<TacticalObservationEvent>());

        var a = Assert.Single(Effects(unchanged, 7).Targets);
        var b = Assert.Single(Effects(modified, 7).Targets);

        Assert.Equal(a.DistinctStruck, b.DistinctStruck);
        Assert.Equal(a.Killed, b.Killed);
        Assert.Equal(a.WoundedUnavailable, b.WoundedUnavailable);
    }

    [Fact]
    public void Effects_DoNotChangeRealMilitaryResources()
    {
        var s = CreateScenario(secondShooter: true);

        Fire(s);
        Contacts(s);

        var frenchBefore = s.French.GetCurrentResourceTotals();
        var germanBefore = s.German.GetCurrentResourceTotals();

        Effects(s);

        Assert.Equal(
            frenchBefore,
            s.French.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.German.GetCurrentResourceTotals());

        // Les deux tireurs ont consomme 30 cartouches.
        Assert.Equal(1940, frenchBefore.Ammunition.SmallArms);
        Assert.Equal(100, germanBefore.Personnel.Available);
    }

    [Fact]
    public void Effects_RequireContacts_AndCanRunOnlyOnce()
    {
        var s = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            Effects(s));

        Fire(s);

        Assert.Throws<InvalidOperationException>(() =>
            Effects(s));

        Contacts(s);
        Effects(s);

        Assert.Throws<InvalidOperationException>(() =>
            Effects(s));
    }

    [Fact]
    public void NewFirePhase_AllowsNewEffectResolution()
    {
        var s = CreateScenario();

        Fire(s, hour: 1);
        Contacts(s);
        Effects(s);

        s.Perception.Apply(2, new[]
        {
            new TacticalObservationEvent(
                s.Shooter,
                s.Target,
                new TacticalPoint(100, 0, 100),
                s.Sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 10)
        });

        Fire(s, hour: 2);
        Contacts(s, seed: 44);

        var next = Effects(s, seed: 55);

        Assert.Equal(2, next.Hour);
        Assert.Single(next.Targets);

        Assert.Equal(
            940,
            s.French.GetCurrentResourceTotals()
                .Ammunition.SmallArms);
    }

    [Fact]
    public void InvalidEffectProbabilities_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalPersonnelEffectProfile(-0.1, 0.2));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalPersonnelEffectProfile(0.2, double.NaN));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalPersonnelEffectProfile(0.8, 0.5));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TacticalPersonnelEffectResolver.SampleDistinct(
                -1, 5, seed: 1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TacticalPersonnelEffectResolver.SampleDistinct(
                10, -1, seed: 1));
    }

    /// <summary>
    /// Prepare une vraie phase avec de nombreux contacts.
    /// Les parametres tres eleves servent exclusivement
    /// a rendre les assertions de pertes robustes.
    /// </summary>
    private static TacticalPersonnelEffectsPhaseResult
        PrepareStrongEffects(
            Scenario scenario,
            double killedProbability,
            double woundedProbability)
    {
        Fire(scenario);

        scenario.Battle.DirectFire.ResolvePersonnelLastPhase(
            new TacticalHitProfile(
                minimumDispersionMeters: 0.1,
                dispersionAt100Meters: 0,
                effectiveTargetRadiusMeters: 30),
            new TacticalDefenseProfile(
                standingExposureFactor: 1,
                crouchedExposureFactor: 1,
                proneExposureFactor: 1,
                vegetationInterceptionFraction: 0,
                buildingInterceptionFraction: 0,
                entrenchmentInterceptionFraction: 0),
            seed: 123);

        return scenario.Battle.DirectFire
            .ResolvePersonnelEffectsLastPhase(
                new TacticalPersonnelEffectProfile(
                    killedProbability,
                    woundedProbability),
                seed: 456);
    }

    [Fact]
    public void AppliedCasualties_UpdateCompanyBattalionAndDivision()
    {
        var s = CreateScenario(
            exposed: 80,
            secondShooter: true);

        var effects = PrepareStrongEffects(
            s,
            killedProbability: 0.25,
            woundedProbability: 0.50);

        var expected = Assert.Single(effects.Targets);

        Assert.True(expected.TotalOperationalCasualties > 0);

        var applied =
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase();

        var report = Assert.Single(applied.Companies);

        var personnel = s.Target.Company.Composition!.Manpower;

        Assert.Same(s.Target, report.Target);
        Assert.Equal(100, report.AvailableBefore);

        Assert.Equal(expected.Killed,
            report.Casualties.KilledInAction);

        Assert.Equal(expected.WoundedUnavailable,
            report.Casualties.WoundedInAction);

        Assert.Equal(
            100 - expected.TotalOperationalCasualties,
            report.AvailableAfter);

        Assert.Equal(report.AvailableAfter, personnel.Current);

        Assert.Equal(expected.Killed, personnel.KilledInAction);
        Assert.Equal(
            expected.WoundedUnavailable,
            personnel.WoundedUnavailable);

        // Remontee sans aucune seconde repartition.
        var battalion = s.German.Battalions[0]
            .GetResourceTotals().Personnel;

        var division = s.German
            .GetCurrentResourceTotals().Personnel;

        Assert.Equal(personnel.Current, battalion.Available);
        Assert.Equal(personnel.Current, division.Available);

        Assert.Equal(expected.Killed, division.KilledInAction);
        Assert.Equal(
            expected.WoundedUnavailable,
            division.WoundedUnavailable);

        // Les deux compagnies francaises n'ont subi
        // aucune perte du fait de leur propre tir.
        Assert.Equal(
            200,
            s.French.GetCurrentResourceTotals()
                .Personnel.Available);

        Assert.Equal(
            1940,
            s.French.GetCurrentResourceTotals()
                .Ammunition.SmallArms);
    }

    [Fact]
    public void SameCasualtyPhase_CannotBeAppliedTwice()
    {
        var s = CreateScenario(exposed: 80);

        PrepareStrongEffects(
            s,
            killedProbability: 1,
            woundedProbability: 0);

        var first =
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase();

        int afterFirst =
            s.Target.Company.Composition!.Manpower.Current;

        Assert.True(Assert.Single(first.Companies)
            .Casualties.Total > 0);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase());

        Assert.Equal(
            afterFirst,
            s.Target.Company.Composition!.Manpower.Current);
    }

    [Fact]
    public void ZeroCasualties_StillCompleteAnApplication()
    {
        var s = CreateScenario(exposed: 0);

        Fire(s);
        Contacts(s);
        Effects(s);

        var applied =
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase();

        var report = Assert.Single(applied.Companies);

        Assert.Equal(0, report.Casualties.Total);
        Assert.Equal(100, report.AvailableBefore);
        Assert.Equal(100, report.AvailableAfter);

        Assert.Equal(
            100,
            s.German.GetCurrentResourceTotals()
                .Personnel.Available);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase());
    }

    [Fact]
    public void Application_RequiresCompletedPersonnelPipeline()
    {
        var s = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase());

        Fire(s);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase());

        Contacts(s);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase());

        Effects(s);

        var applied =
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase();

        Assert.Equal(1, applied.Hour);
    }

    [Fact]
    public void ExternalManpowerChange_RejectsStaleCasualties()
    {
        var s = CreateScenario(exposed: 80);

        PrepareStrongEffects(
            s,
            killedProbability: 1,
            woundedProbability: 0);

        var manpower = s.Target.Company.Composition!.Manpower;

        // Simulation d'une autre source ayant deja
        // retire un homme depuis la phase de tir.
        manpower.ApplyCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 1,
                WoundedInAction: 0,
                MissingOrCaptured: 0));

        Assert.Equal(99, manpower.Current);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase());

        // Seule la perte externe deja appliquee existe.
        Assert.Equal(99, manpower.Current);
        Assert.Equal(1, manpower.KilledInAction);
        Assert.Null(s.Battle.DirectFire.LastCasualtyPhase);
    }

    [Fact]
    public void InvalidSecondCompany_PreventsPartialApplication()
    {
        var s = CreateScenario(exposed: 100);

        // Test direct du validateur de lots.
        // Premiere compagnie : bilan valide.
        var valid = new TacticalPersonnelEffectResult(
            Target: s.Target,
            ExposedPersonnel: 100,
            ContactEvents: 10,
            DistinctStruck: 5,
            Killed: 2,
            WoundedUnavailable: 3,
            NoLastingIncapacity: 0);

        // Deuxieme compagnie : les categories
        // totalisent 6 pour 5 personnes distinctes.
        var invalid = new TacticalPersonnelEffectResult(
            Target: s.Shooter,
            ExposedPersonnel: 100,
            ContactEvents: 10,
            DistinctStruck: 5,
            Killed: 5,
            WoundedUnavailable: 1,
            NoLastingIncapacity: 0);

        var effects = new TacticalPersonnelEffectsPhaseResult(
            Hour: 1,
            Seed: 42,
            Targets: Array.AsReadOnly(
                new[] { valid, invalid }));

        var availableAtFire =
            new Dictionary<CompanyDeployment, int>(
                ReferenceEqualityComparer.Instance)
            {
                [s.Target] = 100,
                [s.Shooter] = 100
            };

        Assert.Throws<InvalidOperationException>(() =>
            TacticalPersonnelCasualtyApplier.Apply(
                s.Battle,
                effects,
                availableAtFire));

        // Aucune perte sur la premiere compagnie,
        // malgre la validite de son propre bilan.
        Assert.Equal(
            100,
            s.Target.Company.Composition!.Manpower.Current);

        Assert.Equal(
            100,
            s.Shooter.Company.Composition!.Manpower.Current);
    }
    [Fact]
    public void DuplicateCompanyReport_IsRejected()
    {
        var s = CreateScenario(exposed: 100);

        var effect = new TacticalPersonnelEffectResult(
            Target: s.Target,
            ExposedPersonnel: 100,
            ContactEvents: 2,
            DistinctStruck: 2,
            Killed: 1,
            WoundedUnavailable: 1,
            NoLastingIncapacity: 0);

        var effects = new TacticalPersonnelEffectsPhaseResult(
            Hour: 1,
            Seed: 42,
            Targets: Array.AsReadOnly(
                new[] { effect, effect }));

        var availableAtFire =
            new Dictionary<CompanyDeployment, int>(
                ReferenceEqualityComparer.Instance)
            {
                [s.Target] = 100
            };

        Assert.Throws<InvalidOperationException>(() =>
            TacticalPersonnelCasualtyApplier.Apply(
                s.Battle,
                effects,
                availableAtFire));

        Assert.Equal(
            100,
            s.Target.Company.Composition!.Manpower.Current);

    }

    [Fact]
    public void WoundedPersonnel_CanReturnToDuty()
    {
        var s = CreateScenario(exposed: 80);

        PrepareStrongEffects(
            s,
            killedProbability: 0,
            woundedProbability: 1);

        var phase =
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase();

        var report = Assert.Single(phase.Companies);
        int wounded = report.Casualties.WoundedInAction;

        Assert.True(wounded > 0);
        Assert.Equal(0, report.Casualties.KilledInAction);

        var manpower = s.Target.Company.Composition!.Manpower;

        Assert.Equal(100 - wounded, manpower.Current);
        Assert.Equal(wounded, manpower.WoundedUnavailable);

        // Test du mecanisme de guerison deja existant.
        int returned = manpower.ReturnWoundedToDuty(wounded);

        Assert.Equal(wounded, returned);
        Assert.Equal(100, manpower.Current);
        Assert.Equal(0, manpower.WoundedUnavailable);

        Assert.Equal(
            100,
            s.German.GetCurrentResourceTotals()
                .Personnel.Available);
    }

    [Fact]
    public void NextFirePhase_UsesReducedAvailableManpower()
    {
        var s = CreateScenario(exposed: 80);

        PrepareStrongEffects(
            s,
            killedProbability: 1,
            woundedProbability: 0);

        var first =
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase();

        int firstLosses =
            Assert.Single(first.Companies).Casualties.Total;

        Assert.True(firstLosses > 0);

        int available =
            s.Target.Company.Composition!.Manpower.Current;

        Assert.Equal(100 - firstLosses, available);

        // La repartition precedente contient encore
        // 100 hommes : elle est donc perimee.
        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.PersonnelExposure.Get(s.Target));

        // Nouvelle repartition des survivants.
        s.Battle.PersonnelExposure.Set(
            s.Target,
            new TacticalPersonnelExposure(
                exposed: available,
                sheltered: 0,
                reserve: 0,
                exposedDepthMeters: 30));

        s.Perception.Apply(2, new[]
        {
                new TacticalObservationEvent(
                    s.Shooter,
                    s.Target,
                    new TacticalPoint(100, 0, 100),
                    s.Sector.Id,
                    TacticalKnowledgeLevel.Located,
                    PositionUncertaintyMeters: 10)
            });

        // Deuxieme heure, avec les ressources restantes.
        Fire(s, hour: 2);

        Contacts(s, seed: 88);
        Effects(s, seed: 99);

        var second =
            s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase();

        Assert.Equal(2, second.Hour);

        int secondLosses =
            Assert.Single(second.Companies).Casualties.Total;

        Assert.Equal(
            100 - firstLosses - secondLosses,
            s.Target.Company.Composition!.Manpower.Current);

        Assert.Equal(
            s.Target.Company.Composition!.Manpower.Current,
            s.German.GetCurrentResourceTotals()
                .Personnel.Available);
    }
}
