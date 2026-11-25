using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;
using static WoF.Simulation.Tests.Combat.Tactical.TacticalGridTestFactory;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalBattlefieldControlGaugeTests
{
    [Fact]
    public void TruncatedSectorCannotSwingTheZoneByHeadcount()
    {
        var b = CreateBattle(CreateGrid(7, 7, (_, _) => 0));
        var large = Assert.Single(b.Layout.Partition.Sectors, s => !s.IsPartial);
        var small = b.Layout.Partition.Sectors.Where(s => s.IsPartial).ToArray();
        Assert.Equal(4, small.Length);
        var challenger = b.French.Country!;
        var incumbent = b.German.Country!;
        var gauge = new TacticalBattlefieldControlGauge(incumbent);
        var taken = small.Take(2).ToHashSet();
        gauge.Update(b.Layout, s => taken.Contains(s) ? challenger : incumbent);

        // Ce troisième secteur tronqué donne 3 secteurs sur 5, mais les quatre
        // tronqués réunis ne représentent que 12 des 49 cases du champ.
        taken.Add(small[2]);
        gauge.Update(b.Layout, s => taken.Contains(s) ? challenger : incumbent);
        Assert.Equal(37, large.Cells.Count);
        Assert.Equal(49, gauge.TotalWeight);
        Assert.Equal(taken.Sum(s => s.Cells.Count), gauge.GetWeight(challenger));
        Assert.Same(incumbent, gauge.Controller);

        // Le seul secteur complet suffit ici à renverser la jauge.
        gauge.Update(b.Layout, s => s == large ? challenger : incumbent);
        Assert.Equal(37, gauge.GetWeight(challenger));
        Assert.Same(challenger, gauge.Controller);
    }

    [Fact]
    public void FirstAwardNeedsSixtyPercentIncludingUnheldGround()
    {
        var b = CreateBattle(CreateGrid(10, 10, (_, _) => 0));
        var side = b.French.Country!;
        var gauge = new TacticalBattlefieldControlGauge(null);
        // Sur ce champ de 100 cases, ces tailles existent une fois chacune.
        var sizes = new HashSet<int> { 37, 18, 4 };
        void Update() => gauge.Update(b.Layout, s => sizes.Contains(s.Cells.Count) ? side : null);

        Update();
        Assert.Equal(100, gauge.TotalWeight);
        Assert.Equal(59, gauge.GetWeight(side));
        Assert.Null(gauge.Controller);
        sizes = new HashSet<int> { 37, 15, 8 };
        Update();
        Assert.Equal(60, gauge.GetWeight(side));
        Assert.Same(side, gauge.Controller);
        sizes.Clear();
        Update();
        Assert.Equal(0, gauge.GetWeight(side));
        Assert.Equal(100, gauge.TotalWeight);
        Assert.Null(gauge.Controller);
    }

    [Fact]
    public void FortyRetainsTheZone_BelowFortySelectsTheLeader()
    {
        var b = CreateBattle(CreateGrid(10, 10, (_, _) => 0));
        var a = b.French.Country!;
        var bSide = b.German.Country!;
        var third = new Country(3, "Troisième pays");
        var gauge = new TacticalBattlefieldControlGauge(a);
        // Tailles du découpage : 37, 18, 15, 9, 9, 8 et 4.
        gauge.Update(b.Layout, s => s.Cells.Count switch
        {
            37 or 15 => bSide,
            8 => third,
            _ => a
        });
        Assert.Equal(40, gauge.GetWeight(a));
        Assert.Equal(52, gauge.GetWeight(bSide));
        Assert.Same(a, gauge.Controller);

        var nine = b.Layout.Partition.Sectors.First(s => s.Cells.Count == 9);
        gauge.Update(b.Layout, s => s.Cells.Count is 37 or 15 ? bSide : s == nine ? third : a);
        Assert.Equal(39, gauge.GetWeight(a));
        Assert.Equal(52, gauge.GetWeight(bSide));
        Assert.Same(bSide, gauge.Controller);

        gauge.Update(b.Layout, s => s.Cells.Count is 37 or 15 or 8 ? a : bSide);
        Assert.Equal(40, gauge.GetWeight(bSide));
        Assert.Equal(60, gauge.GetWeight(a));
        Assert.Same(a, gauge.Controller); // La prise à 60 % est prioritaire.
    }

    [Fact]
    public void EqualLeadersAreSeparatedByCountryId()
    {
        var b = CreateBattle(CreateGrid(10, 10, (_, _) => 0));
        var first = b.Layout.Partition.Sectors.First(s => s.Cells.Count == 9);
        var low = b.French.Country!;
        var high = b.German.Country!;
        foreach (bool swap in new[] { false, true })
        {
            // Chaque pays pèse 50 cases. Repartir du tiers à zéro pour exercer
            // le départage dans les deux ordres de rencontre des prétendants.
            var gauge = new TacticalBattlefieldControlGauge(new Country(3, "Détenteur précédent"));
            gauge.Update(b.Layout, s => ((s.Cells.Count is 37 or 4 || s == first) ^ swap) ? low : high);
            Assert.Equal(50, gauge.GetWeight(low));
            Assert.Equal(50, gauge.GetWeight(high));
            Assert.Same(low, gauge.Controller);
        }
    }

    [Fact]
    public void OnlyIncludedSectorsContributeEvenOnAPartialBattlefield()
    {
        var b = CreateBattle(CreateGrid(40, 32, (_, _) => 0));
        var layout = TacticalBattlefieldBuilder.Build(b.Grid, b.Layout.Partition,
            [b.Layout.Partition.GetSectorOfCell(0).Id]);
        var visited = new List<int>();
        var gauge = new TacticalBattlefieldControlGauge(null);
        gauge.Update(layout, s => { visited.Add(s.Id); return b.French.Country; });
        Assert.Equal(layout.IncludedGrandSectorIds, visited);
        Assert.True(layout.IncludedGrandSectorIds.Count > layout.RequestedGrandSectorIds.Count);
        Assert.True(layout.CellIndices.Count < b.Grid.CellCount);
        Assert.Equal(layout.CellIndices.Count, gauge.GetWeight(b.French.Country!));
        Assert.Equal(layout.CellIndices.Count, gauge.TotalWeight);
    }

    [Fact]
    public void HourlyCaptureAndRecaptureLeaveTheProvinceWithItsInitialController()
    {
        var b = CreateBattle(CreateGrid(2, 2, (_, _) => 0));
        var province = b.Battle.Engagement.Province!;
        province.ChangeController(b.German.Country);
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        var gauge = control.BattlefieldGauge;
        Assert.Same(b.German.Country, gauge.Controller);

        var french = Enumerable.Range(0, 4).Select(i => Place(b, b.French, i, i % 2, i / 2)).ToArray();
        Assert.Equal((4, 0), control.ApplyHourlyControl(2, b.Layout));
        Assert.Equal(4, gauge.GetWeight(b.French.Country!));
        Assert.Same(b.French.Country, gauge.Controller);
        Assert.Same(b.German.Country, province.Controller);

        for (int i = 0; i < french.Length; i++)
        {
            french[i].SetInTransit(true);
            Place(b, b.German, i, i % 2, i / 2);
        }

        Assert.Equal((4, 0), control.ApplyHourlyControl(3, b.Layout));
        Assert.Same(b.German.Country, gauge.Controller);
        Assert.Same(b.German.Country, province.Controller);
    }
}