
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat;

public class EngagementIndexTests
{
    private static Division CreateDivision(
        string name,
        Province province,
        Country country,
        double organization = 100)
    {
        return new Division(
            name,
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            maxOrganization: organization
        );
    }

    [Fact]
    public void DifferentProvinceObjectsWithSameIdStayIndependent()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        // Même identifiant numérique,
        // mais deux objets Province distincts.
        var firstProvince = new Province(10, "First");
        var secondProvince = new Province(10, "Second");

        var divisions = new[]
        {
            CreateDivision("F1", firstProvince, france),
            CreateDivision("G1", firstProvince, germany),
            CreateDivision("F2", secondProvince, france),
            CreateDivision("G2", secondProvince, germany)
        };

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var system = new EngagementSystem(diplomacy);

        system.DetectProvinceEngagements(
            divisions, currentHour: 1);

        Assert.Equal(2, system.ActiveEngagements.Count());

        Assert.Single(system.ActiveEngagements,
            e => ReferenceEquals(e.Province, firstProvince));

        Assert.Single(system.ActiveEngagements,
            e => ReferenceEquals(e.Province, secondProvince));

        // Une seconde détection ne doit pas
        // créer de batailles supplémentaires.
        system.DetectProvinceEngagements(
            divisions, currentHour: 2);

        Assert.Equal(2, system.Engagements.Count);
        Assert.Equal(2, system.ActiveEngagements.Count());
    }

    [Fact]
    public void EndedBattlesLeaveIndexesButRemainInHistory()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Battlefield");

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var system = new EngagementSystem(diplomacy);

        // Plusieurs batailles successives
        // dans la même province.
        for (int i = 0; i < 12; i++)
        {
            var french = CreateDivision(
                $"French {i}",
                province,
                france,
                organization: 1000);

            var german = CreateDivision(
                $"German {i}",
                province,
                germany,
                organization: 1);

            system.DetectProvinceEngagements(
                new[] { french, german },
                currentHour: i * 2 + 1);

            Assert.Single(system.ActiveEngagements);

            // L'organisation allemande très faible
            // provoque la fin de la bataille.
            system.ProcessEngagements(
                currentHour: i * 2 + 2);

            Assert.Empty(system.ActiveEngagements);

            Assert.Equal(
                i + 1,
                system.Engagements.Count);

            Assert.Equal(
                i + 1,
                system.History.Reports.Count);
        }

        // Les douze batailles sont archivées,
        // mais aucune n'est encore active.
        Assert.Equal(12, system.Engagements.Count);
        Assert.Equal(12, system.History.Reports.Count);
        Assert.Empty(system.ActiveEngagements);
    }

    [Fact]
    public void TransitReinforcementJoinsOnlyItsConnection()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var a = new Province(1, "A");
        var b = new Province(2, "B");
        var c = new Province(3, "C");
        var d = new Province(4, "D");

        a.ConnectTo(b);
        c.ConnectTo(d);

        var f1 = CreateDivision("F1", a, france);
        var g1 = CreateDivision("G1", b, germany);
        var f2 = CreateDivision("F2", c, france);
        var g2 = CreateDivision("G2", d, germany);

        var plan = new MovementPlan(
            DurationHours: 2,
            FuelPerHour: 0);

        Assert.True(f1.TryStartMovement(b, plan));
        Assert.True(g1.TryStartMovement(a, plan));
        Assert.True(f2.TryStartMovement(d, plan));
        Assert.True(g2.TryStartMovement(c, plan));

        foreach (var division in new[] { f1, g1, f2, g2 })
            division.AdvanceOneHour();

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var system = new EngagementSystem(diplomacy);

        system.DetectTransitEngagements(
            new[] { f1, g1, f2, g2 },
            currentHour: 1);

        Assert.Equal(2, system.ActiveEngagements.Count());

        var battleAB = Assert.Single(
            system.ActiveEngagements,
            e => ReferenceEquals(e.ConnectionA, a)
                && ReferenceEquals(e.ConnectionB, b));

        var battleCD = Assert.Single(
            system.ActiveEngagements,
            e => ReferenceEquals(e.ConnectionA, c)
                && ReferenceEquals(e.ConnectionB, d));

        // Un renfort français arrive sur A-B.
        var reinforcement = CreateDivision(
            "French Reinforcement", a, france);

        Assert.True(reinforcement.TryStartMovement(b, plan));
        reinforcement.AdvanceOneHour();

        system.DetectTransitEngagements(
            new[] { reinforcement },
            currentHour: 2);

        Assert.Same(
            battleAB,
            reinforcement.CurrentEngagement);

        Assert.Contains(reinforcement, battleAB.Participants);

        // La bataille C-D doit rester inchangée.
        Assert.Equal(2, battleCD.Participants.Count);
        Assert.Equal(2, system.ActiveEngagements.Count());
    }
}
