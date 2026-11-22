using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World;
using static WoF.Simulation.Tests.Combat.Tactical.TacticalGridTestFactory;

namespace WoF.Simulation.Tests.Combat.Tactical;

/// <summary>
/// Altitude des positions : sur une grille, toute position est au sol
/// interpolé ; un champ de bataille construit à la main garde l'altitude
/// déclarée de ses secteurs.
/// </summary>
public sealed class TacticalTerrainGeometryTests
{
    private static double Plane(double x, double y) => 80 + 0.04 * x - 0.06 * y;

    [Fact]
    public void GridPositions_ReadTheInterpolatedGround()
    {
        var b = CreateBattle(CreateGrid(12, 8, Plane));

        Assert.NotNull(b.Map.Terrain);

        var sector = b.Layout.GetSector(Cell(b.Grid, 5, 3));
        var center = b.Map.GetSectorCenterPoint(sector);

        // Centre : altitude exacte de la case.
        Assert.Equal((double)b.Grid.GetElevation(Cell(b.Grid, 5, 3)), center.GroundElevationMeters);
        Assert.Equal(b.Layout.GetCellCenter(sector), center);

        // Ailleurs dans la case : le plan, et non l'altitude du centre.
        var offset = b.Map.GetGroundPoint(sector, center.XMeters + 70, center.YMeters - 40);

        Assert.Equal(Plane(offset.XMeters, offset.YMeters), offset.GroundElevationMeters, 3);
        Assert.NotEqual(center.GroundElevationMeters, offset.GroundElevationMeters, 1);
    }

    [Fact]
    public void PlaceCompany_OffTheGround_IsRejectedWithoutChange()
    {
        var b = CreateBattle(CreateGrid(12, 8, Plane));

        var sector = b.Layout.GetSector(Cell(b.Grid, 5, 3));
        var deployment = b.Battle.DeployCompany(
            b.French.Battalions[0].Companies[0], sector, CompanyDeploymentRole.FrontLine);

        var ground = b.Map.GetGroundPoint(sector, 1050, 500);
        long revision = b.Map.Revision;

        Assert.Throws<InvalidOperationException>(() => b.Map.PlaceCompany(
            deployment,
            new TacticalPoint(ground.XMeters, ground.YMeters, ground.GroundElevationMeters + 0.5),
            90, 20));

        Assert.Null(b.Map.GetPosition(deployment));
        Assert.Equal(revision, b.Map.Revision);

        b.Map.PlaceCompany(deployment, ground, 90, 20);
        Assert.Equal(ground, b.Map.GetPosition(deployment)!.Point);
    }

    [Fact]
    public void RelocateCompany_ChecksTheDestinationAndTheGround()
    {
        var b = CreateBattle(CreateGrid(12, 8, Plane));

        var company = Place(b, b.French, 0, column: 5, row: 3);
        var start = b.Map.GetPosition(company)!.Point;

        var neighbor = b.Layout.GetSector(Cell(b.Grid, 6, 3));
        var center = b.Map.GetSectorCenterPoint(neighbor);

        // Hors de la géométrie de la destination.
        Assert.Throws<InvalidOperationException>(() => b.Map.RelocateCompany(
            company, neighbor, b.Map.GetGroundPoint(neighbor, center.XMeters + 150, center.YMeters)));

        // Dans la destination, mais pas au sol.
        Assert.Throws<InvalidOperationException>(() => b.Map.RelocateCompany(
            company, neighbor,
            new TacticalPoint(center.XMeters, center.YMeters, center.GroundElevationMeters - 2)));

        Assert.Equal(start, b.Map.GetPosition(company)!.Point);

        // Un point quelconque de la destination, au sol : il est conservé tel quel.
        var point = b.Map.GetGroundPoint(neighbor, center.XMeters - 30, center.YMeters + 55);
        b.Map.RelocateCompany(company, neighbor, point);

        Assert.Equal(point, b.Map.GetPosition(company)!.Point);
    }

    [Fact]
    public void RetreatPoint_KeepsAPointInsideTheSector_AndStopsAtItsEdgeOtherwise()
    {
        var b = CreateBattle(CreateGrid(12, 8, Plane));

        var sector = b.Layout.GetSector(Cell(b.Grid, 5, 3));
        var center = b.Map.GetSectorCenterPoint(sector);
        var geometry = b.Map.GetSectorGeometry(sector)!;

        var inside = b.Map.GetGroundPoint(sector, center.XMeters + 30, center.YMeters - 40);
        Assert.Equal(inside, b.Map.GetRetreatPoint(sector, inside));

        // 300 m à l'est et 400 m au nord : recul sur le rayon, dans la même direction.
        var outside = new TacticalPoint(center.XMeters + 300, center.YMeters + 400, 0);
        var retreat = b.Map.GetRetreatPoint(sector, outside);

        Assert.True(geometry.Contains(retreat));
        Assert.Equal(center.XMeters + 0.6 * geometry.RadiusMeters, retreat.XMeters, 6);
        Assert.Equal(center.YMeters + 0.8 * geometry.RadiusMeters, retreat.YMeters, 6);
        Assert.Equal(Plane(retreat.XMeters, retreat.YMeters), retreat.GroundElevationMeters, 3);
    }

    [Fact]
    public void HandBuiltBattlefield_KeepsTheDeclaredSectorElevation()
    {
        var b = CreateBattle(CreateGrid(6, 4, (_, _) => 0));

        // Bataille sur un champ de bataille construit à la main.
        var field = new TacticalBattlefield();
        var hill = new BattleSector(1, "Colline", TerrainType.Hills, elevationMeters: 150);
        field.AddSector(hill);

        var battle = new TacticalBattle(b.Battle.Engagement, field);
        var map = new TacticalGeometryMap(battle);
        map.DefineSector(hill, 0, 0, 500);

        Assert.Null(map.Terrain);
        Assert.Equal(new TacticalPoint(0, 0, 150), map.GetSectorCenterPoint(hill));
        Assert.Equal(new TacticalPoint(120, -80, 150), map.GetGroundPoint(hill, 120, -80));
        var retreat = map.GetRetreatPoint(hill, new TacticalPoint(900, 0, 40));
        Assert.Equal(500, retreat.XMeters, 9);
        Assert.Equal(0, retreat.YMeters);
        Assert.Equal(150, retreat.GroundElevationMeters);

        // Sans grille, l'altitude d'un placement reste libre, comme avant.
        var deployment = battle.DeployCompany(
            b.French.Battalions[0].Companies[0], hill, CompanyDeploymentRole.FrontLine);

        map.PlaceCompany(deployment, new TacticalPoint(10, 10, 37), 0, 20);
        Assert.Equal(37, map.GetPosition(deployment)!.Point.GroundElevationMeters);
    }

    [Fact]
    public void Terrain_IsAttachedOnce_BeforeAnyPlacement()
    {
        var b = CreateBattle(CreateGrid(6, 4, (_, _) => 0));

        // DefineGeometry a déjà relié la carte au terrain.
        Assert.Throws<InvalidOperationException>(() =>
            b.Map.AttachTerrain(new TacticalTerrainSurface(b.Grid)));

        // Une carte qui a déjà des compagnies placées ne peut plus être reliée.
        var field = new TacticalBattlefield();
        var sector = new BattleSector(1, "Secteur", TerrainType.Plains);
        field.AddSector(sector);

        var battle = new TacticalBattle(b.Battle.Engagement, field);
        var map = new TacticalGeometryMap(battle);
        map.DefineSector(sector, 0, 0, 100);

        var deployment = battle.DeployCompany(
            b.French.Battalions[0].Companies[0], sector, CompanyDeploymentRole.FrontLine);
        map.PlaceCompany(deployment, new TacticalPoint(0, 0, 0), 0, 20);

        long revision = map.Revision;

        Assert.Throws<InvalidOperationException>(() =>
            map.AttachTerrain(new TacticalTerrainSurface(b.Grid)));

        Assert.Null(map.Terrain);
        Assert.Equal(revision, map.Revision);
    }
}
