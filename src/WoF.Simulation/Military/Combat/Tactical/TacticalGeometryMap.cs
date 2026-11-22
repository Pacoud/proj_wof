
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Enveloppe circulaire approximative d'un secteur.
/// Il ne s'agit pas d'une frontière géographique exacte.
/// </summary>
public sealed class SectorGeometry
{
    public BattleSector Sector { get; }

    public double CenterXMeters { get; }
    public double CenterYMeters { get; }

    public double RadiusMeters { get; }

    internal SectorGeometry(
        BattleSector sector,
        double centerXMeters,
        double centerYMeters,
        double radiusMeters)
    {
        Sector = sector;
        CenterXMeters = centerXMeters;
        CenterYMeters = centerYMeters;
        RadiusMeters = radiusMeters;
    }

    public bool Contains(TacticalPoint point)
    {
        double dx =
            point.XMeters - CenterXMeters;

        double dy =
            point.YMeters - CenterYMeters;

        return double.Hypot(dx, dy) <= RadiusMeters;
    }
}

/// <summary>
/// Position géométrique d'un déploiement existant.
/// Ne contient aucune ressource militaire.
/// </summary>
public sealed class CompanyTacticalPosition
{
    public CompanyDeployment Deployment { get; }

    public TacticalPoint Point { get; }

    // 0° = nord, 90° = est,
    // 180° = sud, 270° = ouest.
    public double FacingDegrees { get; }

    public double FrontageMeters { get; }

    internal CompanyTacticalPosition(
        CompanyDeployment deployment,
        TacticalPoint point,
        double facingDegrees,
        double frontageMeters)
    {
        Deployment = deployment;
        Point = point;
        FacingDegrees = facingDegrees;
        FrontageMeters = frontageMeters;
    }
}

/// <summary>
/// Relation spatiale du premier déploiement
/// vers le second.
/// </summary>
public sealed record TacticalSpatialRelation(
    double HorizontalDistanceMeters,
    double ElevationDifferenceMeters,
    double StraightLineDistanceMeters,
    double? BearingDegrees,
    double? RelativeBearingDegrees);

/// <summary>
/// Points d'une compagnie vers lesquels la détection et le tir mesurent,
/// dans l'ordre où ils sont essayés (option b) : le centre, puis
/// l'extrémité gauche, puis l'extrémité droite du front.
///
/// Sur une grille, les extrémités sont au sol ; une extrémité sans sol
/// (hors du cadre de la province) est absente. Le centre est toujours
/// présent, à l'indice 0.
/// </summary>
internal readonly struct TacticalFrontPoints
{
    private readonly TacticalPoint _center;
    private readonly TacticalPoint _first;
    private readonly TacticalPoint _second;

    internal int Count { get; }

    internal TacticalFrontPoints(
        TacticalPoint center,
        TacticalPoint? leftEnd,
        TacticalPoint? rightEnd)
    {
        _center = center;
        _first = default;
        _second = default;
        Count = 1;

        // Les extrémités présentes sont rangées à la suite du centre,
        // la gauche avant la droite.
        if (leftEnd is TacticalPoint left)
        {
            _first = left;
            Count++;
        }

        if (rightEnd is TacticalPoint right)
        {
            if (Count == 1)
                _first = right;
            else
                _second = right;

            Count++;
        }
    }

    // Une valeur par défaut ne contient aucun point.
    internal TacticalPoint this[int index] => index switch
    {
        0 when Count > 0 => _center,
        1 when Count > 1 => _first,
        2 when Count > 2 => _second,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}



public sealed class TacticalGeometryMap
{
    private readonly TacticalBattle _battle;

    internal TacticalBattle Battle => _battle;

    private readonly List<SectorGeometry> _sectorGeometries =
        new();

    private readonly List<CompanyTacticalPosition> _positions =
        new();

    private readonly Dictionary<BattleSector, SectorGeometry>
        _bySector = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<
        CompanyDeployment,
        CompanyTacticalPosition>
        _byDeployment = new(ReferenceEqualityComparer.Instance);


    private readonly List<TacticalLineObstacle> _lineObstacles =
        new();

    private readonly Dictionary<int, TacticalLineObstacle> _obstaclesById = new();

    public IReadOnlyList<TacticalLineObstacle> LineObstacles
    {
        get;
    }


    public IReadOnlyList<SectorGeometry> SectorGeometries
    {
        get;
    }

    public IReadOnlyList<CompanyTacticalPosition> CompanyPositions
    {
        get;
    }

    internal long Revision { get; private set; }


    /// <summary>
    /// Sol et couvert de la province, quand le champ de bataille vient
    /// de sa grille. Null pour un champ de bataille construit à la main.
    /// </summary>
    internal TacticalTerrainSurface? Terrain { get; private set; }

    /// <summary>
    /// Change le secteur d'une compagnie et la place au point donné,
    /// qui doit se trouver dans la géométrie du secteur de destination.
    /// Le point n'est plus recalculé au centre du secteur.
    /// </summary>
    internal void RelocateCompany(
        CompanyDeployment deployment,
        BattleSector destination,
        TacticalPoint point)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(destination);

        EnsureActive();

        if (!_battle.ContainsDeployment(deployment)
            || !_battle.Battlefield.ContainsSector(destination))
        {
            throw new InvalidOperationException(
                "Invalid tactical relocation.");
        }

        if (!_byDeployment.TryGetValue(
            deployment, out var previous))
        {
            throw new InvalidOperationException(
                "Company has no tactical position.");
        }

        if (!_bySector.TryGetValue(
            destination, out var destinationGeometry))
        {
            throw new InvalidOperationException(
                "Destination has no sector geometry.");
        }

        if (!destinationGeometry.Contains(point))
            throw new InvalidOperationException(
                "Company position is outside its destination sector.");

        EnsureOnGround(point);

        Replace(previous, point);
    }

    public TacticalGeometryMap(TacticalBattle battle)
    {
        ArgumentNullException.ThrowIfNull(battle);

        if (!battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "Cannot initialize geometry for an ended battle.");

        _battle = battle;

        SectorGeometries = _sectorGeometries.AsReadOnly();
        CompanyPositions = _positions.AsReadOnly();
        LineObstacles = _lineObstacles.AsReadOnly();
    }

    /// <summary>
    /// Attribue un centre et un rayon approximatif
    /// à un secteur appartenant à la bataille.
    /// </summary>
    public SectorGeometry DefineSector(
        BattleSector sector,
        double centerXMeters,
        double centerYMeters,
        double radiusMeters)
    {
        ArgumentNullException.ThrowIfNull(sector);

        EnsureActive();

        if (!_battle.Battlefield.ContainsSector(sector))
            throw new InvalidOperationException(
                "Sector does not belong to this battlefield.");

        if (!double.IsFinite(centerXMeters))
            throw new ArgumentOutOfRangeException(
                nameof(centerXMeters));

        if (!double.IsFinite(centerYMeters))
            throw new ArgumentOutOfRangeException(
                nameof(centerYMeters));

        if (!double.IsFinite(radiusMeters)
            || radiusMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusMeters));
        }

        if (_bySector.ContainsKey(sector))
            throw new InvalidOperationException(
                "Sector geometry already defined.");

        var geometry = new SectorGeometry(
            sector,
            centerXMeters,
            centerYMeters,
            radiusMeters);

        _bySector.Add(sector, geometry);
        _sectorGeometries.Add(geometry);

        Revision++;

        return geometry;
    }

    public SectorGeometry? GetSectorGeometry(
        BattleSector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);

        return _bySector.TryGetValue(
            sector,
            out var geometry)
                ? geometry
                : null;
    }

        
    public void AddLineObstacle(TacticalLineObstacle obstacle)
    {
        ArgumentNullException.ThrowIfNull(obstacle);

        EnsureActive();

        if (!_obstaclesById.TryAdd(obstacle.Id, obstacle))
            throw new InvalidOperationException(
                "Obstacle ID already exists in this geometry map.");

        _lineObstacles.Add(obstacle);

        Revision++;
    }


    /// <summary>
    /// Place une compagnie déjà déployée
    /// dans son secteur d'affectation.
    /// </summary>
    public CompanyTacticalPosition PlaceCompany(
        CompanyDeployment deployment,
        TacticalPoint point,
        double facingDegrees,
        double frontageMeters)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        EnsureActive();

        if (!_battle.Deployments.Contains(deployment))
            throw new InvalidOperationException(
                "Deployment does not belong to this battle.");

        if (!_bySector.TryGetValue(
            deployment.Sector,
            out var sectorGeometry))
        {
            throw new InvalidOperationException(
                "Sector geometry is not defined.");
        }

        if (!sectorGeometry.Contains(point))
            throw new InvalidOperationException(
                "Company position is outside its assigned sector.");

        EnsureOnGround(point);

        if (!double.IsFinite(facingDegrees)
            || facingDegrees < 0
            || facingDegrees >= 360)
        {
            throw new ArgumentOutOfRangeException(
                nameof(facingDegrees));
        }

        if (!double.IsFinite(frontageMeters)
            || frontageMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frontageMeters));
        }

        if (_byDeployment.ContainsKey(deployment))
            throw new InvalidOperationException(
                "Company deployment already has a position.");

        var position = new CompanyTacticalPosition(
            deployment,
            point,
            facingDegrees,
            frontageMeters);

        _byDeployment.Add(deployment, position);
        _positions.Add(position);

        Revision++;
        
        return position;
    }

    public CompanyTacticalPosition? GetPosition(
        CompanyDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return _byDeployment.TryGetValue(
            deployment,
            out var position)
                ? position
                : null;
    }

    /// <summary>
    /// Mesure la relation spatiale entre deux
    /// compagnies déjà positionnées.
    ///
    /// Ne teste ni la visibilité ni la portée.
    /// </summary>
    public TacticalSpatialRelation Measure(
        CompanyDeployment first,
        CompanyDeployment second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (ReferenceEquals(first, second))
            throw new ArgumentException(
                "Two distinct deployments are required.");

        if (!_byDeployment.TryGetValue(first, out var a)
            || !_byDeployment.TryGetValue(second, out var b))
        {
            throw new InvalidOperationException(
                "Both deployments must have positions.");
        }

        double dx = b.Point.XMeters - a.Point.XMeters;
        double dy = b.Point.YMeters - a.Point.YMeters;

        double horizontal =
            a.Point.HorizontalDistanceTo(b.Point);

        double elevationDifference =
            b.Point.GroundElevationMeters -
            a.Point.GroundElevationMeters;

        double spatial =
            a.Point.SpatialDistanceTo(b.Point);

        // Deux compagnies au même X/Y :
        // la direction horizontale est indéfinie.
        if (horizontal == 0)
        {
            return new TacticalSpatialRelation(
                horizontal,
                elevationDifference,
                spatial,
                BearingDegrees: null,
                RelativeBearingDegrees: null);
        }

        // Orientation absolue :
        // 0 = nord, 90 = est.
        double bearing =
            (Math.Atan2(dx, dy) * 180.0 / Math.PI
             + 360.0) % 360.0;

        // Orientation relative au front de A :
        // 0 = devant, 90 = droite,
        // 180 = derrière, 270 = gauche.
        double relativeBearing =
            (bearing - a.FacingDegrees + 360.0) % 360.0;

        return new TacticalSpatialRelation(
            horizontal,
            elevationDifference,
            spatial,
            bearing,
            relativeBearing);
    }

    private void EnsureActive()
    {
        if (!_battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "Cannot modify geometry of an ended battle.");
    }


    /// <summary>
    /// Met a jour la position d'une compagnie en transit.
    ///
    /// Contrairement a PlaceCompany, le point peut
    /// se trouver entre deux enveloppes de secteurs.
    /// Le systeme de mouvement valide la liaison.
    /// </summary>
    internal void MoveCompanyAlongConnection(
        CompanyDeployment deployment,
        TacticalPoint point)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        EnsureActive();

        if (!_battle.ContainsDeployment(deployment)
            || !deployment.IsInTransit)
        {
            throw new InvalidOperationException(
                "Company is not in a valid tactical transit.");
        }

        if (!_byDeployment.TryGetValue(
            deployment, out var previous))
        {
            throw new InvalidOperationException(
                "Company has no tactical position.");
        }

        EnsureOnGround(point);

        Replace(previous, point);
    }

    // ---- Sol ----

    /// <summary>
    /// Relie la carte au sol d'une province. Appelé par
    /// ProvinceBattlefieldLayout.DefineGeometry, avant tout placement.
    /// </summary>
    internal void AttachTerrain(TacticalTerrainSurface terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        EnsureActive();

        if (Terrain is not null)
            throw new InvalidOperationException(
                "Terrain is already attached to this geometry map.");

        if (_positions.Count > 0)
            throw new InvalidOperationException(
                "Terrain must be attached before placing companies.");

        Terrain = terrain;

        Revision++;
    }

    /// <summary>
    /// Point au sol dans un secteur : altitude du terrain si la carte
    /// est reliée à une grille, sinon altitude déclarée du secteur.
    /// À utiliser pour construire les points passés à PlaceCompany.
    /// </summary>
    public TacticalPoint GetGroundPoint(
        BattleSector sector,
        double xMeters,
        double yMeters)
    {
        ArgumentNullException.ThrowIfNull(sector);

        if (!_battle.Battlefield.ContainsSector(sector))
            throw new InvalidOperationException(
                "Sector does not belong to this battlefield.");

        if (!double.IsFinite(xMeters))
            throw new ArgumentOutOfRangeException(nameof(xMeters));

        if (!double.IsFinite(yMeters))
            throw new ArgumentOutOfRangeException(nameof(yMeters));

        double elevation = Terrain is null
            ? sector.ElevationMeters
            : Terrain.GetGroundElevation(xMeters, yMeters);

        return new TacticalPoint(xMeters, yMeters, elevation);
    }

    /// <summary>Centre de la géométrie d'un secteur, au sol.</summary>
    public TacticalPoint GetSectorCenterPoint(BattleSector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);

        var geometry = GetSectorGeometry(sector)
            ?? throw new InvalidOperationException(
                "Sector geometry is not defined.");

        return GetGroundPoint(
            sector,
            geometry.CenterXMeters,
            geometry.CenterYMeters);
    }

    /// <summary>
    /// Point d'un trajet entre deux secteurs. Sur une grille, l'altitude
    /// est celle du sol ; sinon l'altitude calculée par l'appelant
    /// (interpolée entre les deux extrémités) est conservée.
    /// </summary>
    internal TacticalPoint ToGroundPoint(TacticalPoint point)
    {
        return Terrain is null
            ? point
            : new TacticalPoint(
                point.XMeters,
                point.YMeters,
                Terrain.GetGroundElevation(point.XMeters, point.YMeters));
    }

    /// <summary>
    /// Centre et extrémités du front d'une compagnie placée, pour la
    /// détection et le tir (option b).
    ///
    /// Les extrémités viennent de TacticalCompanyFootprint. Sans grille,
    /// elles gardent l'altitude du centre ; sur une grille, elles sont
    /// ramenées au sol, et une extrémité sans sol est absente.
    /// </summary>
    internal TacticalFrontPoints GetFrontPoints(
        CompanyTacticalPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var footprint = new TacticalCompanyFootprint(position);

        return new TacticalFrontPoints(
            position.Point,
            OnGroundOrNull(footprint.LeftEnd),
            OnGroundOrNull(footprint.RightEnd));
    }

    private TacticalPoint? OnGroundOrNull(TacticalPoint point)
    {
        if (Terrain is null)
            return point;

        return Terrain.TryGetGroundElevation(
            point.XMeters,
            point.YMeters,
            out double elevation)
                ? new TacticalPoint(point.XMeters, point.YMeters, elevation)
                : null;
    }

    /// <summary>
    /// Secteur où se trouve un point vu du front d'une compagnie, dont le
    /// secteur principal est donné.
    ///
    /// - Sur une grille : le secteur de la case qui contient le point.
    /// - Sans grille : parmi le secteur principal et ses voisins, celui
    ///   dont l'enveloppe contient le point et dont le centre est le plus
    ///   proche ; à égalité, le plus petit identifiant.
    ///
    /// À défaut (point hors du champ de bataille), le secteur principal.
    /// </summary>
    internal BattleSector FindSectorAt(
        BattleSector primary,
        TacticalPoint point)
    {
        ArgumentNullException.ThrowIfNull(primary);

        if (Terrain is not null
            && Terrain.TryGetSectorAt(
                point.XMeters,
                point.YMeters,
                out var cellSector))
        {
            return cellSector;
        }

        if (Terrain is not null)
            return primary;

        BattleSector? best = null;
        double bestDistance = double.PositiveInfinity;

        Consider(primary);

        foreach (var connection in _battle.Battlefield.GetConnections(primary))
            Consider(connection.GetOther(primary));

        return best ?? primary;

        void Consider(BattleSector sector)
        {
            var geometry = GetSectorGeometry(sector);

            if (geometry is null || !geometry.Contains(point))
                return;

            double distance = double.Hypot(
                point.XMeters - geometry.CenterXMeters,
                point.YMeters - geometry.CenterYMeters);

            if (distance < bestDistance
                || (distance == bestDistance && sector.Id < best!.Id))
            {
                best = sector;
                bestDistance = distance;
            }
        }
    }

    /// <summary>
    /// Relèvement d'un point vu depuis une position orientée :
    /// 0 = devant, 90 = à droite, 180 = derrière, 270 = à gauche.
    /// Mêmes formules que Measure. Le point doit être distinct de l'origine.
    /// </summary>
    internal static double RelativeBearingDegrees(
        TacticalPoint from,
        double facingDegrees,
        TacticalPoint to)
    {
        double dx = to.XMeters - from.XMeters;
        double dy = to.YMeters - from.YMeters;

        double bearing =
            (Math.Atan2(dx, dy) * 180.0 / Math.PI
             + 360.0) % 360.0;

        return (bearing - facingDegrees + 360.0) % 360.0;
    }

    /// <summary>
    /// Point où s'arrête une compagnie dont le mouvement est annulé :
    /// sa position actuelle si elle est encore dans la géométrie du
    /// secteur, sinon le point le plus proche sur le bord de celle-ci.
    /// </summary>
    internal TacticalPoint GetRetreatPoint(
        BattleSector sector,
        TacticalPoint point)
    {
        ArgumentNullException.ThrowIfNull(sector);

        var geometry = GetSectorGeometry(sector)
            ?? throw new InvalidOperationException(
                "Sector geometry is not defined.");

        double x = point.XMeters;
        double y = point.YMeters;

        double dx = x - geometry.CenterXMeters;
        double dy = y - geometry.CenterYMeters;
        double distance = double.Hypot(dx, dy);

        if (distance > geometry.RadiusMeters)
        {
            double scale = geometry.RadiusMeters / distance;

            x = geometry.CenterXMeters + dx * scale;
            y = geometry.CenterYMeters + dy * scale;

            // L'arrondi peut laisser le point juste hors du cercle :
            // le rapprocher du centre jusqu'à ce qu'il y soit.
            while (double.Hypot(
                       x - geometry.CenterXMeters,
                       y - geometry.CenterYMeters)
                   > geometry.RadiusMeters)
            {
                scale = Math.BitDecrement(scale);
                x = geometry.CenterXMeters + dx * scale;
                y = geometry.CenterYMeters + dy * scale;
            }
        }

        return GetGroundPoint(sector, x, y);
    }

    /// <summary>
    /// Sur une grille, une position doit être exactement au sol :
    /// tout le moteur lit la même altitude.
    /// </summary>
    private void EnsureOnGround(TacticalPoint point)
    {
        if (Terrain is null)
            return;

        double ground = Terrain.GetGroundElevation(
            point.XMeters, point.YMeters);

        if (Math.Abs(point.GroundElevationMeters - ground)
            > GroundToleranceMeters)
        {
            throw new InvalidOperationException(
                "Company elevation does not match the terrain.");
        }
    }

    private const double GroundToleranceMeters = 1e-6;

    private void Replace(
        CompanyTacticalPosition previous,
        TacticalPoint point)
    {
        int index = _positions.FindIndex(p =>
            ReferenceEquals(p, previous));

        if (index < 0)
            throw new InvalidOperationException(
                "Tactical geometry index is inconsistent.");

        var replacement = new CompanyTacticalPosition(
            previous.Deployment,
            point,
            previous.FacingDegrees,
            previous.FrontageMeters);

        _positions[index] = replacement;
        _byDeployment[previous.Deployment] = replacement;

        Revision++;
    }
}
