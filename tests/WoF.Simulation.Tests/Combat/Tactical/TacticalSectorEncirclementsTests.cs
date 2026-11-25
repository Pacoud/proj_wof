using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;
using static WoF.Simulation.Tests.Combat.Tactical.TacticalGridTestFactory;
using CellState = WoF.Simulation.Military.Combat.Tactical.TacticalSectorEncirclement.CellState;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalSectorEncirclementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoEmptyCellsFallTogether_UnlessTheyTouchAFriendlyCompany(bool friendlyStays)
    {
        var grid = CreateGrid(9, 7, (_, _) => 0);
        int west = Cell(grid, 3, 3);
        int east = Cell(grid, 4, 3);
        // Rivières à l'intérieur de la poche et sur son pourtour.
        grid.SetRiverEdge(grid.GetCoord(west), 0);
        grid.SetRiverEdge(grid.GetCoord(east), 0);
        var b = CreateBattle(grid);
        var province = b.Battle.Engagement.Province!;
        province.ChangeController(b.German.Country);
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);

        var westCompany = Place(b, b.French, 0, 3, 3);
        var eastCompany = Place(b, b.French, 1, 4, 3);
        var guard = Place(b, b.French, 2, 5, 3);
        Assert.False(b.Layout.Battlefield.AreConnected(westCompany.Sector, eastCompany.Sector));
        Assert.False(b.Layout.Battlefield.AreConnected(eastCompany.Sector, guard.Sector));
        Assert.Equal(3, control.ApplyOccupation());

        westCompany.SetInTransit(true);
        eastCompany.SetInTransit(true);
        guard.SetRouted(true);
        guard.SetInTransit(!friendlyStays);
        Place(b, b.German, 0, 5, 3); // Peut prendre le dernier côté si le garde est parti.

        var result = control.ApplyHourlyControl(2, b.Layout);
        Assert.Equal(friendlyStays ? (0, 0) : (1, 2), result);
        var expected = friendlyStays ? b.French.Country : b.German.Country;
        Assert.Same(expected, control.GetController(westCompany.Sector));
        Assert.Same(expected, control.GetController(eastCompany.Sector));
        Assert.Same(b.German.Country, province.Controller);
    }

    [Fact]
    public void RoutedCenterRemainsOccupied_UntilItsManpowerIsGone()
    {
        var b = CreateBattle(CreateGrid(7, 7, (_, _) => 0));
        b.Battle.Engagement.Province!.ChangeController(b.German.Country);
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        var company = Place(b, b.French, 0, 3, 3);
        Assert.Equal(1, control.ApplyOccupation());
        company.SetRouted(true);

        Assert.Equal((0, 0), control.ApplyHourlyControl(2, b.Layout));
        Assert.Same(b.French.Country, control.GetController(company.Sector));
        var pool = company.Company.Composition!.Manpower;
        pool.ApplyCasualties(new PersonnelCasualtyReport(0, pool.Current, 0));
        Assert.Equal((0, 1), control.ApplyHourlyControl(3, b.Layout));
        Assert.Same(b.German.Country, control.GetController(company.Sector));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenEdgeProtectsTheWholePocket(bool partialField)
    {
        var grid = CreateGrid(40, 32, (_, _) => 0);
        var partition = GrandSectorPartition.Build(grid);
        var ids = partialField
            ? new[] { partition.GetSectorOfCell(Cell(grid, 20, 16)).Id }
            : partition.Sectors.Select(s => s.Id).ToArray();
        var layout = TacticalBattlefieldBuilder.Build(grid, partition, ids);
        var (holder, enemy, wars) = Opponents();
        var cells = layout.CellIndices.ToDictionary(i => i, _ => new CellState(enemy, true));

        bool IsInside(int cell) => cells.ContainsKey(cell);
        bool IsInterior(int cell) => IsInside(cell) && Neighbors(grid, cell).All(IsInside);
        // Pour un champ partiel, la voisine manquante existe bien dans la province.
        int edge = layout.CellIndices.First(i => Neighbors(grid, i).Any(n =>
            !IsInside(n) && (!partialField || n >= 0)) && Neighbors(grid, i).Any(IsInterior));
        int inner = Neighbors(grid, edge).First(IsInterior);
        cells[edge] = cells[inner] = new CellState(holder, false);

        Assert.Empty(TacticalSectorEncirclement.FindTransfers(layout, cells, wars));
    }

    [Fact]
    public void ThreeCountries_CountDistinctBorderCells_ThenBreakTiesById()
    {
        var b = CreateBattle(CreateGrid(8, 7, (_, _) => 0));
        var low = b.French.Country!;
        var high = b.German.Country!;
        var holder = new Country(3, "Détenteur");
        var wars = b.Diplomacy;
        wars.DeclareWar(holder, low);
        wars.DeclareWar(holder, high);
        int west = Cell(b.Grid, 3, 3);
        int east = Cell(b.Grid, 4, 3);
        var cells = b.Layout.CellIndices.ToDictionary(i => i, _ => new CellState(low, true));
        cells[west] = cells[east] = new CellState(holder, false);
        var shared = Neighbors(b.Grid, west).Intersect(Neighbors(b.Grid, east)).ToArray();
        var single = Neighbors(b.Grid, west).Union(Neighbors(b.Grid, east))
            .Except(shared).Where(i => i != west && i != east).Order().ToArray();
        Assert.Equal(2, shared.Length);
        Assert.Equal(6, single.Length);

        void Expect(Country winner)
        {
            var taken = TacticalSectorEncirclement.FindTransfers(b.Layout, cells, wars);
            Assert.Equal(new[] { west, east }, taken.Select(t => t.Cell).Order());
            Assert.All(taken, t => Assert.Same(winner, t.Winner));
        }

        foreach (int i in shared.Concat(single.Take(2)))
            cells[i] = new CellState(high, true);
        Expect(low);  // 4 contre 4. Compter les eastôtés donnerait 6 voix à high.
        cells[single[2]] = new CellState(high, true);
        Expect(high); // 5 contre 3, malgré l'identifiant plus grand.

        wars.MakePeace(holder, low);
        Assert.Empty(TacticalSectorEncirclement.FindTransfers(b.Layout, cells, wars));
    }

    [Fact]
    public void UnheldInteriorOrUnheldBorderPreventsCapture()
    {
        var b = CreateBattle(CreateGrid(7, 7, (_, _) => 0));
        var cells = b.Layout.CellIndices.ToDictionary(i => i, _ => new CellState(b.German.Country, true));
        int center = Cell(b.Grid, 3, 3);
        cells[center] = new CellState(null, false);
        Assert.Empty(TacticalSectorEncirclement.FindTransfers(b.Layout, cells, b.Diplomacy));

        cells[center] = new CellState(b.French.Country, false);
        cells[b.Grid.GetNeighborIndex(center, 0)] = new CellState(null, false);
        Assert.Empty(TacticalSectorEncirclement.FindTransfers(b.Layout, cells, b.Diplomacy));
    }

    [Fact]
    public void RingAndCenterChangeSimultaneously()
    {
        var b = CreateBattle(CreateGrid(9, 9, (_, _) => 0));
        var inside = b.German.Country!;
        var ringOwner = b.French.Country!;
        var cells = b.Layout.CellIndices.ToDictionary(i => i, _ => new CellState(inside, false));
        int center = Cell(b.Grid, 4, 4);
        var ring = Neighbors(b.Grid, center).ToArray();
        foreach (int i in ring)
            cells[i] = new CellState(ringOwner, false);

        // Centre B, anneau A, extérieur B ouvert sur le bord. Les deux poches
        // intérieures doivent échanger de pays en lisant la même carte.
        var changes = TacticalSectorEncirclement.FindTransfers(b.Layout, cells, b.Diplomacy)
            .ToDictionary(t => t.Cell, t => t.Winner);
        Assert.Equal(7, changes.Count);
        Assert.Same(ringOwner, changes[center]);
        Assert.All(ring, i => Assert.Same(inside, changes[i]));
        Assert.Same(inside, cells[center].Holder);
        Assert.All(ring, i => Assert.Same(ringOwner, cells[i].Holder));
    }

    [Fact]
    public void InvalidLayoutOrRepeatedHourFailsBeforeOccupation()
    {
        var grid = CreateGrid(7, 7, (_, _) => 0);
        var b = CreateBattle(grid);
        var other = CreateBattle(grid);
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        var french = Place(b, b.French, 0, 3, 3);

        Assert.Throws<InvalidOperationException>(() => control.ApplyHourlyControl(3, other.Layout));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            control.ApplyHourlyControl(b.Battle.Engagement.StartHour - 1, b.Layout));
        Assert.Null(control.GetController(french.Sector));
        Assert.Equal((1, 0), control.ApplyHourlyControl(3, b.Layout));

        var german = Place(b, b.German, 0, 5, 4);
        Assert.Throws<InvalidOperationException>(() => control.ApplyHourlyControl(3, b.Layout));
        Assert.Throws<InvalidOperationException>(() => control.ApplyHourlyControl(2, b.Layout));
        Assert.Null(control.GetController(german.Sector));
        Assert.Equal((1, 0), control.ApplyHourlyControl(4, b.Layout));
    }

    private static IEnumerable<int> Neighbors(ProvinceHexGrid grid, int cell) =>
        Enumerable.Range(0, HexCoord.DirectionCount).Select(d => grid.GetNeighborIndex(cell, d));

    private static (Country Holder, Country Enemy, DiplomacySystem Wars) Opponents()
    {
        var holder = new Country(1, "Détenteur");
        var enemy = new Country(2, "Voisin");
        var wars = new DiplomacySystem();
        wars.DeclareWar(holder, enemy);
        return (holder, enemy, wars);
    }
}