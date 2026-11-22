using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World.Terrain;
using static WoF.Simulation.Tests.Combat.Tactical.TacticalGridTestFactory;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalGridSectorPresenceTests
{
    [Fact]
    public void WideFront_ReferencesOneDeploymentInThreeCells()
    {
        var b = CreateBattle(CreateGrid(8, 8, (_, _) => 0));
        var company = Place(b, b.French, 0, 3, 2, facingDegrees: 0, frontageMeters: 300);
        var index = new TacticalSectorPresenceIndex(b.Battle, b.Map);

        // 50 m à gauche, 200 m au centre, 50 m à droite.
        for (int column = 2; column <= 4; column++)
        {
            var sector = b.Layout.GetSector(Cell(b.Grid, column, 2));
            Assert.Same(company, Assert.Single(index.GetCovering(sector)));
            if (column == 3)
                Assert.Same(company, Assert.Single(index.GetAssigned(sector)));
            else
                Assert.Empty(index.GetAssigned(sector));
        }
        Assert.Same(company, Assert.Single(b.Battle.Deployments));
    }

    [Fact]
    public void MoveOneMeter_ChangesNeighborCoverageAfterRebuild()
    {
        var b = CreateBattle(CreateGrid(8, 8, (_, _) => 0));
        var company = Place(b, b.French, 0, 3, 2, facingDegrees: 0, frontageMeters: 238);
        var east = b.Layout.GetSector(Cell(b.Grid, 4, 2));
        var west = b.Layout.GetSector(Cell(b.Grid, 2, 2));
        var before = new TacticalSectorPresenceIndex(b.Battle, b.Map);
        Assert.Empty(before.GetCovering(east)); // 19 m

        var center = b.Layout.GetCellCenter(company.Sector);
        var next = b.Map.GetGroundPoint(company.Sector, center.XMeters + 1, center.YMeters);
        b.Map.RelocateCompany(company, company.Sector, next);

        Assert.Throws<InvalidOperationException>(() => before.GetCovering(east));
        var after = new TacticalSectorPresenceIndex(b.Battle, b.Map);
        Assert.Same(company, Assert.Single(after.GetCovering(east))); // 20 m
        Assert.Empty(after.GetCovering(west));                       // 18 m
    }

    [Fact]
    public void ShortFront_KeepsAssignmentWithoutCoveringItsCell()
    {
        var b = CreateBattle(CreateGrid(8, 8, (_, _) => 0));
        var company = Place(b, b.French, 0, 3, 2, frontageMeters: 15);
        var index = new TacticalSectorPresenceIndex(b.Battle, b.Map);

        Assert.Same(company, Assert.Single(index.GetAssigned(company.Sector)));
        Assert.Empty(index.GetCovering(company.Sector));
        Assert.Equal(0, index.GetCoverageCount(company.Sector));
    }

    [Fact]
    public void LongFront_StopsAtTheLimitOfAPartialBattlefield()
    {
        var b = CreateBattle(CreateGrid(32, 8, (_, _) => 0));
        var grid = b.Grid;
        var partition = b.Layout.Partition;
        var layout = TacticalBattlefieldBuilder.Build(grid, partition,
            [partition.GetSectorOfCell(0).Id]);
        var battle = new TacticalBattle(b.Battle.Engagement, layout.Battlefield);
        var map = new TacticalGeometryMap(battle);
        layout.DefineGeometry(map);

        // Cette rangée continue dans la province au-delà du champ construit.
        int edge = layout.CellIndices.Last(c => grid.GetRow(c) == 0);
        int outside = grid.GetNeighborIndex(edge, 0);
        Assert.True(outside >= 0);
        Assert.False(layout.TryGetSector(outside, out _));

        var primary = layout.GetSector(edge);
        var company = battle.DeployCompany(b.French.Battalions[0].Companies[0],
            primary, CompanyDeploymentRole.FrontLine);
        map.PlaceCompany(company, layout.GetCellCenter(primary), 0, 800);

        var index = new TacticalSectorPresenceIndex(battle, map);
        var covered = layout.CellIndices.Where(c =>
            index.GetCovering(layout.GetSector(c)).Contains(company));
        Assert.Equal(new[] { edge - 2, edge - 1, edge }, covered);
    }
}