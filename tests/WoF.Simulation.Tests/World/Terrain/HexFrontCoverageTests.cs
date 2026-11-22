using WoF.Simulation.Tests.Combat.Tactical;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.World.Terrain;

public sealed class HexFrontCoverageTests
{
    private static ProvinceHexGrid Grid() =>
        TacticalGridTestFactory.CreateGrid(8, 8, (_, _) => 0);

    [Fact]
    public void HorizontalFront_FiltersEachCellSeparately()
    {
        // 19 m, 200 m, puis 20 m sur la rangée 0.
        var grid = Grid();
        var cells = HexFrontCoverage.Measure(grid, (281, 0), (520, 0));
        Assert.Equal(new[] { new CoveredCell(2, 200), new CoveredCell(3, 20) }, cells);

        var shorter = HexFrontCoverage.Measure(grid, (281, 0), (519.99, 0));
        Assert.Equal(new CoveredCell(2, 200), Assert.Single(shorter));
    }

    [Fact]
    public void ObliqueFront_IncludesIntermediateCellsInEitherDirection()
    {
        var grid = Grid();
        int start = 2 * grid.Columns + 2;
        int middle = grid.GetNeighborIndex(start, 2);
        int end = grid.GetNeighborIndex(middle, 2);
        var a = (grid.GetCenterXMeters(start), grid.GetCenterYMeters(start));
        var b = (grid.GetCenterXMeters(end), grid.GetCenterYMeters(end));

        var cells = HexFrontCoverage.Measure(grid, a, b);
        Assert.Equal(new[] { start, middle, end }, cells.Select(c => c.CellIndex));
        Assert.Equal(100, cells[0].LengthMeters, 9);
        Assert.Equal(200, cells[1].LengthMeters, 9);
        Assert.Equal(100, cells[2].LengthMeters, 9);
        Assert.Equal(cells.ToArray(), HexFrontCoverage.Measure(grid, b, a).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SharedEdge_IsCountedOnce_AndAnOffsetChangesSide(int direction)
    {
        var grid = Grid();
        int cell = 5 * grid.Columns + 3;
        int neighbor = grid.GetNeighborIndex(cell, direction);
        double x = grid.GetCenterXMeters(cell), y = grid.GetCenterYMeters(cell);
        double nx = (grid.GetCenterXMeters(neighbor) - x) / 200;
        double ny = (grid.GetCenterYMeters(neighbor) - y) / 200;

        CoveredCell AlongEdge(double offset)
        {
            double mx = x + (100 + offset) * nx, my = y + (100 + offset) * ny;
            var a = (mx - 30 * ny, my + 30 * nx);
            var b = (mx + 30 * ny, my - 30 * nx);
            var hit = Assert.Single(HexFrontCoverage.Measure(grid, a, b));
            Assert.Equal(hit, Assert.Single(HexFrontCoverage.Measure(grid, b, a)));
            Assert.Equal(60, hit.LengthMeters, 9);
            return hit;
        }

        Assert.Equal(Math.Min(cell, neighbor), AlongEdge(0).CellIndex);
        Assert.Equal(cell, AlongEdge(-0.000001).CellIndex);
        Assert.Equal(neighbor, AlongEdge(0.000001).CellIndex);
    }

    [Theory]
    [InlineData(120, true)]
    [InlineData(119.99, false)]
    public void ObliqueEntry_AppliesTheThreshold(double distance, bool reachesNeighbor)
    {
        var grid = Grid();
        int cell = 5 * grid.Columns + 3;
        double x = grid.GetCenterXMeters(cell), y = grid.GetCenterYMeters(cell);
        var cells = HexFrontCoverage.Measure(grid, (x, y),
            (x + distance / 2, y + distance * HexCoord.RowSpacingFactor));
        Assert.Equal(reachesNeighbor ? 2 : 1, cells.Count);
        if (reachesNeighbor)
            Assert.Equal(20, cells[1].LengthMeters, 9);
    }

    [Fact]
    public void OuterEdge_AndFrontWithBothEndsOutside()
    {
        var grid = Grid();
        // Bord ouest de la case 0, sur lequel il n'existe aucune autre case.
        Assert.Equal(new CoveredCell(0, 60),
            Assert.Single(HexFrontCoverage.Measure(grid, (-100, -30), (-100, 30))));

        var cells = HexFrontCoverage.Measure(grid, (-400, 0), (2000, 0));
        Assert.Equal(Enumerable.Range(0, grid.Columns), cells.Select(c => c.CellIndex));
        Assert.All(cells, c => Assert.Equal(200, c.LengthMeters, 9));
    }

    [Fact]
    public void Vertex_Graze_DegenerateAndOutsideFronts()
    {
        var grid = Grid();
        double tip = ProvinceHexGrid.CircumradiusMeters;
        // Sommet sud de la case 0, puis passage de quelques mètres dans la pointe.
        Assert.Empty(HexFrontCoverage.Measure(grid, (-30, -tip), (30, -tip)));
        Assert.Empty(HexFrontCoverage.Measure(grid, (-30, 1 - tip), (30, 1 - tip)));
        Assert.Empty(HexFrontCoverage.Measure(grid, (0, 0), (0, 0)));
        Assert.Empty(HexFrontCoverage.Measure(grid, (-500, -500), (-200, -200)));
        Assert.Equal(new CoveredCell(0, 20),
            Assert.Single(HexFrontCoverage.Measure(grid, (0, 0), (12, 16))));
    }
}