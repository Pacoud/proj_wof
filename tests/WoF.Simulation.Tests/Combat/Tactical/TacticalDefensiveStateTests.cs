
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat.Tactical;

/// <summary>
/// Regles de la disposition defensive d'une compagnie,
/// independamment du modele de tir qui l'utilise.
/// </summary>
public sealed class TacticalDefensiveStateTests
{
    private sealed record Scenario(
        TacticalBattle Battle,
        CompanyDeployment Target);

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
                infantryEquipment: 100)));

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }

    private static Scenario CreateScenario(
        SectorFeature features = SectorFeature.None)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battle");

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

        var battlefield = new TacticalBattlefield();

        var sector = new BattleSector(
            1, "Main", TerrainType.Plains,
            features: features);

        battlefield.AddSector(sector);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var target = battle.DeployCompany(
            german.Battalions[0].Companies[0],
            sector,
            CompanyDeploymentRole.FrontLine);

        return new Scenario(battle, target);
    }

    [Fact]
    public void CoverRequiresAppropriateSectorFeature()
    {
        var s = CreateScenario();

        var building = new TacticalDefensiveDisposition(
            TacticalStance.Crouched,
            TacticalCover.Building);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DefensiveState.Set(s.Target, building));

        var entrenched = new TacticalDefensiveDisposition(
            TacticalStance.Prone,
            TacticalCover.Entrenchment);

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DefensiveState.Set(s.Target, entrenched));

        Assert.Equal(
            TacticalCover.None,
            s.Battle.DefensiveState.Get(s.Target).Cover);

        var prepared = CreateScenario(
            features: SectorFeature.Entrenchments);

        prepared.Battle.DefensiveState.Set(
            prepared.Target,
            entrenched);

        Assert.Equal(
            TacticalCover.Entrenchment,
            prepared.Battle.DefensiveState
                .Get(prepared.Target).Cover);
    }

    [Fact]
    public void DefenseState_CannotMixDifferentBattles()
    {
        var s = CreateScenario();
        var other = CreateScenario();

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DefensiveState.Set(
                other.Target,
                new TacticalDefensiveDisposition(
                    TacticalStance.Prone,
                    TacticalCover.None)));

        Assert.Throws<InvalidOperationException>(() =>
            s.Battle.DefensiveState.Get(other.Target));
    }

    [Fact]
    public void InvalidDefensiveInputs_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalDefenseProfile(
                1, 0.6, 0.3, 0.1, 0.5, double.NaN));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TacticalDefenseProfile(
                1, 0.6, 0.3, 0.1, 1.5, 0.7));

        var s = CreateScenario();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            s.Battle.DefensiveState.Set(
                s.Target,
                new TacticalDefensiveDisposition(
                    (TacticalStance)999,
                    TacticalCover.None)));
    }
}
