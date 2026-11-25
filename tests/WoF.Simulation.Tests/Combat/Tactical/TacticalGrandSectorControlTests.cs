using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;
using static WoF.Simulation.Tests.Combat.Tactical.TacticalGridTestFactory;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalGrandSectorControlTests
{
    [Fact]
    public void FullSector_RequiresTheLastCell_AndRemembersTheWinner()
    {
        var b = CreateBattle(CreateGrid(20, 20, (_, _) => 0));
        var sector = b.Layout.Partition.Sectors.First(s => !s.IsPartial);
        Assert.Equal(37, sector.Cells.Count);
        var original = b.German.Country!;
        var claimant = b.French.Country!;
        var control = new TacticalGrandSectorControl(original);
        var holders = sector.Cells.ToDictionary(b.Layout.GetSector, _ => (Country?)claimant);
        var last = b.Layout.GetSector(sector.Cells[^1]);

        void Update() => control.Update(b.Layout, s => holders.GetValueOrDefault(s, original));

        holders[last] = original;
        Update();
        Assert.Same(original, control.GetController(b.Layout, sector)); // 36 sur 37.
        holders[last] = null;
        Update();
        Assert.Same(original, control.GetController(b.Layout, sector)); // Une case sans détenteur.
        holders[last] = claimant;
        Update();
        Assert.Same(claimant, control.GetController(b.Layout, sector));
        holders[last] = original;
        Update();
        Assert.Same(claimant, control.GetController(b.Layout, sector)); // Reprise partielle.
        foreach (var cell in sector.Cells)
            holders[b.Layout.GetSector(cell)] = original;
        Update();
        Assert.Same(original, control.GetController(b.Layout, sector));
    }

    [Fact]
    public void UnheldSectorWaitsForTheHourlyPassAndFullControl()
    {
        var b = CreateBattle(CreateGrid(2, 2, (_, _) => 0));
        var sector = Assert.Single(b.Layout.Partition.Sectors);
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        Country? Holder() => control.GetGrandSectorController(b.Layout, sector);

        Assert.Null(Holder());
        Place(b, b.French, 0, 0, 0);
        Assert.Equal((1, 0), control.ApplyHourlyControl(2, b.Layout));
        Assert.Null(Holder());
        Place(b, b.French, 1, 1, 0);
        Place(b, b.French, 2, 0, 1);
        Place(b, b.French, 3, 1, 1);
        Assert.Equal(3, control.ApplyOccupation());
        Assert.Null(Holder()); // Les quatre cases sont désormais prises par occupation.
        Assert.Equal((0, 0), control.ApplyHourlyControl(3, b.Layout));
        Assert.Same(b.French.Country, Holder());
        Assert.Null(b.Battle.Engagement.Province!.Controller);
    }

    [Fact]
    public void PartialNeighborUsesItsActualCells_AndLookupRejectsForeignSectors()
    {
        var grid = CreateGrid(40, 32, (_, _) => 0);
        var b = CreateBattle(grid);
        var partition = b.Layout.Partition;
        var border = partition.Sectors.First(s => s.IsPartial);
        int requested = border.NeighborIds[0];
        var layout = TacticalBattlefieldBuilder.Build(grid, partition, [requested]);
        Assert.DoesNotContain(border.Id, layout.RequestedGrandSectorIds);
        Assert.Contains(border.Id, layout.IncludedGrandSectorIds);
        var outside = partition.Sectors.First(s => !layout.IncludedGrandSectorIds.Contains(s.Id));

        var state = new TacticalGrandSectorControl(b.German.Country);
        var held = border.Cells.Select(layout.GetSector).ToHashSet();
        state.Update(layout, small => held.Contains(small) ? b.French.Country : b.German.Country);
        Assert.Same(b.French.Country, state.GetController(layout, border));
        Assert.Same(b.German.Country, state.GetController(layout, partition.GetSector(requested)));
        Assert.Throws<InvalidOperationException>(() => state.GetController(layout, outside));

        var publicControl = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        var foreign = GrandSectorPartition.Build(grid).GetSector(border.Id);
        Assert.Throws<InvalidOperationException>(() => publicControl.GetGrandSectorController(b.Layout, foreign));
        Assert.Throws<InvalidOperationException>(() => publicControl.GetGrandSectorController(layout, border));
    }

    [Fact]
    public void LastCellEncircledChangesTheGrandSectorThatHour()
    {
        var b = CreateBattle(CreateGrid(3, 3, (_, _) => 0));
        var sector = Assert.Single(b.Layout.Partition.Sectors);
        var province = b.Battle.Engagement.Province!;
        province.ChangeController(b.German.Country);
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        var guard = Place(b, b.German, 0, 1, 1);
        var patrol = Place(b, b.French, 0, 0, 0);

        // Les huit cases du pourtour conservent leur détenteur après le passage
        // de la patrouille. Une garde empêche encore la prise de la case centrale.
        foreach (int cell in sector.Cells)
        {
            var destination = b.Layout.GetSector(cell);
            if (ReferenceEquals(destination, guard.Sector))
                continue;

            patrol.ApplyMovementArrival(destination, patrol.Role);
            b.Map.RelocateCompany(patrol, destination, b.Layout.GetCellCenter(destination));
            control.ApplyOccupation();
        }

        Assert.Equal((0, 0), control.ApplyHourlyControl(2, b.Layout));
        Assert.Same(b.German.Country, control.GetGrandSectorController(b.Layout, sector));
        guard.SetInTransit(true);
        Assert.Equal((0, 1), control.ApplyHourlyControl(3, b.Layout));
        Assert.Same(b.French.Country, control.GetController(guard.Sector));
        Assert.Same(b.French.Country, control.GetGrandSectorController(b.Layout, sector));
        Assert.Same(b.German.Country, province.Controller);
    }
}
