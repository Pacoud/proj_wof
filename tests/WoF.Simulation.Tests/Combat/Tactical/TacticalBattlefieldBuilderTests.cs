using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.Tests.World.Terrain;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalBattlefieldBuilderTests
{
    /// <summary>
    /// Province dont une rivière traverse le cadre (graine 1) ;
    /// grands secteurs demandés : celui d'un pont et celui d'un bord de rivière sans pont.
    /// </summary>
    private static (ProvinceHexGrid Grid, GrandSectorPartition Partition, int[] Requested)
        CreateRiverSelection()
    {
        var grid = ProvinceTerrainGenerator.Generate(
            ProvinceTerrainGeneratorTests.Descriptor(worldSeed: 1));

        var partition = GrandSectorPartition.Build(grid);

        int bridgeCell = Enumerable.Range(0, grid.CellCount)
            .First(c => Enumerable.Range(0, 6).Any(d => grid.HasBridge(c, d)));

        int blockedCell = Enumerable.Range(0, grid.CellCount)
            .First(c => !grid.IsFrameBoundary(c)
                && Enumerable.Range(0, 6).Any(d => grid.IsRiverEdge(c, d) && !grid.HasBridge(c, d)));

        var requested = new[]
        {
            partition.GetSectorOfCell(bridgeCell).Id,
            partition.GetSectorOfCell(blockedCell).Id
        };

        return (grid, partition, requested);
    }

    [Fact]
    public void Build_IncludesOnlyRequestedSectorsAndTheirNeighbours()
    {
        var (grid, partition, requested) = CreateRiverSelection();

        var layout = TacticalBattlefieldBuilder.Build(grid, partition, requested);

        var expectedSectors = requested
            .Concat(requested.SelectMany(id => partition.GetSector(id).NeighborIds))
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        var expectedCells = expectedSectors
            .SelectMany(id => partition.GetSector(id).Cells)
            .OrderBy(c => c)
            .ToArray();

        Assert.Equal(requested.Distinct().OrderBy(id => id), layout.RequestedGrandSectorIds);
        Assert.Equal(expectedSectors, layout.IncludedGrandSectorIds);
        Assert.Equal(expectedCells, layout.CellIndices);

        Assert.Equal(expectedCells.Length, layout.Battlefield.Sectors.Count);

        Assert.Equal(
            expectedCells.Select(c => c + 1),
            layout.Battlefield.Sectors.Select(s => s.Id));

        foreach (int cell in expectedCells)
        {
            var sector = layout.GetSector(cell);

            Assert.True(layout.Battlefield.ContainsSector(sector));
            Assert.Equal(cell, layout.GetCellIndex(sector));
        }
    }

    [Fact]
    public void Build_TranslatesTerrainElevationAndFeatures()
    {
        var (grid, partition, requested) = CreateRiverSelection();

        var layout = TacticalBattlefieldBuilder.Build(grid, partition, requested);

        foreach (int cell in layout.CellIndices)
        {
            var sector = layout.GetSector(cell);
            var features = grid.GetFeatures(cell);

            Assert.Equal(grid.GetTerrain(cell), sector.DominantTerrain);
            Assert.Equal((int)Math.Round(grid.GetElevation(cell), MidpointRounding.AwayFromZero),
                sector.ElevationMeters);

            Assert.Equal(features.HasFlag(TerrainCellFeature.Woods), sector.Features.HasFlag(SectorFeature.Woods));
            Assert.Equal(features.HasFlag(TerrainCellFeature.Buildings), sector.Features.HasFlag(SectorFeature.Buildings));
            Assert.Equal(features.HasFlag(TerrainCellFeature.Road), sector.Features.HasFlag(SectorFeature.Road));
            Assert.False(sector.Features.HasFlag(SectorFeature.Entrenchments));
        }
    }

    [Fact]
    public void Build_ConnectsNeighboursExceptAcrossRiversWithoutBridge()
    {
        var (grid, partition, requested) = CreateRiverSelection();

        var layout = TacticalBattlefieldBuilder.Build(grid, partition, requested);
        var included = layout.CellIndices.ToHashSet();

        int expectedConnections = 0;
        int blockedPairs = 0;
        int bridgedPairs = 0;

        foreach (int cell in layout.CellIndices)
        {
            for (int d = 0; d < 3; d++)
            {
                int next = grid.GetNeighborIndex(cell, d);

                if (next < 0 || !included.Contains(next))
                    continue;

                bool connected = layout.Battlefield.AreConnected(
                    layout.GetSector(cell), layout.GetSector(next));

                bool river = grid.IsRiverEdge(cell, d);
                bool bridge = grid.HasBridge(cell, d);

                Assert.Equal(!river || bridge, connected);

                if (connected)
                    expectedConnections++;

                if (river && !bridge)
                    blockedPairs++;

                if (bridge)
                    bridgedPairs++;
            }
        }

        Assert.True(blockedPairs > 0);
        Assert.True(bridgedPairs > 0);
        Assert.Equal(expectedConnections, layout.Battlefield.Connections.Count);

        Assert.All(layout.Battlefield.Connections, c =>
            Assert.Equal(ProvinceHexGrid.SpacingMeters, c.DistanceMeters));
    }

    [Fact]
    public void Build_RejectsInvalidSelections()
    {
        var (grid, partition, _) = CreateRiverSelection();

        Assert.Throws<ArgumentException>(() =>
            TacticalBattlefieldBuilder.Build(grid, partition, Array.Empty<int>()));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TacticalBattlefieldBuilder.Build(grid, partition, new[] { 0 }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TacticalBattlefieldBuilder.Build(grid, partition, new[] { partition.Sectors.Count + 1 }));

        var otherGrid = ProvinceTerrainGenerator.Generate(
            ProvinceTerrainGeneratorTests.Descriptor(worldSeed: 2));

        Assert.Throws<InvalidOperationException>(() =>
            TacticalBattlefieldBuilder.Build(otherGrid, partition, new[] { 1 }));
    }

    [Fact]
    public void Geometry_AllowsPlacingAndMeasuringCompaniesInNeighbouringCells()
    {
        var (grid, partition, requested) = CreateRiverSelection();
        var layout = TacticalBattlefieldBuilder.Build(grid, partition, requested);

        var (battle, french, german) = CreateBattle(layout.Battlefield);
        var map = new TacticalGeometryMap(battle);

        layout.DefineGeometry(map);

        Assert.Equal(layout.CellIndices.Count, map.SectorGeometries.Count);

        Assert.All(map.SectorGeometries, g =>
            Assert.Equal(ProvinceHexGrid.CircumradiusMeters, g.RadiusMeters));

        // Une case intérieure du premier grand secteur demandé et une voisine reliée.
        int cell = partition.GetSector(requested[0]).Cells
            .First(c => Enumerable.Range(0, 6).Any(d =>
                grid.CanCross(c, d) && layout.TryGetSector(grid.GetNeighborIndex(c, d), out _)));

        int direction = Enumerable.Range(0, 6).First(d =>
            grid.CanCross(cell, d) && layout.TryGetSector(grid.GetNeighborIndex(cell, d), out _));

        var first = layout.GetSector(cell);
        var second = layout.GetSector(grid.GetNeighborIndex(cell, direction));

        Assert.True(layout.Battlefield.AreConnected(first, second));

        var frenchDeployment = battle.DeployCompany(
            french.Battalions[0].Companies[0], first, CompanyDeploymentRole.FrontLine);

        var germanDeployment = battle.DeployCompany(
            german.Battalions[0].Companies[0], second, CompanyDeploymentRole.FrontLine);

        // Au centre de la première case ; à 100 m du centre de la seconde, vers l'est.
        var center = layout.GetCellCenter(first);
        var secondCenter = layout.GetCellCenter(second);

        // Le point décalé est pris au sol : la carte refuse une altitude
        // différente de celle de la grille.
        var offset = map.GetGroundPoint(
            second,
            secondCenter.XMeters + 100,
            secondCenter.YMeters);

        map.PlaceCompany(frenchDeployment, center, 0, 300);
        map.PlaceCompany(germanDeployment, offset, 180, 300);

        var relation = map.Measure(frenchDeployment, germanDeployment);

        Assert.Equal(center.HorizontalDistanceTo(offset), relation.HorizontalDistanceMeters, 9);
        Assert.Equal(offset.GroundElevationMeters - center.GroundElevationMeters,
            relation.ElevationDifferenceMeters, 9);

        // Une carte d'une autre bataille est refusée.
        var otherLayout = TacticalBattlefieldBuilder.Build(grid, partition, new[] { requested[0] });
        var (otherBattle, _, _) = CreateBattle(otherLayout.Battlefield);

        Assert.Throws<InvalidOperationException>(() =>
            layout.DefineGeometry(new TacticalGeometryMap(otherBattle)));
    }

    [Fact]
    public void CellCenter_UsesTheGroundElevationLikeTheEngine()
    {
        var (grid, partition, requested) = CreateRiverSelection();
        var layout = TacticalBattlefieldBuilder.Build(grid, partition, requested);

        var (battle, french, _) = CreateBattle(layout.Battlefield);
        var map = new TacticalGeometryMap(battle);
        layout.DefineGeometry(map);

        // Le centre d'une case est à l'altitude de la grille, la même
        // pour la disposition et pour la carte du moteur.
        foreach (int c in layout.CellIndices)
        {
            var s = layout.GetSector(c);
            var center = layout.GetCellCenter(s);

            Assert.Equal((double)grid.GetElevation(c), center.GroundElevationMeters);
            Assert.Equal(center, map.GetSectorCenterPoint(s));
        }

        // Une case d'altitude non entière, reliée à une voisine incluse :
        // l'altitude entière du secteur n'est plus lue pour les positions.
        int cell = layout.CellIndices.First(c =>
            grid.GetElevation(c) != MathF.Round(grid.GetElevation(c))
            && Enumerable.Range(0, 6).Any(d =>
                grid.CanCross(c, d) && layout.TryGetSector(grid.GetNeighborIndex(c, d), out _)));

        int direction = Enumerable.Range(0, 6).First(d =>
            grid.CanCross(cell, d) && layout.TryGetSector(grid.GetNeighborIndex(cell, d), out _));

        var sector = layout.GetSector(cell);
        var neighbor = layout.GetSector(grid.GetNeighborIndex(cell, direction));

        Assert.NotEqual(sector.ElevationMeters, layout.GetCellCenter(sector).GroundElevationMeters);

        var deployment = battle.DeployCompany(
            french.Battalions[0].Companies[0], sector, CompanyDeploymentRole.FrontLine);

        var placed = map.PlaceCompany(deployment, layout.GetCellCenter(sector), 0, 300);

        // Une annulation sans avoir quitté la case ne déplace pas la compagnie.
        Assert.Equal(placed.Point, map.GetRetreatPoint(sector, placed.Point));

        // Déplacée par le moteur au centre de la case voisine : même point que GetCellCenter.
        map.RelocateCompany(deployment, neighbor, map.GetSectorCenterPoint(neighbor));
        Assert.Equal(layout.GetCellCenter(neighbor), map.GetPosition(deployment)!.Point);
    }

    [Fact]
    public void DefineGeometry_ChecksEveryCellBeforeWriting()
    {
        var (grid, partition, requested) = CreateRiverSelection();
        var layout = TacticalBattlefieldBuilder.Build(grid, partition, requested);

        // Une case du milieu a déjà une géométrie, posée à la main.
        var (battle, _, _) = CreateBattle(layout.Battlefield);
        var map = new TacticalGeometryMap(battle);

        int middle = layout.CellIndices[layout.CellIndices.Count / 2];

        map.DefineSector(
            layout.GetSector(middle),
            grid.GetCenterXMeters(middle),
            grid.GetCenterYMeters(middle),
            50);

        long revision = map.Revision;

        Assert.Throws<InvalidOperationException>(() => layout.DefineGeometry(map));

        // Refus sans aucune écriture : seule la géométrie posée à la main existe.
        Assert.Single(map.SectorGeometries);
        Assert.Equal(revision, map.Revision);
        Assert.Null(map.GetSectorGeometry(layout.GetSector(layout.CellIndices[0])));

        // Un second appel sur une carte déjà remplie est refusé et ne change rien.
        var (otherBattle, _, _) = CreateBattle(layout.Battlefield);
        var filled = new TacticalGeometryMap(otherBattle);

        layout.DefineGeometry(filled);
        long filledRevision = filled.Revision;

        Assert.Throws<InvalidOperationException>(() => layout.DefineGeometry(filled));
        Assert.Equal(layout.CellIndices.Count, filled.SectorGeometries.Count);
        Assert.Equal(filledRevision, filled.Revision);
    }

    private static (TacticalBattle Battle, Division French, Division German)
        CreateBattle(TacticalBattlefield battlefield)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var province = new Province(10, "Province générée", TerrainType.Plains);

        var french = MakeDivision("Division française", france, province);
        var german = MakeDivision("Division allemande", germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement = Assert.Single(engagements.ActiveEngagements);

        return (new TacticalBattle(engagement, battlefield), french, german);
    }

    private static Division MakeDivision(string name, Country country, Province province)
    {
        var division = new Division(
            name,
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: 200,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(1, "Bataillon 1", BattalionType.Infantry);

        for (int i = 1; i <= 2; i++)
        {
            battalion.AddCompany(new Company(
                i,
                $"Compagnie {i}",
                CompanyType.Infantry,
                new CompanyComposition(manpower: 100)));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }
}
