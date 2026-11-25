
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.Combat;

public sealed class EngagementDeploymentPlanTests
{
    private const ulong Seed = 20261125;
    private static readonly Country Red = new(1, "Rouge");
    private static readonly Country Blue = new(2, "Bleu");

    // ---- Mise en place ----

    /// <summary>Province P (Bleu) entourée des voisines demandées, frontières réciproques.</summary>
    private static (Province P, Dictionary<string, Province> Around, ProvinceBattlefieldGridCache Cache) World(
        params (string Name, double Bearing)[] neighbours)
    {
        var p = new Province(1, "P", TerrainType.Plains, Blue);
        var catalog = new ProvinceTerrainCatalog(Seed);
        var around = new Dictionary<string, Province>();
        var borders = new List<ProvinceNeighborBorder>();
        int id = 2;

        foreach (var (name, bearing) in neighbours)
        {
            var neighbour = new Province(id++, name);
            p.ConnectTo(neighbour);
            around[name] = neighbour;
            borders.Add(new ProvinceNeighborBorder(neighbour.Id, bearing, 1));
            catalog.SetBorders(neighbour, new[] { new ProvinceNeighborBorder(p.Id, (bearing + 180) % 360, 1) });
        }

        catalog.SetBorders(p, borders);
        return (p, around, new ProvinceBattlefieldGridCache(catalog));
    }

    /// <summary>Division d'infanterie : un bataillon par nombre donné, autant de compagnies d'infanterie.</summary>
    private static Division Build(string name, Province at, Country country, params int[] companiesPerBattalion)
    {
        var division = new Division(name, at, 100, 100, country: country);

        for (int b = 0; b < companiesPerBattalion.Length; b++)
        {
            var battalion = new Battalion(b + 1, $"{name}-B{b + 1}", BattalionType.Infantry);

            for (int c = 0; c < companiesPerBattalion[b]; c++)
                battalion.AddCompany(new Company(c + 1, $"{name}-B{b + 1}-C{c + 1}", CompanyType.Infantry));

            division.AddBattalion(battalion);
        }

        return division;
    }

    /// <summary>Division rouge construite en from, qui fait réellement le trajet vers to.</summary>
    private static Division Arrive(Province from, Province to, string name, params int[] companiesPerBattalion)
    {
        var division = Build(name, from, Red, companiesPerBattalion);
        Assert.True(division.TryStartMovement(to, new MovementPlan(DurationHours: 1, FuelPerHour: 0)));
        division.AdvanceOneHour();
        Assert.True(division.FinalizeArrival());
        return division;
    }

    /// <summary>Engagement de province détecté par le système : les arrivants attaquent le défenseur.</summary>
    private static Engagement Provincial(Division defender, params Division[] attackers)
    {
        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(Red, Blue);
        var system = new EngagementSystem(diplomacy);
        system.DetectProvinceEngagements(attackers.Append(defender).ToArray(), currentHour: 1, arrivals: attackers);
        return Assert.Single(system.ActiveEngagements);
    }

    private static EngagementDeploymentPlan Plan(
        ProvinceBattlefieldGridCache cache, Engagement engagement, DeploymentPlanOptions? options = null)
    {
        Assert.True(EngagementDeploymentZones.TryCompute(
            cache.GetOrGenerate(engagement), engagement, out var zones, out _));
        var plan = EngagementDeploymentPlan.Compute(zones!, engagement, options);
        AssertPlanIsConsistent(plan);
        return plan;
    }

    /// <summary>Position moyenne d'un grand secteur par rapport au centre du cadre (x vers l'est, y vers le nord).</summary>
    private static (double X, double Y) Offset(ProvinceBattlefieldGrid field, int sectorId)
    {
        var cells = field.Partition.GetSector(sectorId).Cells;
        return (cells.Average(c => field.Grid.GetCenterXMeters(c)) - field.Grid.FrameCenterXMeters,
                cells.Average(c => field.Grid.GetCenterYMeters(c)) - field.Grid.FrameCenterYMeters);
    }

    private static BattalionDeploymentAssignment Of(EngagementDeploymentPlan plan, Battalion battalion) =>
        Assert.Single(plan.Battalions, a => ReferenceEquals(a.Battalion, battalion));

    /// <summary>
    /// Chaque bataillon et chaque compagnie des deux camps apparaissent une seule fois.
    /// Première ligne sur le front de son camp, soutien et réserve à leur place,
    /// seconde ligne entièrement en réserve ; toute réserve est dans la zone arrière de son camp.
    /// </summary>
    private static void AssertPlanIsConsistent(EngagementDeploymentPlan plan)
    {
        var zones = plan.Zones;
        var expected = zones.EnteringDivisions.Concat(zones.FacingDivisions)
            .SelectMany(d => d.Battalions)
            .ToArray();

        Assert.Equal(expected, plan.Battalions.Select(a => a.Battalion));
        Assert.Equal(
            expected.SelectMany(b => b.Companies),
            plan.Battalions.SelectMany(a => a.Companies).Select(c => c.Company));

        foreach (var assignment in plan.Battalions)
        {
            bool entering = assignment.Side == DeploymentSide.Entering;
            var front = entering ? zones.EnteringFrontSectorIds : zones.FacingFrontSectorIds;
            var rear = entering ? zones.EnteringRearSectorIds : zones.FacingRearSectorIds;

            Assert.Contains(assignment.Entry, zones.EntryProvinces);

            if (rear.Count > 0)
                Assert.Contains(assignment.ReserveSectorId, rear);

            if (assignment.Echelon == BattalionEchelon.SecondLine)
            {
                Assert.Empty(assignment.FrontSectorIds);
                Assert.Null(assignment.SupportSectorId);
                Assert.All(assignment.Companies, c => Assert.Equal(CompanyDeploymentRole.Reserve, c.Role));
                continue;
            }

            Assert.NotEmpty(assignment.FrontSectorIds);
            Assert.All(assignment.FrontSectorIds, id => Assert.Contains(id, front));
            Assert.Contains(assignment.Companies, c => c.Role == CompanyDeploymentRole.FrontLine);

            // Le camp entrant n'a que deux bandes : le soutien reste dans le secteur du front.
            if (entering)
                Assert.Contains(assignment.SupportSectorId!.Value, assignment.FrontSectorIds);
        }
    }

    // ---- Rôles ----

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 1, 0, 0)]
    [InlineData(2, 1, 1, 0)]
    [InlineData(3, 2, 1, 0)]
    [InlineData(4, 2, 1, 1)]
    [InlineData(5, 3, 1, 1)]
    [InlineData(6, 3, 2, 1)]
    [InlineData(8, 4, 2, 2)]
    public void FrontLineIsRoundedUpReserveDownAndSupportTakesTheRest(int companies, int front, int support, int reserve)
    {
        Assert.Equal((front, support, reserve), EngagementDeploymentPlan.SplitRoles(companies));
    }

    [Fact]
    public void RoleSharesAreNamedParametersThatAddUpToOne()
    {
        var defaults = DeploymentPlanOptions.Default;
        Assert.Equal(0.5, defaults.FrontLineShare);
        Assert.Equal(0.25, defaults.SupportShare);
        Assert.Equal(0.25, defaults.ReserveShare);
        Assert.Equal(1, defaults.BattalionFrontSectors);

        var noSupport = new DeploymentPlanOptions { FrontLineShare = 0.75, SupportShare = 0, ReserveShare = 0.25 };
        Assert.Equal((3, 0, 1), EngagementDeploymentPlan.SplitRoles(4, noSupport));

        // En virgule flottante, 100 × 0,07 vaut 7,000000000000001 et 100 × 0,29 vaut 28,999999999999996 :
        // sans tolérance, la première ligne passerait à 8 et la réserve à 28.
        var uneven = new DeploymentPlanOptions { FrontLineShare = 0.07, SupportShare = 0.64, ReserveShare = 0.29 };
        Assert.Equal((7, 64, 29), EngagementDeploymentPlan.SplitRoles(100, uneven));

        Assert.Throws<ArgumentException>(() => EngagementDeploymentPlan.SplitRoles(4,
            new DeploymentPlanOptions { FrontLineShare = 0.5, SupportShare = 0.5, ReserveShare = 0.25 }));
        Assert.Throws<ArgumentException>(() => EngagementDeploymentPlan.SplitRoles(4,
            new DeploymentPlanOptions { FrontLineShare = 0, SupportShare = 0.5, ReserveShare = 0.5 }));
        Assert.Throws<ArgumentException>(() => EngagementDeploymentPlan.SplitRoles(4,
            new DeploymentPlanOptions { BattalionFrontSectors = 0 }));
    }

    [Fact]
    public void BattalionsOfOneTwoAndThreeCompaniesPlaceEachCompanyOnce()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var attacker = Arrive(around["E"], p, "R", 1, 2, 3);
        var headquarters = new Company(4, "R-B3-QG", CompanyType.Headquarters);
        attacker.Battalions[2].AddCompany(headquarters);
        var battle = Provincial(Build("D", p, Blue, 4), attacker);

        var plan = Plan(cache, battle);

        CompanyDeploymentRole[] Roles(int battalion) =>
            Of(plan, attacker.Battalions[battalion]).Companies.Select(c => c.Role).ToArray();

        const CompanyDeploymentRole F = CompanyDeploymentRole.FrontLine;
        const CompanyDeploymentRole S = CompanyDeploymentRole.Support;
        const CompanyDeploymentRole R = CompanyDeploymentRole.Reserve;
        Assert.Equal(new[] { F }, Roles(0));
        Assert.Equal(new[] { F, S }, Roles(1));
        Assert.Equal(new[] { F, F, S, R }, Roles(2)); // Le QG n'est pas une compagnie de combat.
        Assert.All(attacker.Battalions, b => Assert.Equal(BattalionEchelon.FirstLine, Of(plan, b).Echelon));
    }

    // ---- Front des bataillons ----

    [Fact]
    public void FirstLineBattalionsHoldOneGrandSectorEachInABlockCenteredOnTheFront()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var attacker = Arrive(around["E"], p, "R", 4, 4, 4);
        var defender = Build("D", p, Blue, 4, 4);
        var plan = Plan(cache, Provincial(defender, attacker));

        void AssertCentered(IReadOnlyList<int> line, Division division)
        {
            int start = (line.Count - division.Battalions.Count) / 2;

            for (int i = 0; i < division.Battalions.Count; i++)
                Assert.Equal(new[] { line[start + i] }, Of(plan, division.Battalions[i]).FrontSectorIds);
        }

        Assert.True(plan.EnteringFrontLines[0].Count >= 5);
        AssertCentered(plan.EnteringFrontLines[0], attacker);
        AssertCentered(plan.FacingFrontLines[0], defender);
    }

    [Fact]
    public void WiderBattalionFrontTakesConsecutiveSectorsOfTheLine()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var attacker = Arrive(around["E"], p, "R", 4, 4);
        var plan = Plan(cache, Provincial(Build("D", p, Blue, 4), attacker),
            new DeploymentPlanOptions { BattalionFrontSectors = 2 });

        var line = plan.EnteringFrontLines[0];
        int start = (line.Count - 4) / 2;

        Assert.Equal(line.Skip(start).Take(2), Of(plan, attacker.Battalions[0]).FrontSectorIds);
        Assert.Equal(line.Skip(start + 2).Take(2), Of(plan, attacker.Battalions[1]).FrontSectorIds);
    }

    [Fact]
    public void ExcessBattalionsAndBattalionsWithoutCombatCompaniesGoToTheSecondLine()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var probe = Plan(cache, Provincial(Build("D0", p, Blue, 4), Arrive(around["E"], p, "R0", 4)));
        int frontLength = probe.EnteringFrontLines[0].Count;

        // Deux bataillons de plus que de grands secteurs au front, puis un bataillon d'artillerie.
        var attacker = Arrive(around["E"], p, "R", Enumerable.Repeat(3, frontLength + 2).ToArray());
        var artillery = new Battalion(99, "R-Art", BattalionType.Artillery);
        artillery.AddCompany(new Company(1, "R-Art-1", CompanyType.Artillery));
        artillery.AddCompany(new Company(2, "R-Art-2", CompanyType.Artillery));
        var withArtillery = Build("RA", around["E"], Red);
        withArtillery.AddBattalion(artillery);
        Assert.True(withArtillery.TryStartMovement(p, new MovementPlan(DurationHours: 1, FuelPerHour: 0)));
        withArtillery.AdvanceOneHour();
        Assert.True(withArtillery.FinalizeArrival());

        // L'artillerie arrive en premier : elle ne prend pas de place au front pour autant.
        var plan = Plan(cache, Provincial(Build("D", p, Blue, 4), withArtillery, attacker));
        var echelons = attacker.Battalions.Select(b => Of(plan, b).Echelon).ToArray();

        Assert.Equal(frontLength, echelons.Count(e => e == BattalionEchelon.FirstLine));
        Assert.Equal(new[] { BattalionEchelon.SecondLine, BattalionEchelon.SecondLine }, echelons[^2..]);
        Assert.Equal(BattalionEchelon.SecondLine, Of(plan, artillery).Echelon);
        Assert.Equal(plan.EnteringFrontLines[0],
            attacker.Battalions.Take(frontLength).Select(b => Assert.Single(Of(plan, b).FrontSectorIds)));
    }

    // ---- Profondeur ----

    [Fact]
    public void SupportAndReserveAreNeverInTheSameBand()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var attacker = Arrive(around["E"], p, "R", 4, 4, 4);
        var defender = Build("D", p, Blue, 4, 4, 4);
        var plan = Plan(cache, Provincial(defender, attacker));
        var zones = plan.Zones;
        int support = EngagementDeploymentZones.FacingFrontBand + 1;

        foreach (var a in plan.Battalions)
        {
            Assert.Equal(BattalionEchelon.FirstLine, a.Echelon);
            int supportBand = zones.GetBand(a.SupportSectorId!.Value);
            int reserveBand = zones.GetBand(a.ReserveSectorId);

            if (a.Side == DeploymentSide.Entering)
            {
                // Attaquant : première ligne et soutien en bande 1, réserve en bande 0.
                Assert.Equal((1, 0), (supportBand, reserveBand));
            }
            else
            {
                // Défenseur : front en bande 3, soutien en bande 4, réserve dans la zone arrière, au-delà.
                Assert.Equal(support, supportBand);
                Assert.True(reserveBand > support);
            }
        }
    }

    // ---- Lignes ----

    [Fact]
    public void TwoOppositeEntriesGiveOneOrderedLinePerEntry()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var east = Arrive(around["E"], p, "RE", 4, 4);
        var west = Arrive(around["W"], p, "RW", 4, 4);
        var defender = Build("D", p, Blue, 4, 4, 4, 4);
        var battle = Provincial(defender, east, west);
        var plan = Plan(cache, battle);
        var field = plan.Zones.Field;

        Assert.Equal(new[] { around["E"], around["W"] }, plan.Zones.EntryProvinces);

        // Ligne 0 à l'est, ligne 1 à l'ouest, pour chaque front.
        foreach (var lines in new[] { plan.EnteringFrontLines, plan.FacingFrontLines })
        {
            Assert.All(lines[0], id => Assert.True(Offset(field, id).X > 0));
            Assert.All(lines[1], id => Assert.True(Offset(field, id).X < 0));

            // Axe latéral de l'est : du nord au sud ; de l'ouest : du sud au nord.
            Assert.True(lines[0].Select(id => Offset(field, id).Y).Zip(lines[0].Skip(1).Select(id => Offset(field, id).Y))
                .All(pair => pair.First >= pair.Second));
            Assert.True(lines[1].Select(id => Offset(field, id).Y).Zip(lines[1].Skip(1).Select(id => Offset(field, id).Y))
                .All(pair => pair.First <= pair.Second));
        }

        Assert.All(east.Battalions, b => Assert.Same(around["E"], Of(plan, b).Entry));
        Assert.All(west.Battalions, b => Assert.Same(around["W"], Of(plan, b).Entry));

        // Les bataillons du défenseur sont distribués tour à tour entre les deux lignes.
        Assert.Equal(new[] { around["E"], around["W"], around["E"], around["W"] },
            defender.Battalions.Select(b => Of(plan, b).Entry));
    }

    [Fact]
    public void ReserveStaysInTheRearZoneWhenAnEntryHasNoRearOfItsOwn()
    {
        // Attaqué au nord, à l'est et au sud : l'arrière du défenseur n'est plus que le bord ouest,
        // en face de l'entrée est. Les lignes nord et sud n'ont pas d'arrière à elles.
        var (p, around, cache) = World(("N", 0), ("E", 90), ("S", 180), ("W", 270));
        var defender = Build("D", p, Blue, 4, 4, 4);
        var battle = Provincial(defender,
            Arrive(around["N"], p, "RN", 4), Arrive(around["E"], p, "RE", 4), Arrive(around["S"], p, "RS", 4));
        var plan = Plan(cache, battle);
        var field = plan.Zones.Field;

        Assert.NotEmpty(plan.Zones.FacingRearSectorIds);
        Assert.All(plan.Zones.FacingRearSectorIds, id => Assert.True(Offset(field, id).X < 0));
        Assert.Equal(new[] { around["N"], around["E"], around["S"] },
            defender.Battalions.Select(b => Of(plan, b).Entry));
        Assert.All(defender.Battalions,
            b => Assert.Contains(Of(plan, b).ReserveSectorId, plan.Zones.FacingRearSectorIds));
    }

    [Fact]
    public void MeetingEngagementGetsAPlanForBothSides()
    {
        var (b, around, cache) = World(("A", 270));
        var a = around["A"];
        var fromA = Build("FA", a, Red, 4, 4);
        var fromB = Build("FB", b, Blue, 4, 4);
        var move = new MovementPlan(DurationHours: 5, FuelPerHour: 0);
        Assert.True(fromA.TryStartMovement(b, move));
        Assert.True(fromB.TryStartMovement(a, move));
        var battle = new Engagement(1, 0, a, b, 0.6);
        battle.AddParticipant(fromA, 0);
        battle.AddParticipant(fromB, 0);

        var plan = Plan(cache, battle);

        Assert.All(fromA.Battalions, bn => Assert.Equal(
            (DeploymentSide.Entering, BattalionEchelon.FirstLine), (Of(plan, bn).Side, Of(plan, bn).Echelon)));
        Assert.All(fromB.Battalions, bn => Assert.Equal(
            (DeploymentSide.Facing, BattalionEchelon.FirstLine), (Of(plan, bn).Side, Of(plan, bn).Echelon)));
    }

    [Fact]
    public void ZonesOfAnotherEngagementAreRefused()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var first = Provincial(Build("D1", p, Blue, 4), Arrive(around["E"], p, "R1", 4));
        var second = Provincial(Build("D2", p, Blue, 4), Arrive(around["E"], p, "R2", 4));
        Assert.True(EngagementDeploymentZones.TryCompute(cache.GetOrGenerate(first), first, out var zones, out _));

        Assert.Throws<ArgumentException>(() => EngagementDeploymentPlan.Compute(zones!, second));
    }

    [Fact]
    public void SameInputsGiveTheSamePlan()
    {
        (string Battalion, BattalionEchelon Echelon, int[] Front, int? Support, int Reserve, CompanyDeploymentRole[] Roles)[] Run()
        {
            var (p, around, cache) = World(("N", 0), ("E", 90), ("W", 270));
            var battle = Provincial(Build("D", p, Blue, 4, 3, 2, 1),
                Arrive(around["E"], p, "RE", 4, 4), Arrive(around["N"], p, "RN", 3, 3));
            return Plan(cache, battle).Battalions
                .Select(a => (a.Battalion.Name, a.Echelon, a.FrontSectorIds.ToArray(), a.SupportSectorId,
                    a.ReserveSectorId, a.Companies.Select(c => c.Role).ToArray()))
                .ToArray();
        }

        var first = Run();
        var second = Run();

        Assert.Equal(first.Length, second.Length);
        for (int i = 0; i < first.Length; i++)
        {
            Assert.Equal(first[i].Battalion, second[i].Battalion);
            Assert.Equal(first[i].Echelon, second[i].Echelon);
            Assert.Equal(first[i].Front, second[i].Front);
            Assert.Equal(first[i].Support, second[i].Support);
            Assert.Equal(first[i].Reserve, second[i].Reserve);
            Assert.Equal(first[i].Roles, second[i].Roles);
        }
    }
}