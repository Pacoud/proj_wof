using WoF.Simulation.Tests.Combat.Tactical;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.World.Terrain;

/// <summary>
/// Positions continues sur la grille : case qui contient un point,
/// altitude du sol en un point quelconque.
/// </summary>
public sealed class ProvinceHexGridGroundTests
{
    [Fact]
    public void CellAt_ReturnsTheCellWhoseHexagonContainsThePoint()
    {
        var grid = ProvinceTerrainGenerator.Generate(
            ProvinceTerrainGeneratorTests.Descriptor(worldSeed: 1));

        foreach (int cell in new[] { 0, 1, grid.Columns, grid.Columns + 1, grid.CellCount / 2, grid.CellCount - 1 })
        {
            double x = grid.GetCenterXMeters(cell);
            double y = grid.GetCenterYMeters(cell);

            Assert.True(grid.TryGetCellAt(x, y, out int found));
            Assert.Equal(cell, found);

            for (int d = 0; d < HexCoord.DirectionCount; d++)
            {
                // Directions des voisins : 0° = est, puis tous les 60°.
                double angle = d * Math.PI / 3;
                double ux = Math.Cos(angle);
                double uy = Math.Sin(angle);

                // Le bord de la case est à 100 m du centre dans ces directions.
                Assert.True(grid.TryGetCellAt(x + 95 * ux, y + 95 * uy, out int inside));
                Assert.Equal(cell, inside);

                int neighbor = grid.GetNeighborIndex(cell, d);

                if (neighbor >= 0)
                {
                    Assert.True(grid.TryGetCellAt(x + 105 * ux, y + 105 * uy, out int across));
                    Assert.Equal(neighbor, across);
                }
            }
        }

        Assert.False(grid.TryGetCellAt(-150, -150, out _));
        Assert.False(grid.TryGetCellAt(1e7, 0, out _));
    }

    [Fact]
    public void GroundElevation_IsExactlyTheCellElevationAtEachCenter()
    {
        var grid = ProvinceTerrainGenerator.Generate(
            ProvinceTerrainGeneratorTests.Descriptor(
                TerrainType.Mountain, worldSeed: 1));

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            Assert.True(grid.TryGetGroundElevation(
                grid.GetCenterXMeters(cell),
                grid.GetCenterYMeters(cell),
                out double elevation));

            Assert.Equal((double)grid.GetElevation(cell), elevation);
        }
    }

    [Fact]
    public void GroundElevation_FollowsARegularSlopeExactly()
    {
        // Plan incliné : 4 % vers l'est, 7 % vers le nord.
        static double Plane(double x, double y) => 50 + 0.04 * x + 0.07 * y;

        var grid = TacticalGridTestFactory.CreateGrid(20, 12, Plane);

        // Points quelconques, y compris près des bords de cases et des sommets.
        var points = new (double X, double Y)[]
        {
            (510, 260), (733.3, 401.7), (1000, 866.0254), (1100.5, 950.2), (2900, 1500), (37, 12)
        };

        foreach (var (x, y) in points)
        {
            Assert.True(grid.TryGetGroundElevation(x, y, out double elevation));

            // Les altitudes des cases sont stockées en float.
            Assert.Equal(Plane(x, y), elevation, 3);
        }
    }

    [Fact]
    public void GroundElevation_IsContinuousAlongASegment()
    {
        // Relief irrégulier : aucune marche entre deux points proches.
        var grid = TacticalGridTestFactory.CreateGrid(
            20, 12, (x, y) => 40 * Math.Sin(x / 230) + 25 * Math.Cos(y / 170));

        Assert.True(grid.TryGetGroundElevation(300, 300, out double previous));

        for (int i = 1; i <= 2000; i++)
        {
            double x = 300 + i * 1.3;
            double y = 300 + i * 0.4;

            Assert.True(grid.TryGetGroundElevation(x, y, out double current));

            // Pente maximale du relief < 0,3 : moins de 0,5 m sur un pas de 1,36 m.
            Assert.True(Math.Abs(current - previous) < 0.5);
            previous = current;
        }
    }

    [Fact]
    public void GroundElevation_AtTheFrameEdge_UsesTheExistingCellsOnly()
    {
        var grid = TacticalGridTestFactory.CreateGrid(10, 6, (_, _) => 120);

        // Au sud de la première rangée : seuls des centres de la rangée 0 existent.
        Assert.True(grid.TryGetGroundElevation(300, -60, out double edge));
        Assert.Equal(120, edge, 6);

        // Loin du cadre : aucun centre de case autour du point.
        Assert.False(grid.TryGetGroundElevation(-1000, -1000, out _));
        Assert.False(grid.TryGetGroundElevation(500, 1e6, out _));
    }
}
