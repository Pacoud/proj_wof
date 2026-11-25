
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Réglages du placement des compagnies : largeur de front d'une compagnie,
/// réduite quand son groupe est serré, sans descendre sous le plancher.
/// </summary>
public sealed record CompanyPlacementOptions
{
    public const double DefaultCompanyFrontageMeters = 300;
    public const double DefaultMinCompanyFrontageMeters = 180;

    public static CompanyPlacementOptions Default { get; } = new();

    public double CompanyFrontageMeters { get; init; } = DefaultCompanyFrontageMeters;
    public double MinCompanyFrontageMeters { get; init; } = DefaultMinCompanyFrontageMeters;

    internal void Validate()
    {
        if (!double.IsFinite(CompanyFrontageMeters)
            || !(MinCompanyFrontageMeters > 0)
            || MinCompanyFrontageMeters > CompanyFrontageMeters)
        {
            throw new ArgumentException(
                "Company frontage must be finite, with a positive floor no larger than the frontage.");
        }
    }
}

/// <summary>
/// Position d'une compagnie : case de la grille de la province, point au centre de la case
/// à l'altitude du sol, orientation (0° au nord, sens horaire) et largeur de front.
/// En 3e, layout.GetSector(Cell) donnera le secteur de bataille de la case.
/// </summary>
public sealed record CompanyPlacement(
    Company Company,
    Battalion Battalion,
    CompanyDeploymentRole Role,
    int GrandSectorId,
    int Cell,
    TacticalPoint Point,
    double FacingDegrees,
    double FrontageMeters);

/// <summary>
/// Placement des compagnies d'un plan de déploiement, étape 3d.2.
///
/// Groupes : les compagnies qui partagent les mêmes grands secteurs et la même rangée forment
/// un groupe, même si elles viennent de plusieurs bataillons. Un groupe garde l'ordre du plan :
/// divisions, bataillons, puis compagnies ; chaque bataillon y reste donc contigu.
///
/// Rangées, mesurées vers l'ennemi : la première ligne au bord avant de ses grands secteurs ;
/// le soutien du camp entrant, qui partage le secteur du front, au bord arrière ; le soutien
/// du camp d'en face et les réserves au milieu de leur secteur.
///
/// Largeur : l'étendue latérale des cases du groupe (axe de sa ligne, comme en 3d.1) est
/// découpée en autant de parts égales que de compagnies. Chaque compagnie vise l'axe de sa part,
/// une bande d'une case de large : elle y prend la case libre la mieux placée sur sa rangée.
/// Si l'axe n'a plus de case libre, elle prend la case libre la plus proche de l'axe ; quand
/// toutes les cases du groupe sont prises, elle peut en reprendre une. Front : CompanyFrontageMeters,
/// ramené à la largeur d'une part, sans descendre sous MinCompanyFrontageMeters.
///
/// Orientation : vers le centre de la case la plus proche du front adverse. Sans front adverse,
/// la compagnie regarde perpendiculairement à son bord d'entrée : vers l'intérieur pour le camp
/// entrant, vers le bord pour le camp d'en face.
///
/// Calcul pur : ne tient pas compte du terrain et ne modifie rien.
/// </summary>
public sealed class EngagementCompanyPlacement
{
    private enum Row
    {
        Forward,
        Middle,
        Rear,
    }

    public EngagementDeploymentPlan Plan { get; }
    public CompanyPlacementOptions Options { get; }

    /// <summary>Une position par compagnie, dans l'ordre du plan.</summary>
    public IReadOnlyList<CompanyPlacement> Companies { get; }

    private EngagementCompanyPlacement(
        EngagementDeploymentPlan plan, CompanyPlacementOptions options, CompanyPlacement[] companies)
    {
        Plan = plan;
        Options = options;
        Companies = Array.AsReadOnly(companies);
    }

    public static EngagementCompanyPlacement Compute(
        EngagementDeploymentPlan plan, CompanyPlacementOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        options ??= CompanyPlacementOptions.Default;
        options.Validate();

        var zones = plan.Zones;
        var grid = zones.Field.Grid;
        var partition = zones.Field.Partition;

        (double X, double Y)[] CentersOf(IEnumerable<int> sectorIds) => sectorIds
            .SelectMany(id => partition.GetSector(id).Cells)
            .Select(cell => (grid.GetCenterXMeters(cell), grid.GetCenterYMeters(cell)))
            .ToArray();

        var enteringFront = CentersOf(zones.EnteringFrontSectorIds);
        var facingFront = CentersOf(zones.FacingFrontSectorIds);

        // 1. Groupes, dans l'ordre du plan : divisions, bataillons, puis compagnies.
        var groups = new List<(int[] Sectors, Row Row, List<(BattalionDeploymentAssignment Battalion, CompanyRoleAssignment Company)> Members)>();

        foreach (var assignment in plan.Battalions)
        {
            foreach (var company in assignment.Companies)
            {
                var (sectors, row) = company.Role switch
                {
                    CompanyDeploymentRole.FrontLine => (assignment.FrontSectorIds.ToArray(), Row.Forward),
                    CompanyDeploymentRole.Support => (new[] { assignment.SupportSectorId!.Value },
                        assignment.Side == DeploymentSide.Entering ? Row.Rear : Row.Middle),
                    _ => (new[] { assignment.ReserveSectorId }, Row.Middle),
                };

                int index = groups.FindIndex(g => g.Row == row && g.Sectors.SequenceEqual(sectors));

                if (index < 0)
                {
                    groups.Add((sectors, row, new()));
                    index = groups.Count - 1;
                }

                groups[index].Members.Add((assignment, company));
            }
        }

        // 2. Une case par compagnie, groupe après groupe.
        var taken = new HashSet<int>();
        var placed = new Dictionary<Company, CompanyPlacement>(); // Company ne redéfinit pas Equals.

        foreach (var (sectors, row, members) in groups)
        {
            var first = members[0].Battalion;
            bool entering = first.Side == DeploymentSide.Entering;
            double entryBearing = grid.Descriptor.Neighbors
                .First(n => n.NeighborProvinceId == first.Entry.Id).BearingDegrees;

            // Vers l'ennemi : vers l'intérieur pour le camp entrant, vers le bord pour l'autre.
            double towardEnemy = entering ? (entryBearing + 180) % 360 : entryBearing;
            var opposingFront = entering ? facingFront : enteringFront;

            var cells = sectors.SelectMany(id => partition.GetSector(id).Cells).ToArray();
            var lateral = cells.Select(c => Lateral(grid.GetCenterXMeters(c), grid.GetCenterYMeters(c), entryBearing)).ToArray();
            var forward = cells.Select(c => Forward(grid.GetCenterXMeters(c), grid.GetCenterYMeters(c), towardEnemy)).ToArray();

            // Une case couvre SpacingMeters de large : l'étendue va d'un bord à l'autre des cases extrêmes.
            double lateralMin = lateral.Min() - ProvinceHexGrid.SpacingMeters / 2;
            double slot = (lateral.Max() + ProvinceHexGrid.SpacingMeters / 2 - lateralMin) / members.Count;
            double middle = (forward.Min() + forward.Max()) / 2;
            double frontage = Math.Clamp(slot, options.MinCompanyFrontageMeters, options.CompanyFrontageMeters);

            // Écart à la rangée : plus petit = mieux placé.
            double RowGap(int k) => row switch
            {
                Row.Forward => -forward[k],
                Row.Rear => forward[k],
                _ => Math.Abs(forward[k] - middle),
            };

            for (int i = 0; i < members.Count; i++)
            {
                double slotCenter = lateralMin + (i + 0.5) * slot;

                // Clé de choix, la plus petite l'emporte :
                // 0. case libre sur l'axe de la part (bande d'une case de large) : la mieux placée
                //    sur la rangée, puis la plus proche de l'axe ;
                // 1. sinon, case libre la plus proche de l'axe, puis la mieux placée sur la rangée ;
                // 2. si toutes les cases du groupe sont prises, même ordre parmi elles.
                // À égalité, le plus petit indice de case.
                (int Tier, double First, double Second, int Cell) Key(int k)
                {
                    bool free = !taken.Contains(cells[k]);
                    double gap = Math.Abs(lateral[k] - slotCenter);
                    bool onAxis = gap <= ProvinceHexGrid.SpacingMeters / 2;

                    return free && onAxis
                        ? (0, RowGap(k), gap, cells[k])
                        : (free ? 1 : 2, gap, RowGap(k), cells[k]);
                }

                int cell = cells[Enumerable.Range(0, cells.Length).MinBy(Key)];
                taken.Add(cell);

                double x = grid.GetCenterXMeters(cell);
                double y = grid.GetCenterYMeters(cell);

                if (!grid.TryGetGroundElevation(x, y, out double elevation))
                    throw new InvalidOperationException("Cell center is outside the province grid.");

                var (battalion, company) = members[i];
                placed.Add(company.Company, new CompanyPlacement(
                    company.Company,
                    battalion.Battalion,
                    company.Role,
                    partition.GetSectorOfCell(cell).Id,
                    cell,
                    new TacticalPoint(x, y, elevation),
                    FaceNearest(x, y, opposingFront, towardEnemy),
                    frontage));
            }
        }

        var ordered = plan.Battalions
            .SelectMany(a => a.Companies)
            .Select(c => placed[c.Company])
            .ToArray();

        return new EngagementCompanyPlacement(plan, options, ordered);
    }

    /// <summary>
    /// Relèvement vers le point le plus proche parmi targets (à égalité, le premier).
    /// Sans point, ou si le plus proche est confondu avec (x, y), renvoie fallbackDegrees.
    /// </summary>
    internal static double FaceNearest(
        double x, double y, IReadOnlyList<(double X, double Y)> targets, double fallbackDegrees)
    {
        int best = -1;
        double bestDistance = double.PositiveInfinity;

        for (int i = 0; i < targets.Count; i++)
        {
            double distance = Square(targets[i].X - x) + Square(targets[i].Y - y);

            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        if (best < 0 || bestDistance == 0)
            return fallbackDegrees;

        double bearing = Math.Atan2(targets[best].X - x, targets[best].Y - y) * 180 / Math.PI;
        return (bearing + 360) % 360;
    }

    /// <summary>Même axe latéral qu'en 3d.1 : perpendiculaire à l'entrée, orienté vers le relèvement + 90°.</summary>
    private static double Lateral(double x, double y, double entryBearing)
    {
        double radians = entryBearing * Math.PI / 180;
        return x * Math.Cos(radians) - y * Math.Sin(radians);
    }

    /// <summary>Projection sur la direction du relèvement donné.</summary>
    private static double Forward(double x, double y, double bearing)
    {
        double radians = bearing * Math.PI / 180;
        return x * Math.Sin(radians) + y * Math.Cos(radians);
    }

    private static double Square(double value) => value * value;
}