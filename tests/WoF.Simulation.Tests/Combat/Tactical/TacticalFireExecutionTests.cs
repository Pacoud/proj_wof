
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalDirectFireExecutionTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalPerceptionState Perception,
        DiplomacySystem Diplomacy,
        CompanyDeployment French,
        CompanyDeployment German,
        BattleSector Sector,
        Division FrenchDivision,
        Division GermanDivision);

    private static Division MakeDivision(
        Country country,
        Province province,
        long ammunition)
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
            1,
            "Company",
            CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 100,
                ammunition: new AmmunitionTotals(
                    smallArms: ammunition))));

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario(
        long frenchAmmo = 100,
        long germanAmmo = 80)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battle");

        var french = MakeDivision(
            france, province, frenchAmmo);

        var german = MakeDivision(
            germany, province, germanAmmo);

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
            1, "Plains", TerrainType.Plains);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var frenchDeployment = battle.DeployCompany(
            french.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var germanDeployment = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);
        geometry.DefineSector(
            sector, 50, 0, radiusMeters: 500);

        geometry.PlaceCompany(
            frenchDeployment,
            new TacticalPoint(0, 0, 100),
            facingDegrees: 90,
            frontageMeters: 40);

        geometry.PlaceCompany(
            germanDeployment,
            new TacticalPoint(100, 0, 100),
            facingDegrees: 270,
            frontageMeters: 40);

        return new Scenario(
            battle,
            geometry,
            new TacticalPerceptionState(battle),
            diplomacy,
            frenchDeployment,
            germanDeployment,
            sector,
            french,
            german);
    }

    private static void MarkLocated(
        Scenario s,
        long hour,
        bool bothDirections = false)
    {
        var observations = new List<TacticalObservationEvent>
        {
            new(
                s.French,
                s.German,
                new TacticalPoint(100, 0, 100),
                s.Sector.Id)
        };

        if (bothDirections)
        {
            observations.Add(new TacticalObservationEvent(
                s.German,
                s.French,
                new TacticalPoint(0, 0, 100),
                s.Sector.Id));
        }

        s.Perception.Apply(hour, observations);
    }

    private static TacticalFireOrder FrenchOrder(
        Scenario s,
        int weapons = 10,
        int roundsPerWeapon = 3)
    {
        return new TacticalFireOrder(
            s.French,
            s.German,
            new TacticalDirectFireProfile(200),
            weapons,
            roundsPerWeapon);
    }

    private static TacticalFireOrder GermanOrder(
        Scenario s,
        int weapons = 4,
        int roundsPerWeapon = 5)
    {
        return new TacticalFireOrder(
            s.German,
            s.French,
            new TacticalDirectFireProfile(200),
            weapons,
            roundsPerWeapon);
    }

    private static TacticalFirePhaseResult Execute(
        Scenario s,
        long hour,
        params TacticalFireOrder[] orders)
    {
        return s.Battle.DirectFire.Execute(
            orders,
            s.Geometry,
            s.Diplomacy,
            s.Perception,
            hour);
    }

    [Fact]
    public void SuccessfulSalvo_ConsumesAmmoWithoutDamage()
    {
        var s = CreateScenario();
        MarkLocated(s, hour: 1);

        var germanBefore =
            s.GermanDivision.GetCurrentResourceTotals();

        var result = Execute(s, 1, FrenchOrder(s));

        Assert.Equal(
            TacticalFireOrderStatus.Fired,
            Assert.Single(result.Outcomes).Status);

        var fire = Assert.Single(result.Events);

        Assert.Equal(1, fire.Hour);
        Assert.Same(s.French, fire.Shooter);
        Assert.Equal(AmmunitionType.SmallArms, fire.AmmunitionType);
        Assert.Equal(10, fire.WeaponsCommitted);
        Assert.Equal(30, fire.RoundsExpended);

        var knownTarget = Assert.Single(
            s.Perception.GetReports(s.French));

        Assert.Equal(knownTarget.TrackId, fire.TargetTrackId);

        Assert.Equal(
            70,
            s.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));

        Assert.Equal(
            70,
            s.FrenchDivision.GetCurrentResourceTotals()
                .Ammunition.SmallArms);

        // Tir execute, mais pas encore d'impacts.
        Assert.Equal(
            germanBefore,
            s.GermanDivision.GetCurrentResourceTotals());
    }

    [Fact]
    public void UnknownTarget_CannotConsumeAmmunition()
    {
        var s = CreateScenario();

        var result = Execute(s, 1, FrenchOrder(s));

        var outcome = Assert.Single(result.Outcomes);

        Assert.Equal(
            TacticalFireOrderStatus.AuthorizationDenied,
            outcome.Status);

        Assert.Equal(
            TacticalDirectFireStatus.NoCurrentLocalization,
            outcome.AuthorizationStatus);

        Assert.Empty(result.Events);

        Assert.Equal(
            100,
            s.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));
    }

    [Fact]
    public void StaleLocalization_DoesNotPermitFire()
    {
        var s = CreateScenario();

        MarkLocated(s, hour: 1);

        // Une nouvelle passe sans observation rend
        // la localisation precedente perimee.
        s.Perception.Apply(
            2,
            Array.Empty<TacticalObservationEvent>());

        var result = Execute(s, 2, FrenchOrder(s));

        Assert.Equal(
            TacticalFireOrderStatus.AuthorizationDenied,
            Assert.Single(result.Outcomes).Status);

        Assert.Empty(result.Events);

        Assert.Equal(
            100,
            s.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));
    }

    [Fact]
    public void InsufficientAmmunition_RejectsSalvo()
    {
        var s = CreateScenario(frenchAmmo: 10);
        MarkLocated(s, 1);

        var result = Execute(s, 1, FrenchOrder(s));

        Assert.Equal(
            TacticalFireOrderStatus.InsufficientAmmunition,
            Assert.Single(result.Outcomes).Status);

        Assert.Empty(result.Events);

        Assert.Equal(
            10,
            s.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));
    }

    [Fact]
    public void InsufficientWeapons_RejectsSalvo()
    {
        var s = CreateScenario();
        MarkLocated(s, 1);

        s.French.Company.Composition!
            .InfantryEquipment.ApplyLoss(99);

        // Il reste une seule arme, mais l'ordre en
        // demande deux.
        var result = Execute(
            s, 1, FrenchOrder(s, weapons: 2));

        Assert.Equal(
            TacticalFireOrderStatus.InsufficientWeapons,
            Assert.Single(result.Outcomes).Status);

        Assert.Empty(result.Events);

        Assert.Equal(
            100,
            s.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));
    }

    [Fact]
    public void OpposingCompanies_CanFireInSamePhase()
    {
        var s = CreateScenario();
        MarkLocated(s, 1, bothDirections: true);

        var result = Execute(
            s, 1, FrenchOrder(s), GermanOrder(s));

        Assert.Equal(2, result.Events.Count);
        Assert.All(result.Outcomes, outcome =>
            Assert.Equal(
                TacticalFireOrderStatus.Fired,
                outcome.Status));

        Assert.Equal(
            70,
            s.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));

        Assert.Equal(
            60,
            s.German.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));

        Assert.Equal(
            100,
            s.French.Company.Composition.Manpower.Current);

        Assert.Equal(
            100,
            s.German.Company.Composition.Manpower.Current);
    }

    [Fact]
    public void RepeatedPhase_CannotConsumeTwice()
    {
        var s = CreateScenario();
        MarkLocated(s, 1);

        var first = Execute(s, 1, FrenchOrder(s));

        Assert.Single(first.Events);

        Assert.Throws<InvalidOperationException>(() =>
            Execute(s, 1, FrenchOrder(s)));

        Assert.Equal(
            70,
            s.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));

        Assert.Same(first, s.Battle.DirectFire.LastPhase);
    }

    [Fact]
    public void InvalidSecondOrder_CancelsWholePhase()
    {
        var s = CreateScenario();
        var foreign = CreateScenario();

        MarkLocated(s, 1);

        var invalid = new TacticalFireOrder(
            s.German,
            foreign.French,
            new TacticalDirectFireProfile(200),
            1,
            1);

        Assert.Throws<InvalidOperationException>(() =>
            Execute(s, 1, FrenchOrder(s), invalid));

        Assert.Equal(
            100,
            s.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));

        Assert.Null(s.Battle.DirectFire.LastPhase);

        // L'echec de validation n'a pas consomme
        // l'heure : une phase valide reste possible.
        var valid = Execute(s, 1, FrenchOrder(s));

        Assert.Single(valid.Events);
    }

    [Fact]
    public void DuplicateShooter_IsRejectedBeforeMutation()
    {
        var s = CreateScenario();
        MarkLocated(s, 1);

        Assert.Throws<InvalidOperationException>(() =>
            Execute(
                s,
                1,
                FrenchOrder(s),
                FrenchOrder(s, weapons: 1)));

        Assert.Equal(
            100,
            s.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));

        Assert.Null(s.Battle.DirectFire.LastPhase);
    }

    [Fact]
    public void InvalidOrders_AreRejectedBeforeMutation()
    {
        var s = CreateScenario();
        MarkLocated(s, 1);

        var invalid = FrenchOrder(
            s, weapons: 0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Execute(s, 1, invalid));

        Assert.Equal(
            100,
            s.French.Company.Composition!.Ammunition
                .GetAvailable(AmmunitionType.SmallArms));

        Assert.Null(s.Battle.DirectFire.LastPhase);
    }
}
