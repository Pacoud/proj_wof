
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.Combat;

public sealed class EngagementDeploymentZonesTests
{
    private const ulong Seed = 20261125;
    private static readonly Country Red = new(1, "Rouge");
    private static readonly Country Blue = new(2, "Bleu");

    // tan 35° : un grand secteur à ±35° d'une direction, vu du centre (±30° plus la largeur
    // d'un grand secteur). Exclut les coins du cadre, à 45°.
    private static readonly double WithinThirtyFiveDegrees = Math.Tan(35 * Math.PI / 180);

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

    /// <summary>Division rouge qui fait réellement le trajet de from vers to.</summary>
    private static Division Arrive(Province from, Province to, string name)
    {
        var division = new Division(name, from, 100, 100, country: Red);
        Assert.True(division.TryStartMovement(to, new MovementPlan(DurationHours: 1, FuelPerHour: 0)));
        division.AdvanceOneHour();
        Assert.True(division.FinalizeArrival());
        Assert.Same(from, division.EnteredFrom);
        return division;
    }

    /// <summary>Engagement de province détecté par le système : les arrivants attaquent un défenseur bleu.</summary>
    private static Engagement Provincial(Province p, params Division[] attackers)
    {
        var defender = new Division("Défense", p, 100, 100, country: Blue);
        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(Red, Blue);
        var system = new EngagementSystem(diplomacy);
        system.DetectProvinceEngagements(attackers.Append(defender).ToArray(), currentHour: 1, arrivals: attackers);
        return Assert.Single(system.ActiveEngagements);
    }

    private static EngagementDeploymentZones Compute(
        ProvinceBattlefieldGrid field, Engagement engagement, int? extent = null)
    {
        Assert.True(EngagementDeploymentZones.TryCompute(field, engagement, out var zones, out var refusal, extent));
        Assert.Equal(DeploymentZoneRefusal.None, refusal);
        AssertZonesAreConsistent(zones!);
        return zones!;
    }

    /// <summary>Position moyenne d'un grand secteur par rapport au centre du cadre (x vers l'est, y vers le nord).</summary>
    private static (double X, double Y) Offset(ProvinceBattlefieldGrid field, int sectorId)
    {
        var cells = field.Partition.GetSector(sectorId).Cells;
        return (cells.Average(c => field.Grid.GetCenterXMeters(c)) - field.Grid.FrameCenterXMeters,
                cells.Average(c => field.Grid.GetCenterYMeters(c)) - field.Grid.FrameCenterYMeters);
    }

    /// <summary>
    /// Zones non vides (sauf l'arrière de l'autre camp), dans le champ, aux bonnes bandes,
    /// deux à deux disjointes : le front d'un camp n'est jamais dans l'arrière de l'autre.
    /// </summary>
    private static void AssertZonesAreConsistent(EngagementDeploymentZones zones)
    {
        var all = new[]
        {
            zones.EnteringRearSectorIds, zones.EnteringFrontSectorIds,
            zones.FacingFrontSectorIds, zones.FacingRearSectorIds,
        };

        Assert.NotEmpty(zones.EnteringRearSectorIds);
        Assert.NotEmpty(zones.EnteringFrontSectorIds);
        Assert.NotEmpty(zones.FacingFrontSectorIds);

        foreach (var zone in all)
            Assert.All(zone, id => Assert.Contains(id, zones.BattlefieldSectorIds));

        Assert.All(zones.EnteringRearSectorIds, id => Assert.Equal(0, zones.GetBand(id)));
        Assert.All(zones.EnteringFrontSectorIds, id => Assert.Equal(1, zones.GetBand(id)));
        Assert.All(zones.FacingFrontSectorIds,
            id => Assert.Equal(EngagementDeploymentZones.FacingFrontBand, zones.GetBand(id)));
        Assert.All(zones.FacingRearSectorIds,
            id => Assert.True(zones.GetBand(id) > EngagementDeploymentZones.FacingFrontBand));

        Assert.Empty(zones.EnteringFrontSectorIds.Intersect(zones.FacingRearSectorIds));
        Assert.Empty(zones.FacingFrontSectorIds.Intersect(zones.EnteringRearSectorIds));

        for (int i = 0; i < all.Length; i++)
            for (int j = i + 1; j < all.Length; j++)
                Assert.Empty(all[i].Intersect(all[j]));
    }

    // ---- Provenance ----

    [Fact]
    public void EntryIsFrozenWhenJoiningAndKeptAfterAHalfTurn()
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");
        a.ConnectTo(b);
        var french = new Division("F", a, 100, 100, country: Red);
        var german = new Division("G", b, 100, 100, country: Blue);
        Assert.Null(french.EnteredFrom); // Placée au départ.

        var plan = new MovementPlan(DurationHours: 2, FuelPerHour: 0);
        Assert.True(french.TryStartMovement(b, plan));
        Assert.True(german.TryStartMovement(a, plan));
        french.AdvanceOneHour();
        german.AdvanceOneHour();
        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(Red, Blue);
        var system = new EngagementSystem(diplomacy);
        system.DetectTransitEngagements(new[] { french, german }, currentHour: 1);
        var battle = Assert.Single(system.ActiveEngagements);
        Assert.Same(a, battle.GetEntryProvince(french));
        Assert.Same(b, battle.GetEntryProvince(german));

        // Le Français perd et fait demi-tour : il revient en A en venant du côté de B.
        french.LoseOrganization(french.Organization);
        system.ProcessEngagements(currentHour: 2);
        french.Transit!.Reverse();
        german.Transit!.Resume();
        foreach (var division in new[] { french, german })
        {
            division.AdvanceOneHour();
            Assert.True(division.FinalizeArrival());
        }

        Assert.Same(b, french.EnteredFrom);
        Assert.Same(b, german.EnteredFrom);
        Assert.Same(a, battle.GetEntryProvince(french)); // Gelée à l'entrée dans l'engagement.
        Assert.Same(b, battle.GetEntryProvince(german));
        Assert.Null(battle.GetEntryProvince(new Division("X", a, 100, 100)));
    }

    // ---- Zones ----

    [Fact]
    public void ArrivalFromTheEastPutsTheEnteringSideEastAndTheFacingRearWest()
    {
        // Deux voisines seulement : chaque bord couvre 180°, la limite de ±30° s'applique.
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var battle = Provincial(p, Arrive(around["E"], p, "R"));
        var field = cache.GetOrGenerate(battle);

        var zones = Compute(field, battle);

        Assert.Equal(new[] { around["E"] }, zones.EntryProvinces);
        Assert.All(zones.EnteringRearSectorIds, id =>
        {
            var (x, y) = Offset(field, id);
            Assert.True(Math.Abs(y) < WithinThirtyFiveDegrees * x, $"Secteur {id} hors du bord est : {x:F0}, {y:F0}");
        });
        Assert.All(zones.FacingRearSectorIds, id =>
        {
            var (x, y) = Offset(field, id);
            Assert.True(Math.Abs(y) < WithinThirtyFiveDegrees * -x, $"Secteur {id} hors du bord ouest : {x:F0}, {y:F0}");
        });
        Assert.NotEmpty(zones.FacingRearSectorIds);
    }

    [Fact]
    public void MeetingAtSixtyPercentIsFoughtNearTheBorderOfBFacingA()
    {
        // B n'a qu'une voisine, A, à l'ouest : son bord couvrirait tout le pourtour sans la limite de ±30°.
        var (b, around, cache) = World(("A", 270));
        var a = around["A"];
        var fromA = new Division("FA", a, 100, 100, country: Red);
        var fromB = new Division("FB", b, 100, 100, country: Blue);
        var plan = new MovementPlan(DurationHours: 5, FuelPerHour: 0);
        Assert.True(fromA.TryStartMovement(b, plan));
        Assert.True(fromB.TryStartMovement(a, plan));
        var battle = new Engagement(1, 0, a, b, 0.6);
        battle.AddParticipant(fromA, 0);
        battle.AddParticipant(fromB, 0);

        var field = cache.GetOrGenerate(battle);
        var zones = Compute(field, battle);

        Assert.Same(b, field.Province);
        Assert.Equal(new[] { fromA }, zones.EnteringDivisions);
        Assert.Equal(new[] { fromB }, zones.FacingDivisions);
        Assert.Equal(new[] { a }, zones.EntryProvinces);
        Assert.All(zones.EnteringRearSectorIds, id =>
        {
            var (x, y) = Offset(field, id);
            Assert.True(Math.Abs(y) < WithinThirtyFiveDegrees * -x, $"Secteur {id} hors du bord face à A : {x:F0}, {y:F0}");
        });
        Assert.All(zones.FacingRearSectorIds, id => Assert.True(Offset(field, id).X > 0));
    }

    [Fact]
    public void TwoEntryBordersFixTheFacingFrontAtBandThreeFromBoth()
    {
        var (p, around, cache) = World(("N", 0), ("E", 90), ("W", 270));
        var field = cache.GetOrGenerate(p);

        var east = Compute(field, Provincial(p, Arrive(around["E"], p, "RE")));
        var north = Compute(field, Provincial(p, Arrive(around["N"], p, "RN")));
        var both = Compute(field, Provincial(p, Arrive(around["E"], p, "RE"), Arrive(around["N"], p, "RN")));

        Assert.Equal(new[] { around["E"], around["N"] }, both.EntryProvinces);

        // Chaque bande est la plus petite distance aux deux bords ; le front d'en face est
        // exactement l'ensemble des grands secteurs à trois bandes du bord d'entrée le plus proche.
        foreach (int id in both.BattlefieldSectorIds)
            Assert.Equal(Math.Min(east.GetBand(id), north.GetBand(id)), both.GetBand(id));

        var expectedFront = both.BattlefieldSectorIds
            .Where(id => Math.Min(east.GetBand(id), north.GetBand(id)) == 3)
            .ToArray();

        Assert.Equal(expectedFront, both.FacingFrontSectorIds);
        Assert.Equal(3, EngagementDeploymentZones.FacingFrontBand);
        Assert.Equal(east.EnteringRearSectorIds.Union(north.EnteringRearSectorIds).Order(),
            both.EnteringRearSectorIds);
    }

    [Fact]
    public void TruncatedGrandSectorsBelongToTheWholeProvinceBattlefield()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var battle = Provincial(p, Arrive(around["E"], p, "R"));
        var field = cache.GetOrGenerate(battle);

        var zones = Compute(field, battle);

        Assert.Equal(field.Partition.Sectors.Select(s => s.Id), zones.BattlefieldSectorIds);
        Assert.Contains(zones.EnteringRearSectorIds, id => field.Partition.GetSector(id).IsPartial);
        foreach (var sector in field.Partition.Sectors.Where(s => s.IsPartial))
            Assert.InRange(zones.GetBand(sector.Id), 0, int.MaxValue);
    }

    [Fact]
    public void SmallProvinceOf15KmStillGetsAllZones()
    {
        var p = new Province(1, "P", TerrainType.Plains, Blue);
        var e = new Province(2, "E");
        p.ConnectTo(e);
        var descriptor = new ProvinceTerrainDescriptor(p.Id, Seed, TerrainType.Plains,
            new[] { new ProvinceNeighborBorder(e.Id, 90, 1) });

        // 75 colonnes de 200 m et 87 rangées de 173,2 m : environ 15 km sur 15 km.
        var grid = new ProvinceHexGrid(descriptor, 75, 87);
        ProvinceBorderLayout.Apply(grid);
        var field = new ProvinceBattlefieldGrid(p, grid, GrandSectorPartition.Build(grid));
        var battle = Provincial(p, Arrive(e, p, "R"));

        var zones = Compute(field, battle);

        Assert.NotEmpty(zones.FacingRearSectorIds);
        Assert.All(zones.FacingRearSectorIds, id => Assert.True(Offset(field, id).X < 0));
        Assert.True(zones.BattlefieldSectorIds.Count < 490 / 2);
    }

    [Fact]
    public void EntryBorderWithoutGrandSectorOnTheGridIsIgnoredThenRefused()
    {
        // P touche E et C, mais sa grille ne connaît que le bord de E.
        var p = new Province(1, "P", TerrainType.Plains, Blue);
        var e = new Province(2, "E");
        var c = new Province(3, "C");
        p.ConnectTo(e);
        p.ConnectTo(c);
        var grid = ProvinceTerrainGenerator.Generate(new ProvinceTerrainDescriptor(p.Id, Seed,
            TerrainType.Plains, new[] { new ProvinceNeighborBorder(e.Id, 90, 1) }));
        var field = new ProvinceBattlefieldGrid(p, grid, GrandSectorPartition.Build(grid));
        Assert.Empty(grid.GetBorderCells(c.Id));

        var onlyC = Provincial(p, Arrive(c, p, "RC"));
        Assert.False(EngagementDeploymentZones.TryCompute(field, onlyC, out var none, out var refusal));
        Assert.Null(none);
        Assert.Equal(DeploymentZoneRefusal.NoEntryBorder, refusal);

        var both = Compute(field, Provincial(p, Arrive(c, p, "RC"), Arrive(e, p, "RE")));
        Assert.Equal(new[] { e }, both.EntryProvinces);
        Assert.Equal(2, both.EnteringDivisions.Count);
    }

    [Fact]
    public void EngagementWithoutSidesIsRefusedWithoutException()
    {
        var (p, _, cache) = World(("E", 90));
        var battle = new Engagement(1, 0, p);
        battle.AddParticipant(new Division("R", p, 100, 100, country: Red), 0);
        battle.AddParticipant(new Division("B", p, 100, 100, country: Blue), 0);
        var field = cache.GetOrGenerate(battle);

        Assert.False(EngagementDeploymentZones.TryCompute(field, battle, out var zones, out var refusal));
        Assert.Null(zones);
        Assert.Equal(DeploymentZoneRefusal.MissingSide, refusal);

        // Une grille d'une autre province est une erreur d'appel, pas un refus.
        var (other, _, otherCache) = World(("E", 90));
        Assert.Throws<ArgumentException>(() => EngagementDeploymentZones.TryCompute(
            otherCache.GetOrGenerate(other), battle, out _, out _));
    }

    [Fact]
    public void ExtentLimitsTheBattlefieldToTheFirstBands()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var battle = Provincial(p, Arrive(around["E"], p, "R"));
        var field = cache.GetOrGenerate(battle);

        Assert.False(EngagementDeploymentZones.TryCompute(field, battle, out _, out var refusal, extentBands: 2));
        Assert.Equal(DeploymentZoneRefusal.BattlefieldTooShallow, refusal);

        var zones = Compute(field, battle, extent: 5);

        Assert.All(zones.BattlefieldSectorIds, id => Assert.InRange(zones.GetBand(id), 0, 5));
        Assert.True(zones.BattlefieldSectorIds.Count < field.Partition.Sectors.Count);
        Assert.NotEmpty(zones.FacingRearSectorIds);
        Assert.All(zones.FacingRearSectorIds, id => Assert.Equal(5, zones.GetBand(id)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(200)]
    [InlineData(90, 270)] // Défenseur pris entre deux entrées opposées.
    public void FrontOfOneSideIsNeverInTheRearOfTheOther(params int[] entryBearings)
    {
        var (p, around, cache) = World(entryBearings.Select(b => ($"V{b}", (double)b)).ToArray());
        var attackers = entryBearings.Select(b => Arrive(around[$"V{b}"], p, $"R{b}")).ToArray();
        var battle = Provincial(p, attackers);

        // AssertZonesAreConsistent vérifie les intersections, y compris front / arrière adverse.
        var zones = Compute(cache.GetOrGenerate(battle), battle);

        Assert.Equal(entryBearings.Length, zones.EntryProvinces.Count);
    }

    [Fact]
    public void SameInputsGiveTheSameZones()
    {
        EngagementDeploymentZones Run()
        {
            var (p, around, cache) = World(("N", 0), ("E", 90), ("W", 270));
            var battle = Provincial(p, Arrive(around["E"], p, "RE"), Arrive(around["N"], p, "RN"));
            return Compute(cache.GetOrGenerate(battle), battle);
        }

        var first = Run();
        var second = Run();

        Assert.NotSame(first.Field.Grid, second.Field.Grid);
        Assert.Equal(first.BattlefieldSectorIds, second.BattlefieldSectorIds);
        Assert.Equal(first.EnteringRearSectorIds, second.EnteringRearSectorIds);
        Assert.Equal(first.EnteringFrontSectorIds, second.EnteringFrontSectorIds);
        Assert.Equal(first.FacingFrontSectorIds, second.FacingFrontSectorIds);
        Assert.Equal(first.FacingRearSectorIds, second.FacingRearSectorIds);
        Assert.Equal(first.BattlefieldSectorIds.Select(first.GetBand),
            second.BattlefieldSectorIds.Select(second.GetBand));
    }
}