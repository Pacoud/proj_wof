
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Emprise simplifiee d'une compagnie : un segment
/// perpendiculaire a sa direction de combat.
/// Aucune ressource militaire n'est dupliquee.
/// </summary>
public readonly record struct TacticalCompanyFootprint
{
    public CompanyDeployment Deployment { get; }

    public TacticalPoint LeftEnd { get; }

    public TacticalPoint RightEnd { get; }

    public double FrontageMeters { get; }

    public TacticalCompanyFootprint(
        CompanyTacticalPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        Deployment = position.Deployment;
        FrontageMeters = position.FrontageMeters;

        double radians =
            position.FacingDegrees * Math.PI / 180.0;

        double halfWidth = FrontageMeters / 2.0;

        // Direction laterale par rapport au front.
        // 0 degre = nord, 90 degres = est.
        double dx = Math.Cos(radians) * halfWidth;
        double dy = -Math.Sin(radians) * halfWidth;

        var center = position.Point;

        LeftEnd = new TacticalPoint(
            center.XMeters - dx,
            center.YMeters - dy,
            center.GroundElevationMeters);

        RightEnd = new TacticalPoint(
            center.XMeters + dx,
            center.YMeters + dy,
            center.GroundElevationMeters);
    }

    /// <summary>
    /// Teste si le segment du front rencontre
    /// l'enveloppe circulaire d'un secteur.
    /// </summary>
    public bool Intersects(SectorGeometry sector)
    {
        ArgumentNullException.ThrowIfNull(sector);

        double ax = LeftEnd.XMeters;
        double ay = LeftEnd.YMeters;

        double bx = RightEnd.XMeters;
        double by = RightEnd.YMeters;

        double vx = bx - ax;
        double vy = by - ay;

        double wx = sector.CenterXMeters - ax;
        double wy = sector.CenterYMeters - ay;

        double lengthSquared = vx * vx + vy * vy;

        // Projection du centre du secteur sur
        // le segment de front de la compagnie.
        double t = lengthSquared > 0
            ? Math.Clamp(
                (wx * vx + wy * vy) / lengthSquared,
                0.0,
                1.0)
            : 0.0;

        double closestX = ax + t * vx;
        double closestY = ay + t * vy;

        double dx = sector.CenterXMeters - closestX;
        double dy = sector.CenterYMeters - closestY;

        double distanceSquared = dx * dx + dy * dy;

        return distanceSquared
            <= sector.RadiusMeters * sector.RadiusMeters;
    }
}
