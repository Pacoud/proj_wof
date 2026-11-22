using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.World.Terrain;

public sealed class ProvinceTerrainGeneratorTests
{
    private const ulong WorldSeed = 20261122;

    internal static ProvinceTerrainDescriptor Descriptor(
        TerrainType terrain = TerrainType.Plains,
        ulong worldSeed = WorldSeed,
        int provinceId = 10)
    {
        var neighbors = Enumerable.Range(0, 6)
            .Select(i => new ProvinceNeighborBorder(
                provinceId + 1 + i,
                60.0 * i,
                roadLevel: 1 + i % 2));

        return new ProvinceTerrainDescriptor(
            provinceId, worldSeed, terrain, neighbors);
    }

    [Fact]
    public void SameDescriptor_ProducesIdenticalGrids()
    {
        var first = ProvinceTerrainGenerator.Generate(Descriptor(worldSeed: 1));
        var second = ProvinceTerrainGenerator.Generate(Descriptor(worldSeed: 1));

        Assert.True(first.ElevationData.SequenceEqual(second.ElevationData));
        Assert.True(first.TerrainData.SequenceEqual(second.TerrainData));
        Assert.True(first.FeatureData.SequenceEqual(second.FeatureData));
        Assert.True(first.RiverEdgeData.SequenceEqual(second.RiverEdgeData));
        Assert.True(first.BridgeEdgeData.SequenceEqual(second.BridgeEdgeData));

        Assert.Equal(first.BorderCrossings, second.BorderCrossings);

        Assert.Equal(first.Localities.Count, second.Localities.Count);

        for (int i = 0; i < first.Localities.Count; i++)
        {
            Assert.Equal(first.Localities[i].Kind, second.Localities[i].Kind);
            Assert.Equal(first.Localities[i].CenterCell, second.Localities[i].CenterCell);
            Assert.Equal(first.Localities[i].Cells, second.Localities[i].Cells);
        }
    }

    [Fact]
    public void DifferentWorldSeeds_ProduceDifferentGrids()
    {
        var first = ProvinceTerrainGenerator.Generate(Descriptor(worldSeed: 1));
        var second = ProvinceTerrainGenerator.Generate(Descriptor(worldSeed: 2));

        Assert.False(first.ElevationData.SequenceEqual(second.ElevationData));
        Assert.False(first.FeatureData.SequenceEqual(second.FeatureData));
    }

    [Fact]
    public void Grid_HasStandardSizeAndSymmetricNeighbourhood()
    {
        var grid = ProvinceTerrainGenerator.Generate(Descriptor());

        Assert.Equal(125, grid.Columns);
        Assert.Equal(144, grid.Rows);
        Assert.Equal(18000, grid.CellCount);

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            Assert.Equal(cell, grid.TryGetIndex(grid.GetCoord(cell), out int back) ? back : -1);

            int neighbors = 0;

            for (int d = 0; d < HexCoord.DirectionCount; d++)
            {
                int next = grid.GetNeighborIndex(cell, d);

                if (next < 0)
                    continue;

                neighbors++;

                Assert.Equal(cell, grid.GetNeighborIndex(next, HexCoord.Opposite(d)));

                double distance = double.Hypot(
                    grid.GetCenterXMeters(next) - grid.GetCenterXMeters(cell),
                    grid.GetCenterYMeters(next) - grid.GetCenterYMeters(cell));

                Assert.Equal(200, distance, 6);
            }

            Assert.InRange(neighbors, 2, 6);
            Assert.Equal(neighbors < 6, grid.IsFrameBoundary(cell));
        }
    }

    [Fact]
    public void RiverEdgesAndBridges_AreSymmetricAndBridgesCarryRoads()
    {
        var grid = ProvinceTerrainGenerator.Generate(Descriptor(worldSeed: 1));

        int riverEdges = 0;
        int bridges = 0;

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            for (int d = 0; d < HexCoord.DirectionCount; d++)
            {
                int next = grid.GetNeighborIndex(cell, d);

                if (next < 0)
                    continue;

                int opposite = HexCoord.Opposite(d);

                Assert.Equal(grid.IsRiverEdge(cell, d), grid.IsRiverEdge(next, opposite));
                Assert.Equal(grid.HasBridge(cell, d), grid.HasBridge(next, opposite));

                if (grid.IsRiverEdge(cell, d))
                    riverEdges++;

                if (!grid.HasBridge(cell, d))
                    continue;

                bridges++;

                Assert.True(grid.IsRiverEdge(cell, d));
                Assert.True(grid.GetFeatures(cell).HasFlag(TerrainCellFeature.Road));
                Assert.True(grid.GetFeatures(next).HasFlag(TerrainCellFeature.Road));
                Assert.True(grid.CanCross(cell, d));
            }
        }

        // Avec cette graine, une rivière traverse la province : des ponts sont nécessaires.
        Assert.True(riverEdges > 0);
        Assert.True(bridges > 0);
    }

    [Theory]
    [InlineData(TerrainType.Plains)]
    [InlineData(TerrainType.Forest)]
    [InlineData(TerrainType.Hills)]
    [InlineData(TerrainType.Mountain)]
    [InlineData(TerrainType.Marsh)]
    [InlineData(TerrainType.Desert)]
    [InlineData(TerrainType.Urban)]
    public void Proportions_FollowTheProvinceProfile(TerrainType terrain)
    {
        var profile = TerrainGenerationProfile.For(terrain);
        var grid = ProvinceTerrainGenerator.Generate(Descriptor(terrain));

        double Fraction(Func<int, bool> predicate) =>
            (double)Enumerable.Range(0, grid.CellCount).Count(predicate) / grid.CellCount;

        // Les localités recouvrent un peu de bois et de collines : tolérance de 1 %.
        Assert.Equal(profile.WoodsFraction,
            Fraction(c => grid.GetFeatures(c).HasFlag(TerrainCellFeature.Woods)), 0.01);

        Assert.Equal(profile.MarshFraction,
            Fraction(c => grid.GetTerrain(c) == TerrainType.Marsh), 0.01);

        Assert.Equal(profile.HillsFraction,
            Fraction(c => grid.GetTerrain(c) == TerrainType.Hills), 0.01);

        Assert.Equal(profile.MountainFraction,
            Fraction(c => grid.GetTerrain(c) == TerrainType.Mountain), 0.01);

        float min = float.MaxValue;
        float max = float.MinValue;

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            min = Math.Min(min, grid.GetElevation(cell));
            max = Math.Max(max, grid.GetElevation(cell));
        }

        Assert.Equal(profile.BaseElevationMeters, min, 0.01);
        Assert.Equal(profile.ReliefAmplitudeMeters, max - min, 0.01);
    }

    [Theory]
    [InlineData(TerrainType.Plains)]
    [InlineData(TerrainType.Marsh)]
    [InlineData(TerrainType.Forest)]
    public void Marshes_OnlyOccupyTheLowestCells(TerrainType terrain)
    {
        var grid = ProvinceTerrainGenerator.Generate(Descriptor(terrain));

        var marsh = Enumerable.Range(0, grid.CellCount)
            .Where(c => grid.GetTerrain(c) == TerrainType.Marsh)
            .ToArray();

        Assert.NotEmpty(marsh);

        float highestMarsh = marsh.Max(grid.GetElevation);

        float lowestOther = Enumerable.Range(0, grid.CellCount)
            .Where(c => grid.GetTerrain(c) != TerrainType.Marsh)
            .Min(grid.GetElevation);

        Assert.True(highestMarsh <= lowestOther);
    }

    [Theory]
    [InlineData(TerrainType.Hills)]
    [InlineData(TerrainType.Mountain)]
    public void HillsAndMountains_AreThresholdsOnRelief(TerrainType terrain)
    {
        var grid = ProvinceTerrainGenerator.Generate(Descriptor(terrain));
        var cells = Enumerable.Range(0, grid.CellCount).ToArray();

        float lowestHill = cells
            .Where(c => grid.GetTerrain(c) == TerrainType.Hills)
            .Min(grid.GetElevation);

        // Les localités peuvent recouvrir des collines : on les exclut.
        float highestLowland = cells
            .Where(c => grid.GetTerrain(c) is not (TerrainType.Hills
                or TerrainType.Mountain or TerrainType.Urban))
            .Max(grid.GetElevation);

        Assert.True(lowestHill >= highestLowland);

        if (terrain == TerrainType.Mountain)
        {
            float lowestMountain = cells
                .Where(c => grid.GetTerrain(c) == TerrainType.Mountain)
                .Min(grid.GetElevation);

            float highestHill = cells
                .Where(c => grid.GetTerrain(c) == TerrainType.Hills)
                .Max(grid.GetElevation);

            Assert.True(lowestMountain >= highestHill);
        }
    }

    [Theory]
    [InlineData(TerrainType.Plains)]
    [InlineData(TerrainType.Forest)]
    [InlineData(TerrainType.Hills)]
    [InlineData(TerrainType.Mountain)]
    [InlineData(TerrainType.Marsh)]
    [InlineData(TerrainType.Desert)]
    [InlineData(TerrainType.Urban)]
    public void Localities_FollowProfileCountsAndSizes(TerrainType terrain)
    {
        var profile = TerrainGenerationProfile.For(terrain);

        foreach (ulong seed in new ulong[] { 1, 2, 3, WorldSeed })
        {
            var grid = ProvinceTerrainGenerator.Generate(Descriptor(terrain, seed));

            Assert.True(profile.Villages.Contains(
                grid.Localities.Count(l => l.Kind == TerrainLocalityKind.Village)));

            Assert.True(profile.Towns.Contains(
                grid.Localities.Count(l => l.Kind == TerrainLocalityKind.Town)));

            Assert.True(profile.Cities.Contains(
                grid.Localities.Count(l => l.Kind == TerrainLocalityKind.City)));

            var allCells = new HashSet<int>();

            foreach (var locality in grid.Localities)
            {
                var size = locality.Kind switch
                {
                    TerrainLocalityKind.Village => profile.VillageCells,
                    TerrainLocalityKind.Town => profile.TownCells,
                    _ => profile.CityCells
                };

                Assert.True(size.Contains(locality.Cells.Count));
                Assert.Contains(locality.CenterCell, locality.Cells);

                foreach (int cell in locality.Cells)
                {
                    Assert.True(allCells.Add(cell));
                    Assert.Equal(TerrainType.Urban, grid.GetTerrain(cell));
                    Assert.True(grid.GetFeatures(cell).HasFlag(TerrainCellFeature.Buildings));
                    Assert.False(grid.GetFeatures(cell).HasFlag(TerrainCellFeature.Woods));
                }
            }

            int buildingCells = Enumerable.Range(0, grid.CellCount)
                .Count(c => grid.GetFeatures(c).HasFlag(TerrainCellFeature.Buildings));

            Assert.Equal(allCells.Count, buildingCells);
        }
    }

    [Fact]
    public void EveryBoundaryCell_IsAssignedToOneNeighbour()
    {
        var grid = ProvinceTerrainGenerator.Generate(Descriptor());

        int boundary = Enumerable.Range(0, grid.CellCount).Count(grid.IsFrameBoundary);

        int assigned = grid.Descriptor.Neighbors
            .Sum(n => grid.GetBorderCells(n.NeighborProvinceId).Count);

        Assert.Equal(boundary, assigned);

        // La voisine au relèvement 0° (nord) reçoit des cases du bord nord.
        int north = grid.Descriptor.Neighbors.Single(n => n.BearingDegrees == 0).NeighborProvinceId;

        Assert.All(grid.GetBorderCells(north),
            c => Assert.True(grid.GetCenterYMeters(c) > grid.FrameCenterYMeters));
    }

    [Fact]
    public void NeighbourProvinces_BorderCrossingsCoincide()
    {
        bool riverSeen = false;

        for (ulong seed = 1; seed <= 40; seed++)
        {
            // A (10) voit B (20) au relèvement 30° ; B voit A à 210°.
            var a = ProvinceTerrainGenerator.Generate(new ProvinceTerrainDescriptor(
                10, seed, TerrainType.Plains, new[]
                {
                    new ProvinceNeighborBorder(20, 30, roadLevel: 2),
                    new ProvinceNeighborBorder(21, 150, roadLevel: 1),
                    new ProvinceNeighborBorder(22, 270)
                }));

            var b = ProvinceTerrainGenerator.Generate(new ProvinceTerrainDescriptor(
                20, seed, TerrainType.Hills, new[]
                {
                    new ProvinceNeighborBorder(23, 90, roadLevel: 1),
                    new ProvinceNeighborBorder(10, 210, roadLevel: 2),
                    new ProvinceNeighborBorder(24, 330)
                }));

            var fromA = a.BorderCrossings.Where(c => c.NeighborProvinceId == 20).ToArray();
            var fromB = b.BorderCrossings.Where(c => c.NeighborProvinceId == 10).ToArray();

            Assert.Equal(fromA.Length, fromB.Length);
            Assert.Equal(2, fromA.Count(c => c.Kind == BorderCrossingKind.Road));

            for (int i = 0; i < fromA.Length; i++)
            {
                Assert.Equal(fromA[i].Kind, fromB[i].Kind);
                Assert.Equal(fromA[i].CanonicalParameter, fromB[i].CanonicalParameter);

                // A a l'identifiant le plus petit : son sens horaire est le sens canonique.
                Assert.Equal(fromA[i].CanonicalParameter, fromA[i].LocalParameter);
                Assert.Equal(1 - fromA[i].CanonicalParameter, fromB[i].LocalParameter, 12);

                Assert.Contains(fromA[i].CellIndex, a.GetBorderCells(20));
                Assert.Contains(fromB[i].CellIndex, b.GetBorderCells(10));
            }

            riverSeen |= fromA.Any(c => c.Kind == BorderCrossingKind.River);
        }

        Assert.True(riverSeen);
    }

    [Fact]
    public void BorderCrossings_TouchTheirRoadsAndRivers()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, WorldSeed })
        {
            var grid = ProvinceTerrainGenerator.Generate(Descriptor(worldSeed: seed));

            foreach (var crossing in grid.BorderCrossings)
            {
                if (crossing.Kind == BorderCrossingKind.Road)
                {
                    Assert.True(grid.GetFeatures(crossing.CellIndex)
                        .HasFlag(TerrainCellFeature.Road));

                    continue;
                }

                // La rivière part d'un coin de la case : un bord de rivière
                // touche la case ou l'une de ses voisines.
                var near = Enumerable.Range(0, HexCoord.DirectionCount)
                    .Select(d => grid.GetNeighborIndex(crossing.CellIndex, d))
                    .Where(n => n >= 0)
                    .Append(crossing.CellIndex);

                Assert.Contains(near, grid.HasAnyRiverEdge);
            }
        }
    }

    [Fact]
    public void DefaultProfiles_AreValid()
    {
        foreach (var terrain in Enum.GetValues<TerrainType>())
            TerrainGenerationProfile.For(terrain).Validate();
    }

    [Theory]
    [InlineData("SommeDesSeuilsDeReliefSuperieureA1")]
    [InlineData("BoisEtMaraisSuperieursA1")]
    [InlineData("FractionNegative")]
    [InlineData("FractionNaN")]
    [InlineData("TerrainDeBaseProduitParLeGenerateur")]
    [InlineData("TerrainDeBaseInconnu")]
    [InlineData("AltitudeDeBaseInfinie")]
    [InlineData("AmplitudeNaN")]
    [InlineData("AmplitudeNulle")]
    [InlineData("LocaliteSansCase")]
    public void Generate_RejectsInvalidProfiles(string defect)
    {
        var plains = TerrainGenerationProfile.For(TerrainType.Plains);

        var profile = defect switch
        {
            // Avant le correctif : 40 % de marais au lieu de 60 %, sans erreur.
            "SommeDesSeuilsDeReliefSuperieureA1" => plains with { MarshFraction = 0.6, HillsFraction = 0.6 },
            // Avant le correctif : 65 % de bois au lieu de 80 %, les marais étant exclus.
            "BoisEtMaraisSuperieursA1" => plains with { WoodsFraction = 0.8, MarshFraction = 0.35 },
            "FractionNegative" => plains with { HillsFraction = -0.1 },
            "FractionNaN" => plains with { WoodsFraction = double.NaN },
            // Avant le correctif : 90 % de marais, à toutes les altitudes.
            "TerrainDeBaseProduitParLeGenerateur" => plains with { BaseTerrain = TerrainType.Marsh },
            "TerrainDeBaseInconnu" => plains with { BaseTerrain = (TerrainType)99 },
            "AltitudeDeBaseInfinie" => plains with { BaseElevationMeters = double.PositiveInfinity },
            // Avant le correctif : altitudes NaN, puis échec bien plus tard dans TacticalPoint.
            "AmplitudeNaN" => plains with { ReliefAmplitudeMeters = double.NaN },
            "AmplitudeNulle" => plains with { ReliefAmplitudeMeters = 0 },
            // Avant le correctif : villages d'une case malgré l'intervalle [0, 0].
            "LocaliteSansCase" => plains with { VillageCells = new IntRange(0, 0) },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };

        Assert.Throws<ArgumentException>(() =>
            ProvinceTerrainGenerator.Generate(Descriptor(), profile));
    }

    [Fact]
    public void Descriptor_RejectsInvalidNeighbours()
    {
        Assert.Throws<ArgumentException>(() => new ProvinceTerrainDescriptor(
            1, 0, TerrainType.Plains, new[] { new ProvinceNeighborBorder(1, 0) }));

        Assert.Throws<ArgumentException>(() => new ProvinceTerrainDescriptor(
            1, 0, TerrainType.Plains, new[]
            {
                new ProvinceNeighborBorder(2, 0),
                new ProvinceNeighborBorder(2, 90)
            }));

        Assert.Throws<ArgumentException>(() => new ProvinceTerrainDescriptor(
            1, 0, TerrainType.Plains, new[]
            {
                new ProvinceNeighborBorder(2, 45),
                new ProvinceNeighborBorder(3, 45)
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ProvinceNeighborBorder(2, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProvinceNeighborBorder(2, 0, roadLevel: 4));

        Assert.Equal(
            ProvinceTerrainDescriptor.ComputeBorderSeed(7, 3, 9),
            ProvinceTerrainDescriptor.ComputeBorderSeed(7, 9, 3));
    }
}
