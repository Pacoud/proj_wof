
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.Combat;

public sealed class EngagementCompanyPlacementTests
{
    private const ulong Seed = 20261125;
    private static readonly Country Red = new(1, "Rouge");
    private static readonly Country Blue = new(2, "Bleu");

    // ---- Mise en place ----

    /// <summary>Province P (Bleu) entourée des voisines demandées, frontières réciproques.</summary>
    private static (Province P, Dictionary<string, Province> Around, ProvinceBattlefieldGridCache Cache) World(
        params (string Name, double Bearing)[] neighbours)
    {
        var p = new Province(1, "P", TerrainType.Plains, Blue);
        var catalog = new ProvinceTerrainCatalog(Seed);
        var around = new Dictionary<string, Province>();
        var borders = new List<ProvinceNeighborBorder>();
        int id = 2;

        foreach (var (name, bearing) in neighbours)
        {
            var neighbour = new Province(id++, name);
            p.ConnectTo(neighbour);
            around[name] = neighbour;
            borders.Add(new ProvinceNeighborBorder(neighbour.Id, bearing, 1));
            catalog.SetBorders(neighbour, new[] { new ProvinceNeighborBorder(p.Id, (bearing + 180) % 360, 1) });
        }

        catalog.SetBorders(p, borders);
        return (p, around, new ProvinceBattlefieldGridCache(catalog));
    }

    /// <summary>Division d'infanterie : un bataillon par nombre donné, autant de compagnies d'infanterie.</summary>
    private static Division Build(string name, Province at, Country country, params int[] companiesPerBattalion)
    {
        var division = new Division(name, at, 100, 100, country: country);

        for (int b = 0; b < companiesPerBattalion.Length; b++)
        {
            var battalion = new Battalion(b + 1, $"{name}-B{b + 1}", BattalionType.Infantry);

            for (int c = 0; c < companiesPerBattalion[b]; c++)
                battalion.AddCompany(new Company(c + 1, $"{name}-B{b + 1}-C{c + 1}", CompanyType.Infantry));

            division.AddBattalion(battalion);
        }

        return division;
    }

    /// <summary>Division rouge construite en from, qui fait réellement le trajet vers to.</summary>
    private static Division Arrive(Province from, Province to, string name, params int[] companiesPerBattalion)
    {
        var division = Build(name, from, Red, companiesPerBattalion);
        Assert.True(division.TryStartMovement(to, new MovementPlan(DurationHours: 1, FuelPerHour: 0)));
        division.AdvanceOneHour();
        Assert.True(division.FinalizeArrival());
        return division;
    }

    /// <summary>Engagement de province détecté par le système : les arrivants attaquent le défenseur.</summary>
    private static Engagement Provincial(Division defender, params Division[] attackers)
    {
        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(Red, Blue);
        var system = new EngagementSystem(diplomacy);
        system.DetectProvinceEngagements(attackers.Append(defender).ToArray(), currentHour: 1, arrivals: attackers);
        return Assert.Single(system.ActiveEngagements);
    }

    private static EngagementCompanyPlacement Place(
        ProvinceBattlefieldGridCache cache, Engagement engagement, CompanyPlacementOptions? options = null)
    {
        Assert.True(EngagementDeploymentZones.TryCompute(
            cache.GetOrGenerate(engagement), engagement, out var zones, out _));
        var placement = EngagementCompanyPlacement.Compute(
            EngagementDeploymentPlan.Compute(zones!, engagement), options);
        AssertPlacementIsConsistent(placement);
        return placement;
    }

    /// <summary>Nombre de grands secteurs du front entrant pour une arrivée par l'est.</summary>
    private static int EastFrontLength()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var battle = Provincial(Build("D", p, Blue, 4), Arrive(around["E"], p, "R", 4));
        Assert.True(EngagementDeploymentZones.TryCompute(cache.GetOrGenerate(battle), battle, out var zones, out _));
        return EngagementDeploymentPlan.Compute(zones!, battle).EnteringFrontLines[0].Count;
    }

    private static IEnumerable<CompanyPlacement> Of(EngagementCompanyPlacement placement, Battalion battalion) =>
        placement.Companies.Where(c => ReferenceEquals(c.Battalion, battalion));

    /// <summary>Position d'une compagnie par rapport au centre du cadre (x vers l'est, y vers le nord).</summary>
    private static (double X, double Y) Offset(EngagementCompanyPlacement placement, CompanyPlacement company)
    {
        var grid = placement.Plan.Zones.Field.Grid;
        return (company.Point.XMeters - grid.FrameCenterXMeters, company.Point.YMeters - grid.FrameCenterYMeters);
    }

    private static double BearingTo(double fromX, double fromY, double toX, double toY) =>
        (Math.Atan2(toX - fromX, toY - fromY) * 180 / Math.PI + 360) % 360;

    private static double AngularGap(double first, double second)
    {
        double gap = Math.Abs(first - second) % 360;
        return gap > 180 ? 360 - gap : gap;
    }

    /// <summary>
    /// Chaque compagnie du plan est placée une seule fois, dans l'ordre du plan, sur une case d'un
    /// grand secteur qui correspond à son rôle. Le point est le centre de la case, à l'altitude du sol.
    /// La compagnie regarde le centre de la case la plus proche du front adverse. Le front reste
    /// entre le plancher et la largeur par défaut.
    /// </summary>
    private static void AssertPlacementIsConsistent(EngagementCompanyPlacement placement)
    {
        var zones = placement.Plan.Zones;
        var grid = zones.Field.Grid;
        var partition = zones.Field.Partition;

        Assert.Equal(
            placement.Plan.Battalions.SelectMany(a => a.Companies).Select(c => c.Company),
            placement.Companies.Select(c => c.Company));

        foreach (var company in placement.Companies)
        {
            var assignment = Assert.Single(placement.Plan.Battalions, a => ReferenceEquals(a.Battalion, company.Battalion));
            Assert.Equal(assignment.Companies.Single(c => ReferenceEquals(c.Company, company.Company)).Role, company.Role);
            Assert.Equal(partition.GetSectorOfCell(company.Cell).Id, company.GrandSectorId);

            switch (company.Role)
            {
                case CompanyDeploymentRole.FrontLine:
                    Assert.Contains(company.GrandSectorId, assignment.FrontSectorIds);
                    break;
                case CompanyDeploymentRole.Support:
                    Assert.Equal(assignment.SupportSectorId, company.GrandSectorId);
                    break;
                default:
                    Assert.Equal(assignment.ReserveSectorId, company.GrandSectorId);
                    break;
            }

            double x = grid.GetCenterXMeters(company.Cell);
            double y = grid.GetCenterYMeters(company.Cell);
            Assert.True(grid.TryGetGroundElevation(x, y, out double elevation));
            Assert.Equal(new TacticalPoint(x, y, elevation), company.Point);

            // Le plus proche des centres de cases du front adverse, dans l'ordre de la zone.
            var opposing = assignment.Side == DeploymentSide.Entering
                ? zones.FacingFrontSectorIds
                : zones.EnteringFrontSectorIds;
            var nearest = opposing
                .SelectMany(id => partition.GetSector(id).Cells)
                .Select(c => (X: grid.GetCenterXMeters(c), Y: grid.GetCenterYMeters(c)))
                .MinBy(c => (c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y));
            Assert.Equal(BearingTo(x, y, nearest.X, nearest.Y), company.FacingDegrees, 9);

            Assert.InRange(company.FrontageMeters,
                placement.Options.MinCompanyFrontageMeters, placement.Options.CompanyFrontageMeters);
        }
    }

    // ---- Largeur et profondeur ----

    [Fact]
    public void FrontLineCompaniesSpreadAcrossTheWidthOfTheirGrandSector()
    {
        // Entrée à l'est : l'axe latéral est nord-sud.
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var attacker = Arrive(around["E"], p, "R", 4);
        var placement = Place(cache, Provincial(Build("D", p, Blue, 4), attacker));

        var companies = Of(placement, attacker.Battalions[0]).ToArray();
        var front = companies.Where(c => c.Role == CompanyDeploymentRole.FrontLine).ToArray();
        var support = Assert.Single(companies, c => c.Role == CompanyDeploymentRole.Support);

        Assert.Equal(2, front.Length);
        Assert.Equal(front[0].GrandSectorId, front[1].GrandSectorId);
        Assert.True(Math.Abs(front[0].Point.YMeters - front[1].Point.YMeters) >= 400);

        // Seule sur sa rangée, la compagnie de soutien est au milieu de la largeur du secteur.
        var cells = placement.Plan.Zones.Field.Partition.GetSector(support.GrandSectorId).Cells;
        var grid = placement.Plan.Zones.Field.Grid;
        double middle = (cells.Min(grid.GetCenterYMeters) + cells.Max(grid.GetCenterYMeters)) / 2;
        Assert.True(Math.Abs(support.Point.YMeters - middle) <= 200);
    }

    [Fact]
    public void AttackerSupportStandsBehindItsFrontLineInTheSameGrandSector()
    {
        // Entrée à l'est : l'ennemi est à l'ouest, l'arrière à l'est.
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var attacker = Arrive(around["E"], p, "R", 4, 4, 4);
        var placement = Place(cache, Provincial(Build("D", p, Blue, 4), attacker));

        foreach (var battalion in attacker.Battalions)
        {
            var companies = Of(placement, battalion).ToArray();
            var support = Assert.Single(companies, c => c.Role == CompanyDeploymentRole.Support);

            Assert.All(companies.Where(c => c.Role == CompanyDeploymentRole.FrontLine), front =>
            {
                Assert.Equal(front.GrandSectorId, support.GrandSectorId);
                Assert.True(support.Point.XMeters - front.Point.XMeters >= 400);
            });
        }
    }

    // ---- Orientation ----

    [Fact]
    public void CompanyAtTheEndOfAWideFrontFacesTheEnemyAheadNotTheCenterOfItsFront()
    {
        // Neuf bataillons en défense face à une entrée à l'est : leur front est plus large que celui d'en face.
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var defender = Build("D", p, Blue, Enumerable.Repeat(2, 9).ToArray());
        var placement = Place(cache, Provincial(defender, Arrive(around["E"], p, "R", 4)));

        var grid = placement.Plan.Zones.Field.Grid;
        var enemyCells = placement.Plan.Zones.EnteringFrontSectorIds
            .SelectMany(id => placement.Plan.Zones.Field.Partition.GetSector(id).Cells)
            .ToArray();
        double centerX = enemyCells.Average(grid.GetCenterXMeters);
        double centerY = enemyCells.Average(grid.GetCenterYMeters);

        var end = placement.Companies
            .Where(c => c.Role == CompanyDeploymentRole.FrontLine && c.Battalion.ParentDivision == defender)
            .MaxBy(c => c.Point.YMeters)!;
        double towardCenter = BearingTo(end.Point.XMeters, end.Point.YMeters, centerX, centerY);

        // Le point le plus proche (vérifié par AssertPlacementIsConsistent) est droit devant, vers l'est.
        Assert.True(AngularGap(end.FacingDegrees, 90) <= 30, $"Orientation {end.FacingDegrees:F1}°");
        Assert.True(AngularGap(end.FacingDegrees, towardCenter) >= 45,
            $"Orientation {end.FacingDegrees:F1}°, centre du front adverse à {towardCenter:F1}°");
    }

    [Fact]
    public void EmptyOpposingFrontFallsBackToTheEntryDirection()
    {
        // Les zones de 3c ont toujours deux fronts : la règle de repli est vérifiée directement.
        Assert.Equal(270, EngagementCompanyPlacement.FaceNearest(0, 0, Array.Empty<(double, double)>(), 270));
        Assert.Equal(270, EngagementCompanyPlacement.FaceNearest(5, 5, new[] { (5.0, 5.0) }, 270));

        // Le plus proche l'emporte ; à égalité, le premier.
        Assert.Equal(90, EngagementCompanyPlacement.FaceNearest(0, 0, new[] { (0.0, 300.0), (100.0, 0.0) }, 270), 9);
        Assert.Equal(0, EngagementCompanyPlacement.FaceNearest(0, 0, new[] { (0.0, 100.0), (100.0, 0.0) }, 270), 9);
    }

    [Fact]
    public void TwoOppositeEntriesTurnEachAttackerTowardTheCenter()
    {
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var east = Arrive(around["E"], p, "RE", 4, 4);
        var west = Arrive(around["W"], p, "RW", 4, 4);
        var placement = Place(cache, Provincial(Build("D", p, Blue, 4, 4, 4, 4), east, west));

        Assert.All(placement.Companies.Where(c => c.Battalion.ParentDivision == east),
            c => Assert.InRange(c.FacingDegrees, 180.000001, 359.999999));
        Assert.All(placement.Companies.Where(c => c.Battalion.ParentDivision == west),
            c => Assert.InRange(c.FacingDegrees, 0.000001, 179.999999));
    }

    // ---- Largeur de front ----

    [Fact]
    public void FrontageIs300MetersAndNeverBelowTheFloorOf180()
    {
        Assert.Equal(300, CompanyPlacementOptions.Default.CompanyFrontageMeters);
        Assert.Equal(180, CompanyPlacementOptions.Default.MinCompanyFrontageMeters);

        // Grand secteur d'environ 1,2 km de large, d'un bord à l'autre des cases extrêmes.
        // Deux compagnies de première ligne : 300 m chacune. Huit : moins de 180 m chacune,
        // le plancher s'applique. Cinq : la largeur d'une part, entre les deux.
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var attacker = Arrive(around["E"], p, "R", 4, 16, 10);
        var placement = Place(cache, Provincial(Build("D", p, Blue, 4), attacker));

        CompanyPlacement[] FrontOf(int battalion) => Of(placement, attacker.Battalions[battalion])
            .Where(c => c.Role == CompanyDeploymentRole.FrontLine)
            .ToArray();

        Assert.All(FrontOf(0), c => Assert.Equal(300, c.FrontageMeters));
        Assert.All(FrontOf(1), c => Assert.Equal(180, c.FrontageMeters));

        // Entrée à l'est : la largeur se mesure du nord au sud, plus une demi-case de chaque côté.
        var five = FrontOf(2);
        var grid = placement.Plan.Zones.Field.Grid;
        var cells = placement.Plan.Zones.Field.Partition.GetSector(five[0].GrandSectorId).Cells;
        double width = cells.Max(grid.GetCenterYMeters) - cells.Min(grid.GetCenterYMeters) + ProvinceHexGrid.SpacingMeters;
        Assert.Equal(5, five.Length);
        Assert.InRange(width / 5, 180.001, 299.999);
        Assert.All(five, c => Assert.Equal(width / 5, c.FrontageMeters, 9));

        var (q, aroundQ, cacheQ) = World(("E", 90));
        Assert.Throws<ArgumentException>(() => Place(cacheQ,
            Provincial(Build("D", q, Blue, 4), Arrive(aroundQ["E"], q, "R", 4)),
            new CompanyPlacementOptions { CompanyFrontageMeters = 150 }));
        Assert.Throws<ArgumentException>(() => Place(cacheQ,
            Provincial(Build("D", q, Blue, 4), Arrive(aroundQ["E"], q, "R", 4)),
            new CompanyPlacementOptions { MinCompanyFrontageMeters = 0 }));
    }

    // ---- Groupes ----

    [Fact]
    public void CompaniesOfSeveralBattalionsInOneGroupStayContiguousInPlanOrder()
    {
        // Autant de bataillons que de grands secteurs au front : plusieurs partagent un secteur de réserve.
        // Chacun a deux compagnies en réserve (8 compagnies : 4 / 2 / 2).
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var attacker = Arrive(around["E"], p, "R", Enumerable.Repeat(8, EastFrontLength()).ToArray());
        var placement = Place(cache, Provincial(Build("D", p, Blue, 4), attacker));

        var shared = placement.Companies
            .Where(c => c.Battalion.ParentDivision == attacker && c.Role == CompanyDeploymentRole.Reserve)
            .GroupBy(c => c.GrandSectorId)
            .Where(g => g.Select(c => c.Battalion).Distinct().Count() > 1)
            .ToArray();

        Assert.NotEmpty(shared);

        foreach (var group in shared)
        {
            // Entrée à l'est : la coordonnée latérale croît vers le sud. L'ordre sur le terrain
            // est celui du plan, donc les compagnies d'un même bataillon sont voisines.
            var planOrder = group.ToArray();
            var groundOrder = group.OrderByDescending(c => c.Point.YMeters).ToArray();
            Assert.Equal(planOrder.Select(c => c.Company), groundOrder.Select(c => c.Company));
        }
    }

    [Fact]
    public void GroupLargerThanItsFreeCellsReusesCellsOnlyOnceAllAreTaken()
    {
        // 80 compagnies : 40 en première ligne sur un seul grand secteur de 37 cases au plus.
        var (p, around, cache) = World(("E", 90), ("W", 270));
        var attacker = Arrive(around["E"], p, "R", 80);
        var placement = Place(cache, Provincial(Build("D", p, Blue, 4), attacker));

        var front = Of(placement, attacker.Battalions[0])
            .Where(c => c.Role == CompanyDeploymentRole.FrontLine)
            .ToArray();
        var sector = placement.Plan.Zones.Field.Partition.GetSector(front[0].GrandSectorId);

        Assert.Equal(40, front.Length);
        Assert.True(sector.Cells.Count < front.Length);
        Assert.Equal(sector.Cells.Order(), front.Select(c => c.Cell).Distinct().Order());
    }

    [Fact]
    public void TruncatedGrandSectorAtTheBorderHoldsItsCompaniesOnItsOwnCells()
    {
        // Un bataillon de plus que de grands secteurs au front : il attend en seconde ligne, sur le bord est.
        var (p, around, cache) = World(("E", 90), ("W", 270));
        int frontLength = EastFrontLength();
        var attacker = Arrive(around["E"], p, "R", Enumerable.Repeat(4, frontLength + 1).ToArray());
        var placement = Place(cache, Provincial(Build("D", p, Blue, 4), attacker));

        var companies = Of(placement, attacker.Battalions[frontLength]).ToArray();
        var sector = placement.Plan.Zones.Field.Partition.GetSector(companies[0].GrandSectorId);

        Assert.True(sector.IsPartial);
        Assert.All(companies, c => Assert.Equal(sector.Id, c.GrandSectorId));
        Assert.All(companies, c => Assert.Contains(c.Cell, sector.Cells));
        Assert.Equal(companies.Length, companies.Select(c => c.Cell).Distinct().Count());
        Assert.True(companies.Max(c => c.Point.YMeters) - companies.Min(c => c.Point.YMeters) >= 400);
    }

    [Fact]
    public void SameInputsGiveTheSamePlacement()
    {
        (string Company, int Cell, double Facing, double Frontage)[] Run()
        {
            var (p, around, cache) = World(("N", 0), ("E", 90), ("W", 270));
            var battle = Provincial(Build("D", p, Blue, 4, 3, 2, 1),
                Arrive(around["E"], p, "RE", 4, 4), Arrive(around["N"], p, "RN", 3, 3));
            return Place(cache, battle).Companies
                .Select(c => (c.Company.Name, c.Cell, c.FacingDegrees, c.FrontageMeters))
                .ToArray();
        }

        Assert.Equal(Run(), Run());
    }
}
