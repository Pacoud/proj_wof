using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;
using static WoF.Simulation.Tests.Combat.Tactical.TacticalGridTestFactory;

namespace WoF.Simulation.Tests.Combat.Tactical;

/// <summary>
/// Étape 2b, option (b) : la détection et le tir mesurent vers le centre
/// de la cible, puis vers les extrémités de son front, et l'observateur
/// signale le point qu'il a réellement vu.
/// </summary>
public sealed class TacticalFrontMeasurementTests
{
    private static readonly double RowSpacing =
        ProvinceHexGrid.SpacingMeters * HexCoord.RowSpacingFactor;

    // ---- Détection ----

    [Fact]
    public void FrontCoveringThreeCells_IsDetectedFromEach_InTheCellSeen()
    {
        // Front de 400 m orienté vers le nord, au centre de la case
        // (20, 6) : ses extrémités tombent aux centres des cases (19, 6)
        // et (21, 6). Trois observateurs se tiennent 400 m au nord de
        // l'extrémité gauche, du centre et de l'extrémité droite. Un mur
        // à 300 m de la cible ne laisse à chacun que la partie en face.
        var (b, target, observers) = ThreeCellScenario(walls: true);

        var expected = new[] { (19, -200.0), (20, 0.0), (21, 200.0) };
        var center = b.Map.GetPosition(target)!.Point;

        for (int i = 0; i < observers.Length; i++)
        {
            var report = Assert.Single(b.Perception.GetReports(observers[i]));
            var (column, dx) = expected[i];

            Assert.Equal(TacticalKnowledgeLevel.Located, report.CurrentObservationLevel);
            Assert.Equal(SectorId(b, column, row: 6), report.LastKnownSectorId);
            Assert.Equal(center.XMeters + dx, report.LastKnownPosition!.Value.XMeters, 6);
        }

        // Sans mur, chacun voit d'abord le centre : position et secteur
        // principal, comme avant l'option (b).
        var (open, openTarget, openObservers) = ThreeCellScenario(walls: false);

        Assert.All(openObservers, observer =>
        {
            var report = Assert.Single(open.Perception.GetReports(observer));

            Assert.Equal(openTarget.Sector.Id, report.LastKnownSectorId);
            Assert.Equal(
                open.Map.GetPosition(openTarget)!.Point.XMeters,
                report.LastKnownPosition!.Value.XMeters,
                6);
        });
    }

    [Fact]
    public void EndWithinVisualRange_IsSeen_WhenTheCenterIsBeyond()
    {
        // Cible à 600 m, portée visuelle de 500 m. Avec un front de
        // 400 m vers le nord, l'extrémité gauche est à 400 m, au centre
        // de la case (12, 4) : la cible y est localisée.
        static (GridBattle Battle, TacticalEnemyContactReport? Report) Seen(double frontage)
        {
            var b = CreateBattle(CreateGrid(30, 9, (_, _) => 0));

            var observer = Place(b, b.French, 0, column: 10, row: 4);
            var target = Place(b, b.German, 0, column: 13, row: 4,
                facingDegrees: 0, frontageMeters: frontage);

            var perception = Detect(b, target, TacticalActivity.Stationary,
                TacticalConcealment.None, new TacticalDetectionProfile(500, 0, 360, 1));

            return (b, perception.GetReports(observer).SingleOrDefault());
        }

        var (battle, report) = Seen(frontage: 400);

        Assert.NotNull(report);
        Assert.Equal(TacticalKnowledgeLevel.Located, report.CurrentObservationLevel);
        Assert.Equal(battle.Layout.GetSector(Cell(battle.Grid, 12, 4)).Id, report.LastKnownSectorId);

        Assert.Null(Seen(frontage: 20).Report);
    }

    [Fact]
    public void AcousticRange_IsMeasuredToTheNearestPointOfTheFront()
    {
        // Cible qui tire, presque invisible (portée visuelle de 6,25 m),
        // à 600 m ; portée acoustique de 450 m. Avec un front de 400 m,
        // son extrémité gauche est à 400 m : elle est entendue.
        static TacticalEnemyContactReport? Heard(int column, double frontage, double acoustic = 450)
        {
            var b = CreateBattle(CreateGrid(30, 9, (_, _) => 0));

            var observer = Place(b, b.French, 0, column: 10, row: 4);
            var target = Place(b, b.German, 0, column: column, row: 4,
                facingDegrees: 0, frontageMeters: frontage);

            var perception = Detect(b, target, TacticalActivity.Firing,
                TacticalConcealment.Strong,
                new TacticalDetectionProfile(10, acoustic, 360, 1));

            return perception.GetReports(observer).SingleOrDefault();
        }

        var report = Assert.IsType<TacticalEnemyContactReport>(Heard(column: 13, frontage: 400));

        Assert.Equal(TacticalKnowledgeLevel.Suspected, report.CurrentObservationLevel);
        Assert.Null(report.LastKnownPosition);

        Assert.Null(Heard(column: 13, frontage: 20));

        // Centres confondus : le centre, à 0 m, n'est pas vu mais il est
        // entendu, même quand les extrémités sont hors de portée acoustique.
        Assert.NotNull(Heard(column: 10, frontage: 400, acoustic: 100));
    }

    [Fact]
    public void FrontPoints_FollowTheGround_AndDropAnEndOffTheGrid()
    {
        // Plan incliné : 5 cm par mètre vers l'est. Front de 800 m au
        // centre de la case (0, 4), vers le nord : l'extrémité gauche
        // tombe 400 m à l'ouest du cadre, sans sol ; l'extrémité droite
        // est au sol, 20 m plus haut que le centre.
        var b = CreateBattle(CreateGrid(30, 9, (x, _) => 0.05 * x));

        var target = Place(b, b.German, 0, column: 0, row: 4,
            facingDegrees: 0, frontageMeters: 800);

        var position = b.Map.GetPosition(target)!;
        var points = b.Map.GetFrontPoints(position);

        Assert.Equal(2, points.Count);
        Assert.Equal(position.Point, points[0]);
        Assert.Equal(position.Point.XMeters + 400, points[1].XMeters, 6);
        Assert.Equal(position.Point.GroundElevationMeters + 20, points[1].GroundElevationMeters, 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => points[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => default(TacticalFrontPoints)[0]);

        // La détection ne lit jamais le sol hors du cadre.
        var observer = Place(b, b.French, 0, column: 3, row: 4);

        var perception = Detect(b, target, TacticalActivity.Stationary,
            TacticalConcealment.None, new TacticalDetectionProfile(1000, 0, 360, 1));

        Assert.Single(perception.GetReports(observer));

        // Sans grille, les extrémités gardent l'altitude du centre.
        var s = FireScenario(germanFacing: 0);
        var handBuilt = s.Geometry.GetFrontPoints(s.Geometry.GetPosition(s.German)!);

        Assert.Equal(3, handBuilt.Count);
        Assert.Equal(new TacticalPoint(80, 0, 100), handBuilt[1]);
        Assert.Equal(new TacticalPoint(120, 0, 100), handBuilt[2]);
    }

    [Fact]
    public void SectorOfASeenPoint_WithoutGrid_IsTheNearestEnvelopeAmongThePrimaryAndItsNeighbors()
    {
        var s = FireScenario();
        var field = s.Battle.Battlefield;

        var east = new BattleSector(2, "Est", TerrainType.Plains);
        var north = new BattleSector(3, "Nord", TerrainType.Plains);
        field.AddSector(east);
        field.AddSector(north);
        field.Connect(s.Sector, east, 300);

        // Le secteur principal est centré en (50, 0), rayon 500.
        s.Geometry.DefineSector(east, 400, 0, 200);
        s.Geometry.DefineSector(north, 50, 700, 300);

        static TacticalPoint P(double x, double y) => new(x, y, 0);

        // Dans le seul secteur principal.
        Assert.Same(s.Sector, s.Geometry.FindSectorAt(s.Sector, P(-100, 0)));

        // Dans les deux enveloppes : le centre le plus proche.
        Assert.Same(east, s.Geometry.FindSectorAt(s.Sector, P(300, 0)));
        Assert.Same(s.Sector, s.Geometry.FindSectorAt(s.Sector, P(210, 0)));

        // À égalité de distance, le plus petit identifiant.
        Assert.Same(s.Sector, s.Geometry.FindSectorAt(s.Sector, P(225, 0)));

        // Dans un secteur voisin seulement.
        Assert.Same(east, s.Geometry.FindSectorAt(s.Sector, P(580, 0)));

        // Dans un secteur qui n'est pas voisin, ou hors de toute enveloppe :
        // le secteur principal.
        Assert.Same(s.Sector, s.Geometry.FindSectorAt(s.Sector, P(50, 900)));
        Assert.Same(s.Sector, s.Geometry.FindSectorAt(s.Sector, P(2000, 2000)));
    }

    // ---- Tir ----

    [Fact]
    public void Fire_IsAllowedTowardAVisibleEnd_WhenTheCenterIsHidden()
    {
        // Cible à 100 m, front de 40 m perpendiculaire à la ligne de tir.
        // Un mur court masque le centre, pas les extrémités ; un mur
        // long masque les trois.
        var shortWall = FireScenario();
        AddWall(shortWall, halfLength: 5);

        Assert.Equal(TacticalDirectFireStatus.Allowed, Evaluate(shortWall).Status);

        var longWall = FireScenario();
        AddWall(longWall, halfLength: 30);

        Assert.Equal(
            TacticalDirectFireStatus.LineOfSightUnavailable,
            Evaluate(longWall).Status);
    }

    [Fact]
    public void Fire_IsAllowedTowardAnEndInRange_WhenTheCenterIsOutOfRange()
    {
        // Front orienté vers le nord : extrémités à 80 m et 120 m.
        var s = FireScenario(germanFacing: 0);

        Assert.Equal(TacticalDirectFireStatus.Allowed, Evaluate(s, range: 90).Status);
        Assert.Equal(TacticalDirectFireStatus.OutOfRange, Evaluate(s, range: 70).Status);
    }

    [Fact]
    public void RefusedFire_ReportsTheStatusOfTheCenter()
    {
        // Centre hors de portée ; extrémité proche à portée mais masquée ;
        // extrémité lointaine hors de portée. Le refus est celui du centre.
        var lengthwise = FireScenario(germanFacing: 0);
        AddWall(lengthwise, halfLength: 5);

        Assert.Equal(TacticalDirectFireStatus.OutOfRange, Evaluate(lengthwise, range: 90).Status);

        // Centre à portée mais masqué ; extrémités, à 102 m, hors de
        // portée. Le refus est encore celui du centre, pas du dernier point.
        var across = FireScenario();
        AddWall(across, halfLength: 5);

        Assert.Equal(
            TacticalDirectFireStatus.LineOfSightUnavailable,
            Evaluate(across, range: 101).Status);
    }

    [Fact]
    public void CompaniesAtTheSamePoint_CanFireAtTheEndsOfTheFront()
    {
        // Même point : la mesure vers le centre est impossible, mais
        // l'extrémité gauche, 20 m au nord, est devant le tireur.
        var s = FireScenario(germanX: 0, frenchFacing: 0);

        Assert.Equal(TacticalDirectFireStatus.Allowed, Evaluate(s).Status);
    }

    [Fact]
    public void AimingAtASeenEnd_HitsAboutHalfAsOftenAsAimingAtTheCenter()
    {
        // Contrepartie de l'option (b) : le tir vise le point signalé.
        // Visé à l'extrémité d'un front de 40 m, à peu près la moitié des
        // projectiles tombe au-delà de lui.
        var (centerReport, centerProbability) = DetectAndFire(wallHalfLength: null);
        var (endReport, endProbability) = DetectAndFire(wallHalfLength: 5);

        Assert.Equal(new TacticalPoint(100, 0, 100), centerReport);
        Assert.Equal(new TacticalPoint(100, 20, 100), endReport);

        Assert.True(centerProbability > 0);
        Assert.InRange(endProbability / centerProbability, 0.49, 0.51);
    }

    [Fact]
    public void ProtectionOfACompanySeenByItsEnd_AppliesOnce()
    {
        // L'extrémité visée et le centre ne font qu'une compagnie : sa
        // disposition défensive ne compte qu'une fois.
        var (_, open) = DetectAndFire(wallHalfLength: 5, buildings: true);
        var (_, covered) = DetectAndFire(wallHalfLength: 5, buildings: true, crouchedInBuilding: true);

        // Accroupi (0,6) derrière un bâtiment (1 - 0,5).
        Assert.True(open > 0);
        Assert.Equal(open * 0.3, covered, 10);
    }

    // ---- Scénarios sur la grille ----

    private sealed record GridDetectionResult(GridBattle Grid, TacticalPerceptionState Perception)
    {
        internal TacticalGeometryMap Map => Grid.Map;
    }

    private static (GridDetectionResult Battle, CompanyDeployment Target, CompanyDeployment[] Observers)
        ThreeCellScenario(bool walls)
    {
        var b = CreateBattle(CreateGrid(30, 12, (_, _) => 0));

        var target = Place(b, b.German, 0, column: 20, row: 6,
            facingDegrees: 0, frontageMeters: 400);

        // Rangée 8 : 2 × 173,2 m plus au nord ; décalage vers 400 m.
        double dy = 400 - 2 * RowSpacing;

        var observers = new[]
        {
            Place(b, b.French, 0, column: 19, row: 8, dy: dy, facingDegrees: 180),
            Place(b, b.French, 1, column: 20, row: 8, dy: dy, facingDegrees: 180),
            Place(b, b.French, 2, column: 21, row: 8, dy: dy, facingDegrees: 180)
        };

        if (walls)
        {
            var center = b.Map.GetPosition(target)!.Point;
            double y = center.YMeters + 300;
            double x = center.XMeters;

            // Ouvertures de 40 m en face de chaque partie du front.
            var segments = new[] { (-400.0, -220.0), (-180.0, -20.0), (20.0, 180.0), (220.0, 400.0) };

            for (int i = 0; i < segments.Length; i++)
            {
                b.Map.AddLineObstacle(new TacticalLineObstacle(
                    i + 1, $"Mur {i + 1}", TacticalLineObstacleKind.Wall,
                    x + segments[i].Item1, y, x + segments[i].Item2, y,
                    topElevationMeters: 10));
            }
        }

        var perception = Detect(b, target, TacticalActivity.Stationary,
            TacticalConcealment.None, new TacticalDetectionProfile(500, 0, 360, 1));

        return (new GridDetectionResult(b, perception), target, observers);
    }

    private static int SectorId(GridDetectionResult b, int column, int row) =>
        b.Grid.Layout.GetSector(Cell(b.Grid.Grid, column, row)).Id;

    private static TacticalPerceptionState Detect(
        GridBattle b,
        CompanyDeployment target,
        TacticalActivity activity,
        TacticalConcealment concealment,
        TacticalDetectionProfile profile)
    {
        var signatures = new TacticalSignatureFrame(b.Battle);
        signatures.Set(target, new TacticalUnitSignature(activity, concealment));

        var perception = new TacticalPerceptionState(b.Battle);

        TacticalDetectionSystem.Update(
            b.Battle, b.Map, b.Diplomacy, perception, signatures,
            currentHour: 2, profile);

        return perception;
    }

    // ---- Scénario de tir construit à la main ----

    private static readonly TacticalHitProfile HitProfile = new(2, 1, 0.6);

    private static readonly TacticalDefenseProfile DefenseProfile =
        new(1, 0.6, 0.3, 0.1, 0.5, 0.7);

    private sealed record FireSetup(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        TacticalPerceptionState Perception,
        DiplomacySystem Diplomacy,
        CompanyDeployment French,
        CompanyDeployment German,
        BattleSector Sector);

    /// <summary>
    /// Un secteur de 500 m de rayon ; tireur français en (0, 0), cible
    /// allemande en (germanX, 0), toutes deux à 100 m d'altitude, fronts
    /// de 40 m. Le tireur localise la cible à son centre.
    /// </summary>
    private static FireSetup FireScenario(
        double germanFacing = 90,
        double germanX = 100,
        double frenchFacing = 90,
        bool buildings = false)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Province", TerrainType.Plains);

        var french = MakeArmedDivision(france, province);
        var german = MakeArmedDivision(germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);
        engagements.DetectProvinceEngagements(new[] { french, german }, currentHour: 1);

        var field = new TacticalBattlefield();
        var sector = new BattleSector(
            1, "Plaine", TerrainType.Plains,
            features: buildings ? SectorFeature.Buildings : SectorFeature.None);
        field.AddSector(sector);

        var battle = new TacticalBattle(Assert.Single(engagements.ActiveEngagements), field);

        var shooter = battle.DeployCompany(
            french.Battalions[0].Companies[0], sector, CompanyDeploymentRole.FrontLine);

        var target = battle.DeployCompany(
            german.Battalions[0].Companies[0], sector, CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);
        geometry.DefineSector(sector, 50, 0, radiusMeters: 500);

        geometry.PlaceCompany(shooter, new TacticalPoint(0, 0, 100), frenchFacing, 40);
        geometry.PlaceCompany(target, new TacticalPoint(germanX, 0, 100), germanFacing, 40);

        battle.PersonnelExposure.Set(target, new TacticalPersonnelExposure(
            exposed: 80, sheltered: 20, reserve: 0, exposedDepthMeters: 20));

        var perception = new TacticalPerceptionState(battle);

        perception.Apply(1, new[]
        {
            new TacticalObservationEvent(
                shooter, target, new TacticalPoint(germanX, 0, 100), sector.Id,
                TacticalKnowledgeLevel.Located, PositionUncertaintyMeters: 0)
        });

        return new FireSetup(battle, geometry, perception, diplomacy, shooter, target, sector);
    }

    private static void AddWall(FireSetup s, double halfLength)
    {
        s.Geometry.AddLineObstacle(new TacticalLineObstacle(
            1, "Mur", TacticalLineObstacleKind.Wall,
            50, -halfLength, 50, halfLength,
            topElevationMeters: 110));
    }

    private static TacticalDirectFireDecision Evaluate(FireSetup s, double range = 200) =>
        TacticalDirectFireEvaluator.Evaluate(
            s.Battle, s.Geometry, s.Diplomacy, s.Perception,
            s.French, s.German, currentHour: 1,
            new TacticalDirectFireProfile(range, fireArcDegrees: 120));

    /// <summary>
    /// Détection réelle par le tireur à l'heure 2, puis une salve sur la
    /// cible : point signalé et probabilité de contact par projectile.
    /// </summary>
    private static (TacticalPoint Reported, double Probability) DetectAndFire(
        double? wallHalfLength,
        bool buildings = false,
        bool crouchedInBuilding = false)
    {
        var s = FireScenario(buildings: buildings);

        if (wallHalfLength is double halfLength)
            AddWall(s, halfLength);

        if (crouchedInBuilding)
        {
            s.Battle.DefensiveState.Set(
                s.German,
                new TacticalDefensiveDisposition(TacticalStance.Crouched, TacticalCover.Building));
        }

        var signatures = new TacticalSignatureFrame(s.Battle);
        signatures.Set(s.German, new TacticalUnitSignature(
            TacticalActivity.Stationary, TacticalConcealment.None));

        TacticalDetectionSystem.Update(
            s.Battle, s.Geometry, s.Diplomacy, s.Perception, signatures,
            currentHour: 2, new TacticalDetectionProfile(200, 0, 360, 1));

        var report = Assert.Single(s.Perception.GetReports(s.French));

        var phase = s.Battle.DirectFire.Execute(
            new[]
            {
                new TacticalFireOrder(
                    s.French, s.German, new TacticalDirectFireProfile(250),
                    WeaponsCommitted: 10, RoundsPerWeapon: 3)
            },
            s.Geometry, s.Diplomacy, s.Perception, hour: 2);

        Assert.Single(phase.Events);

        var contact = Assert.Single(s.Battle.DirectFire
            .ResolvePersonnelLastPhase(HitProfile, DefenseProfile, seed: 42).Events);

        return (report.LastKnownPosition!.Value, contact.ProbabilityPerRound);
    }

    private static Division MakeArmedDivision(Country country, Province province)
    {
        var division = new Division(
            $"{country.Name} Division", province, fuel: 100, ammunition: 100, country: country,
            composition: new DivisionComposition(
                manpower: 100, infantryEquipment: 100, artillery: 0, tanks: 0));

        var battalion = new Battalion(1, "Bataillon", BattalionType.Infantry);

        battalion.AddCompany(new Company(
            1, "Compagnie 1", CompanyType.Infantry,
            new CompanyComposition(
                manpower: 100,
                infantryEquipment: 100,
                ammunition: new AmmunitionTotals(smallArms: 1000))));

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }
}
