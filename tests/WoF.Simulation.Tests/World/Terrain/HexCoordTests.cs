using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.World.Terrain;

public sealed class HexCoordTests
{
    [Fact]
    public void Neighbors_AreAtDistanceOneAndTwoHundredMetres()
    {
        var origin = new HexCoord(4, -2);

        for (int d = 0; d < HexCoord.DirectionCount; d++)
        {
            var neighbor = origin.Neighbor(d);

            Assert.Equal(1, origin.DistanceTo(neighbor));
            Assert.Equal(origin, neighbor.Neighbor(HexCoord.Opposite(d)));

            double distance = double.Hypot(
                neighbor.CenterXMeters(200) - origin.CenterXMeters(200),
                neighbor.CenterYMeters(200) - origin.CenterYMeters(200));

            Assert.Equal(200, distance, 9);
        }
    }

    [Fact]
    public void Directions_FollowTheDocumentedBearings()
    {
        var origin = new HexCoord(0, 0);

        // 0 = est, 1 = nord-est, 2 = nord-ouest, 3 = ouest, 4 = sud-ouest, 5 = sud-est.
        double[] expectedMathAngles = { 0, 60, 120, 180, 240, 300 };

        for (int d = 0; d < HexCoord.DirectionCount; d++)
        {
            var n = origin.Neighbor(d);

            double angle = Math.Atan2(n.CenterYMeters(200), n.CenterXMeters(200))
                * 180 / Math.PI;

            Assert.Equal(expectedMathAngles[d], (angle + 360) % 360, 9);
        }
    }

    [Fact]
    public void Distance_CountsCellSteps()
    {
        Assert.Equal(0, new HexCoord(2, 3).DistanceTo(new HexCoord(2, 3)));
        Assert.Equal(3, new HexCoord(0, 0).DistanceTo(new HexCoord(3, -3)));
        Assert.Equal(7, new HexCoord(0, 0).DistanceTo(new HexCoord(7, -3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HexCoord(0, 0).Neighbor(6));
    }
}
