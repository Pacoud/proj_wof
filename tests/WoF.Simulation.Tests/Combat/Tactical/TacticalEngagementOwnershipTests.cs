
using WoF.Simulation.Core;
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalEngagementOwnershipTests
{
    private sealed record Scenario(
        EngagementSystem Engagements,
        DiplomacySystem Diplomacy,
        Engagement Engagement,
        TacticalBattle Battle,
        CompanyDeployment French,
        CompanyDeployment German,
        Division FrenchDivision,
        Division GermanDivision,
        BattleSector Sector);

    private static Division MakeDivision(
        Country country,
        Province province)
    {
        var division = new Division(
            $"{country.Name} Division",
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: 100,
                infantryEquipment: 100,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(
            1, "Battalion", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "Company", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 100,
                ammunition: new AmmunitionTotals(
                    smallArms: 1000))));

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static TacticalBattle CreateTacticalBattle(
        Engagement engagement)
    {
        var battlefield = new TacticalBattlefield();
        battlefield.AddSector(
            new BattleSector(
                1, "Plain", TerrainType.Plains));

        return new TacticalBattle(engagement, battlefield);
    }

    private static Scenario CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var french = MakeDivision(france, province);
        var german = MakeDivision(germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            Assert.Single(engagements.ActiveEngagements);

        var battle = CreateTacticalBattle(engagement);
        var sector = Assert.Single(battle.Battlefield.Sectors);

        var frenchDeployment = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var germanDeployment = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        return new Scenario(
            engagements,
            diplomacy,
            engagement,
            battle,
            frenchDeployment,
            germanDeployment,
            french,
            german,
            sector);
    }

    /// <summary>
    /// Termine une phase de bataille tactique
    /// sans aucune salve. Zero perte est un
    /// resultat valide, pas une phase manquante.
    /// </summary>
    private static void CompleteEmptyTacticalHour(
        Scenario s,
        long hour)
    {
        var geometry = new TacticalGeometryMap(s.Battle);
        var perception = new TacticalPerceptionState(s.Battle);

        var fired = s.Battle.DirectFire.Execute(
            Array.Empty<TacticalFireOrder>(),
            geometry,
            s.Diplomacy,
            perception,
            hour);

        Assert.Empty(fired.Events);

        s.Battle.DirectFire.ResolvePersonnelLastPhase(
            new TacticalHitProfile(1, 0, 0.5),
            new TacticalDefenseProfile(
                1, 1, 1, 0, 0, 0),
            seed: 1);

        s.Battle.DirectFire.ResolvePersonnelEffectsLastPhase(
            new TacticalPersonnelEffectProfile(0, 0),
            seed: 2);

        s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase();
    }

    private static void CompleteLethalTacticalHour(
        Scenario s,
        long hour)
    {
        var geometry = new TacticalGeometryMap(s.Battle);

        geometry.DefineSector(
            s.Sector,
            centerXMeters: 50,
            centerYMeters: 0,
            radiusMeters: 500);

        geometry.PlaceCompany(
            s.French,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 20);

        geometry.PlaceCompany(
            s.German,
            new TacticalPoint(100, 0, 100),
            facingDegrees: 270,
            frontageMeters: 1);

        s.Battle.PersonnelExposure.Set(
            s.German,
            new TacticalPersonnelExposure(
                exposed: 100,
                sheltered: 0,
                reserve: 0,
                exposedDepthMeters: 1));

        var perception = new TacticalPerceptionState(s.Battle);

        perception.Apply(hour, new[]
        {
            new TacticalObservationEvent(
                s.French,
                s.German,
                new TacticalPoint(100, 0, 100),
                s.Sector.Id,
                TacticalKnowledgeLevel.Located,
                PositionUncertaintyMeters: 0)
        });

        var phase = s.Battle.DirectFire.Execute(
            new[]
            {
                new TacticalFireOrder(
                    s.French,
                    s.German,
                    new TacticalDirectFireProfile(250),
                    WeaponsCommitted: 10,
                    RoundsPerWeapon: 3)
            },
            geometry,
            s.Diplomacy,
            perception,
            hour);

        Assert.Single(phase.Events);

        // Parametres extremement favorables
        // destines aux tests, non historiques.
        s.Battle.DirectFire.ResolvePersonnelLastPhase(
            new TacticalHitProfile(
                minimumDispersionMeters: 0.001,
                dispersionAt100Meters: 0,
                effectiveTargetRadiusMeters: 10),
            new TacticalDefenseProfile(
                1, 1, 1, 0, 0, 0),
            seed: 3);

        s.Battle.DirectFire.ResolvePersonnelEffectsLastPhase(
            new TacticalPersonnelEffectProfile(
                killedProbability: 1,
                woundedUnavailableProbability: 0),
            seed: 4);

        s.Battle.DirectFire.ApplyPersonnelEffectsLastPhase();
    }

    [Fact]
    public void NewEngagement_UsesLegacyModeByDefault()
    {
        var s = CreateScenario();

        Assert.Equal(
            EngagementCombatMode.LegacyOperational,
            s.Engagements.GetCombatMode(s.Engagement));
    }

    [Fact]
    public void TacticalRegistration_IsExclusive()
    {
        var s = CreateScenario();

        s.Engagements.AttachTacticalBattle(s.Battle);

        Assert.Equal(
            EngagementCombatMode.Tactical,
            s.Engagements.GetCombatMode(s.Engagement));

        Assert.Throws<InvalidOperationException>(() =>
            s.Engagements.AttachTacticalBattle(s.Battle));

        var anotherBattle = CreateTacticalBattle(
            s.Engagement);

        Assert.Throws<InvalidOperationException>(() =>
            s.Engagements.AttachTacticalBattle(anotherBattle));
    }

    [Fact]
    public void ForeignEngagement_CannotBeRegistered()
    {
        var owner = CreateScenario();
        var foreign = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            owner.Engagements.AttachTacticalBattle(
                foreign.Battle));
    }

    [Fact]
    public void MissingTacticalPhase_IsRejected()
    {
        var s = CreateScenario();

        s.Engagements.AttachTacticalBattle(s.Battle);

        var frenchBefore =
            s.FrenchDivision.GetCurrentResourceTotals();

        var germanBefore =
            s.GermanDivision.GetCurrentResourceTotals();

        Assert.Throws<InvalidOperationException>(() =>
            s.Engagements.ProcessEngagements(2));

        Assert.Equal(
            frenchBefore,
            s.FrenchDivision.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.GermanDivision.GetCurrentResourceTotals());
    }

    [Fact]
    public void EmptyTacticalPhase_SkipsLegacyCombatCalculations()
    {
        var s = CreateScenario();
        s.Engagements.AttachTacticalBattle(s.Battle);

        CompleteEmptyTacticalHour(s, hour: 2);

        var frenchBefore =
            s.FrenchDivision.GetCurrentResourceTotals();

        var germanBefore =
            s.GermanDivision.GetCurrentResourceTotals();

        double frenchOrganization =
            s.FrenchDivision.Organization;

        double germanOrganization =
            s.GermanDivision.Organization;

        double frenchFuel = s.FrenchDivision.Fuel;
        double germanFuel = s.GermanDivision.Fuel;

        s.Engagements.ProcessEngagements(2);

        Assert.Equal(
            frenchBefore,
            s.FrenchDivision.GetCurrentResourceTotals());

        Assert.Equal(
            germanBefore,
            s.GermanDivision.GetCurrentResourceTotals());

        // Aucune perte ni consommation calculee
        // independamment par l'ancien moteur.
        Assert.Equal(
            frenchOrganization,
            s.FrenchDivision.Organization);

        Assert.Equal(
            germanOrganization,
            s.GermanDivision.Organization);

        Assert.Equal(frenchFuel, s.FrenchDivision.Fuel);
        Assert.Equal(germanFuel, s.GermanDivision.Fuel);

        Assert.True(s.Engagement.IsActive);
    }

    [Fact]
    public void TacticalCasualties_AreNotAppliedAgainByLegacy()
    {
        var s = CreateScenario();
        s.Engagements.AttachTacticalBattle(s.Battle);

        CompleteLethalTacticalHour(s, hour: 2);

        int availableAfterTactical =
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available;

        Assert.InRange(availableAfterTactical, 70, 99);

        double organizationBefore =
            s.GermanDivision.Organization;

        var frenchBefore =
            s.FrenchDivision.GetCurrentResourceTotals();

        s.Engagements.ProcessEngagements(2);

        // Les pertes tactiques ont deja ete appliquees.
        // Le traitement operationnel ne les double pas.
        Assert.Equal(
            availableAfterTactical,
            s.GermanDivision.GetCurrentResourceTotals()
                .Personnel.Available);

        Assert.Equal(
            organizationBefore,
            s.GermanDivision.Organization);

        Assert.Equal(
            frenchBefore,
            s.FrenchDivision.GetCurrentResourceTotals());

        Assert.Equal(
            970,
            frenchBefore.Ammunition.SmallArms);
    }

    [Fact]
    public void TacticalHour_CannotBeProcessedTwice()
    {
        var s = CreateScenario();
        s.Engagements.AttachTacticalBattle(s.Battle);

        CompleteEmptyTacticalHour(s, hour: 2);
        s.Engagements.ProcessEngagements(2);

        Assert.Throws<InvalidOperationException>(() =>
            s.Engagements.ProcessEngagements(2));
    }

    [Fact]
    public void NextTacticalHour_RequiresNewCompletedPhase()
    {
        var s = CreateScenario();
        s.Engagements.AttachTacticalBattle(s.Battle);

        CompleteEmptyTacticalHour(s, hour: 2);
        s.Engagements.ProcessEngagements(2);

        Assert.Throws<InvalidOperationException>(() =>
            s.Engagements.ProcessEngagements(3));

        CompleteEmptyTacticalHour(s, hour: 3);
        s.Engagements.ProcessEngagements(3);

        Assert.True(s.Engagement.IsActive);
    }

    [Fact]
    public void PreviouslyLegacyProcessedBattle_CannotSwitchMode()
    {
        var s = CreateScenario();

        s.Engagements.ProcessEngagements(2);

        Assert.Throws<InvalidOperationException>(() =>
            s.Engagements.AttachTacticalBattle(s.Battle));
    }


    [Fact]
    public void SimulationClock_DoesNotAdvanceWithoutTacticalPhase()
    {
        var engine = new SimulationEngine();

        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var french = MakeDivision(france, province);
        var german = MakeDivision(germany, province);

        engine.AddDivision(french);
        engine.AddDivision(german);
        engine.DeclareWar(france, germany);

        // Heure 1 : apparition de l'engagement.
        engine.Tick();

        var engagement = Assert.Single(
            engine.Engagements.ActiveEngagements);

        var tactical = CreateTacticalBattle(engagement);
        var sector = Assert.Single(tactical.Battlefield.Sectors);

        tactical.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        tactical.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        engine.Engagements.AttachTacticalBattle(tactical);

        long before = engine.Clock.CurrentHour;

        // Aucune phase tactique pour l'heure 2.
        Assert.Throws<InvalidOperationException>(() =>
            engine.Tick());

        // Le temps n'a pas avance artificiellement.
        Assert.Equal(before, engine.Clock.CurrentHour);

        Assert.True(engagement.IsActive);
    }

}
