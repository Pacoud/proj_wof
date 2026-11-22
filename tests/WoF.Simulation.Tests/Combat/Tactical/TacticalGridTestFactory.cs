using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.Combat.Tactical;

/// <summary>
/// Petites grilles au relief choisi, pour tester le moteur sur un terrain
/// connu, sans passer par le générateur. Chaque case est une plaine dont
/// l'altitude du centre est donnée par une fonction de (x, y).
/// </summary>
internal static class TacticalGridTestFactory
{
    internal sealed record GridBattle(
        ProvinceHexGrid Grid,
        ProvinceBattlefieldLayout Layout,
        TacticalBattle Battle,
        TacticalGeometryMap Map,
        DiplomacySystem Diplomacy,
        Division French,
        Division German);

    internal static ProvinceHexGrid CreateGrid(
        int columns,
        int rows,
        Func<double, double, double> elevation,
        Func<double, double, TerrainCellFeature>? features = null)
    {
        var descriptor = new ProvinceTerrainDescriptor(
            10, 1, TerrainType.Plains, Array.Empty<ProvinceNeighborBorder>());

        var grid = new ProvinceHexGrid(descriptor, columns, rows);

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            double x = grid.GetCenterXMeters(cell);
            double y = grid.GetCenterYMeters(cell);

            grid.SetTerrain(cell, TerrainType.Plains);
            grid.SetElevation(cell, (float)elevation(x, y));

            var feature = features?.Invoke(x, y) ?? TerrainCellFeature.None;

            if (feature != TerrainCellFeature.None)
                grid.AddFeature(cell, feature);
        }

        return grid;
    }

    /// <summary>
    /// Champ de bataille de toute la grille, bataille franco-allemande
    /// et carte géométrique reliée au terrain.
    /// </summary>
    internal static GridBattle CreateBattle(ProvinceHexGrid grid)
    {
        var partition = GrandSectorPartition.Build(grid);

        var layout = TacticalBattlefieldBuilder.Build(
            grid, partition, partition.Sectors.Select(s => s.Id));

        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(10, "Grille de test", TerrainType.Plains);

        var french = MakeDivision("Division française", france, province);
        var german = MakeDivision("Division allemande", germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);
        engagements.DetectProvinceEngagements(new[] { french, german }, currentHour: 1);

        var battle = new TacticalBattle(
            Assert.Single(engagements.ActiveEngagements),
            layout.Battlefield);

        var map = new TacticalGeometryMap(battle);
        layout.DefineGeometry(map);

        return new GridBattle(grid, layout, battle, map, diplomacy, french, german);
    }

    internal static int Cell(ProvinceHexGrid grid, int column, int row)
    {
        return row * grid.Columns + column;
    }

    /// <summary>
    /// Déploie une compagnie en première ligne dans une case et la place
    /// au sol, au centre décalé de (dx, dy).
    /// </summary>
    internal static CompanyDeployment Place(
        GridBattle b,
        Division division,
        int companyIndex,
        int column,
        int row,
        double dx = 0,
        double dy = 0,
        double facingDegrees = 90)
    {
        var sector = b.Layout.GetSector(Cell(b.Grid, column, row));

        var deployment = b.Battle.DeployCompany(
            division.Battalions[0].Companies[companyIndex],
            sector,
            CompanyDeploymentRole.FrontLine);

        var center = b.Layout.GetCellCenter(sector);

        b.Map.PlaceCompany(
            deployment,
            b.Map.GetGroundPoint(sector, center.XMeters + dx, center.YMeters + dy),
            facingDegrees,
            frontageMeters: 20);

        return deployment;
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
                manpower: 400,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(1, "Bataillon 1", BattalionType.Infantry);

        for (int i = 1; i <= 4; i++)
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
