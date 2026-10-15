
using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Fuel;
using WoF.Simulation.Military.Combat.Power;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat;

public class CountryCombatAggregationTests
{
    private static Division AddDivision(
        SimulationEngine engine,
        Province province,
        Country country,
        string name,
        DivisionType type = DivisionType.Infantry)
    {
        var division = new Division(
            name: name,
            position: province,
            fuel: 100,
            ammunition: 100,
            country: country,
            type: type,
            maxOrganization: 1000
        );

        engine.AddDivision(division);
        return division;
    }

    // Calcul indépendant de la puissance attendue
    // au premier tick de combat.
    private static CombatPowerProfile ExpectedPower(
        Division division,
        Province province)
    {
        var resources = division.GetCurrentResourceTotals();

        var physicalPower =
            CompositionCombatPowerCalculator.Calculate(
                resources);

        var terrainPower =
            CombatTerrainModifier.Apply(
                physicalPower,
                province.Terrain);

        double demand =
            CompositionCombatFuelCalculator.Calculate(
                resources).Total;

        double consumed = Math.Min(
            division.Fuel,
            demand);

        double satisfaction = demand <= 0
            ? 1.0
            : Math.Clamp(consumed / demand, 0.0, 1.0);

        return CombatFuelModifier.Apply(
            terrainPower,
            satisfaction);
    }

    [Fact]
    public void TwoCountries_PreserveOrganizationPressure()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(
            1, "Battlefield", owner: france);

        var engine = new SimulationEngine();

        var french = AddDivision(
            engine, province, france, "French");

        var german = AddDivision(
            engine, province, germany, "German",
            DivisionType.Armored);

        var frenchPower = ExpectedPower(french, province);
        var germanPower = ExpectedPower(german, province);

        engine.DeclareWar(france, germany);

        engine.Tick(); // Détection.
        engine.Tick(); // Combat.

        Assert.Equal(
            1000 - Math.Round(germanPower.Total, 2),
            french.Organization,
            2);

        Assert.Equal(
            1000 - Math.Round(frenchPower.Total, 2),
            german.Organization,
            2);
    }

    [Fact]
    public void ThreeCountries_PreservePartialHostilities()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var britain = new Country(3, "Britain");

        var province = new Province(
            1, "Battlefield", owner: france);

        var engine = new SimulationEngine();

        // L'ordre est volontairement entrelacé.
        var french1 = AddDivision(
            engine, province, france, "French 1");

        var german = AddDivision(
            engine, province, germany, "German",
            DivisionType.Armored);

        var british = AddDivision(
            engine, province, britain, "British",
            DivisionType.Motorized);

        var french2 = AddDivision(
            engine, province, france, "French 2",
            DivisionType.Motorized);

        var french1Power = ExpectedPower(
            french1, province);

        var germanPower = ExpectedPower(
            german, province);

        var britishPower = ExpectedPower(
            british, province);

        var french2Power = ExpectedPower(
            french2, province);

        engine.DeclareWar(france, germany);
        engine.DeclareWar(britain, germany);

        // France et Grande-Bretagne ne sont
        // pas en guerre l'une contre l'autre.
        Assert.False(
            engine.Diplomacy.AreAtWar(france, britain));

        engine.Tick();

        Assert.All(
            new[] { french1, german, british, french2 },
            d => Assert.True(d.IsEngaged));

        engine.Tick();

        // Les deux divisions françaises se partagent
        // uniquement la pression allemande.
        double frenchLoss = Math.Round(
            germanPower.Total / 2.0, 2);

        Assert.Equal(
            1000 - frenchLoss,
            french1.Organization,
            2);

        Assert.Equal(
            1000 - frenchLoss,
            french2.Organization,
            2);

        // Les Britanniques reçoivent la pression
        // allemande, sans la partager avec la France.
        Assert.Equal(
            1000 - Math.Round(germanPower.Total, 2),
            british.Organization,
            2);

        // L'Allemagne reçoit la pression des
        // deux formations françaises et des Britanniques,
        // dans l'ordre initial des participants.
        double hostilePressure = new[]
        {
            french1Power,
            britishPower,
            french2Power
        }.Sum(power => power.Total);

        Assert.Equal(
            1000 - Math.Round(hostilePressure, 2),
            german.Organization,
            2);
    }

    [Fact]
    public void BrokenEnemy_DoesNotContributePower()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(
            1, "Battlefield", owner: france);

        var engine = new SimulationEngine();

        var french = AddDivision(
            engine, province, france, "French");

        var german = AddDivision(
            engine, province, germany, "German");

        engine.DeclareWar(france, germany);
        engine.Tick();

        // L'ennemi est déjà rompu avant
        // la prochaine résolution des pertes.
        german.LoseOrganization(1000);

        engine.Tick();

        Assert.Equal(1000.0, french.Organization, 2);

        Assert.False(french.IsEngaged);

        Assert.Single(
            engine.Engagements.History.Reports);
    }

    [Fact]
    public void Reinforcement_UpdatesCountryPressureNextTick()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(
            1, "Battlefield", owner: france);

        var engine = new SimulationEngine();

        var french = AddDivision(
            engine, province, france, "French");

        AddDivision(
            engine, province, germany, "German 1");

        engine.DeclareWar(france, germany);

        engine.Tick(); // Contact.
        engine.Tick(); // Combat initial.

        double firstLoss =
            1000 - french.Organization;

        // Une nouvelle division ennemie rejoint
        // cet engagement déjà actif.
        var reinforcement = AddDivision(
            engine, province, germany, "German 2");

        engine.Engagements.DetectProvinceEngagements(
            engine.Divisions,
            engine.Clock.CurrentHour);

        Assert.True(reinforcement.IsEngaged);

        double beforeNextTick = french.Organization;

        engine.Tick();

        double secondLoss =
            beforeNextTick - french.Organization;

        // Deux adversaires au lieu d'un :
        // la pression reçue doit augmenter.
        Assert.True(
            secondLoss > firstLoss,
            $"Expected increased pressure: " +
            $"first={firstLoss}, second={secondLoss}");
    }
}
