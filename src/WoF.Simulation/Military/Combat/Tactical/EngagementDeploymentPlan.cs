using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>Camp d'un bataillon, au sens des zones de 3c.</summary>
public enum DeploymentSide
{
    Entering,
    Facing,
}

/// <summary>Échelon d'un bataillon : sur le front, ou en seconde ligne dans la zone arrière.</summary>
public enum BattalionEchelon
{
    FirstLine,
    SecondLine,
}

/// <summary>
/// Réglages du plan de déploiement. Les trois parts des rôles font 1 ; la première ligne
/// est arrondie au-dessus, la réserve au-dessous, et le soutien reçoit le reste.
/// </summary>
public sealed record DeploymentPlanOptions
{
    public const double DefaultFrontLineShare = 0.5;
    public const double DefaultSupportShare = 0.25;
    public const double DefaultReserveShare = 0.25;

    /// <summary>Largeur du front d'un bataillon de première ligne, en grands secteurs.</summary>
    public const int DefaultBattalionFrontSectors = 1;

    public static DeploymentPlanOptions Default { get; } = new();

    public double FrontLineShare { get; init; } = DefaultFrontLineShare;
    public double SupportShare { get; init; } = DefaultSupportShare;
    public double ReserveShare { get; init; } = DefaultReserveShare;
    public int BattalionFrontSectors { get; init; } = DefaultBattalionFrontSectors;

    internal void Validate()
    {
        if (FrontLineShare <= 0 || FrontLineShare > 1
            || SupportShare < 0 || SupportShare > 1
            || ReserveShare < 0 || ReserveShare > 1
            || Math.Abs(FrontLineShare + SupportShare + ReserveShare - 1) > 1e-9)
        {
            throw new ArgumentException(
                "Role shares must be between 0 and 1 and add up to 1, with a positive front line share.");
        }

        if (BattalionFrontSectors < 1)
            throw new ArgumentException("A battalion front needs at least one grand sector.");
    }
}

/// <summary>Rôle d'une compagnie dans le plan.</summary>
public sealed record CompanyRoleAssignment(Company Company, CompanyDeploymentRole Role);

/// <summary>
/// Échelon, grands secteurs et rôles des compagnies d'un bataillon.
/// La première ligne va dans FrontSectorIds, le soutien dans SupportSectorId,
/// la réserve dans ReserveSectorId. Le placement sur les cases relève de 3d.2.
/// En seconde ligne : FrontSectorIds vide, SupportSectorId nul, toutes les compagnies en réserve.
/// Entry est la province du bord d'entrée dont dépend la ligne du bataillon.
/// </summary>
public sealed record BattalionDeploymentAssignment(
    Battalion Battalion,
    DeploymentSide Side,
    Province Entry,
    BattalionEchelon Echelon,
    IReadOnlyList<int> FrontSectorIds,
    int? SupportSectorId,
    int ReserveSectorId,
    IReadOnlyList<CompanyRoleAssignment> Companies);

/// <summary>
/// Plan de déploiement d'un engagement, étape 3d.1 : rôle de chaque compagnie,
/// échelon et grands secteurs de chaque bataillon, à partir des zones de 3c.
///
/// Lignes : chaque zone est découpée en une ligne par bord d'entrée. Un grand secteur va à
/// l'entrée dont le relèvement, vu du centre du cadre, est le plus proche du sien (le relèvement
/// opposé pour l'arrière du camp d'en face). Une ligne est triée selon l'axe perpendiculaire
/// à son entrée, puis par identifiant.
///
/// Les divisions entrantes vont sur la ligne de leur provenance ; les bataillons d'en face sont
/// distribués tour à tour entre les lignes. Sur chaque ligne, les bataillons qui ont des compagnies
/// de combat prennent chacun BattalionFrontSectors grands secteurs du front, en un bloc centré.
/// Les bataillons en trop et ceux sans compagnie de combat vont en seconde ligne, dans la zone arrière.
///
/// Profondeur : le camp entrant n'a que deux bandes. Sa première ligne et son soutien partagent
/// le secteur du front (le soutien derrière, en 3d.2) ; sa réserve est en bande 0. Le camp d'en
/// face a son soutien dans la bande juste derrière son front et sa réserve dans sa zone arrière.
///
/// Calcul pur : ne modifie ni l'engagement, ni les divisions, ni la grille.
/// </summary>
public sealed class EngagementDeploymentPlan
{
    private const double RoundingTolerance = 1e-9;

    public EngagementDeploymentZones Zones { get; }
    public DeploymentPlanOptions Options { get; }

    /// <summary>Camp entrant puis camp d'en face ; dans chacun, ordre des divisions puis des bataillons.</summary>
    public IReadOnlyList<BattalionDeploymentAssignment> Battalions { get; }

    /// <summary>Front entrant : une ligne ordonnée par bord d'entrée, aux indices de Zones.EntryProvinces.</summary>
    public IReadOnlyList<IReadOnlyList<int>> EnteringFrontLines { get; }

    /// <summary>Front d'en face : une ligne ordonnée par bord d'entrée.</summary>
    public IReadOnlyList<IReadOnlyList<int>> FacingFrontLines { get; }

    private EngagementDeploymentPlan(
        EngagementDeploymentZones zones,
        DeploymentPlanOptions options,
        List<BattalionDeploymentAssignment> battalions,
        Line[] enteringFront,
        Line[] facingFront)
    {
        Zones = zones;
        Options = options;
        Battalions = battalions.AsReadOnly();
        EnteringFrontLines = Array.AsReadOnly(
            enteringFront.Select(l => (IReadOnlyList<int>)Array.AsReadOnly(l.Ids)).ToArray());
        FacingFrontLines = Array.AsReadOnly(
            facingFront.Select(l => (IReadOnlyList<int>)Array.AsReadOnly(l.Ids)).ToArray());
    }

    /// <summary>
    /// Répartition des compagnies de combat d'un bataillon : première ligne arrondie au-dessus,
    /// réserve arrondie au-dessous, le reste en soutien. 1 → 1/0/0, 2 → 1/1/0, 3 → 2/1/0, 4 → 2/1/1.
    /// </summary>
    public static (int FrontLine, int Support, int Reserve) SplitRoles(
        int combatCompanies, DeploymentPlanOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(combatCompanies);
        options ??= DeploymentPlanOptions.Default;
        options.Validate();

        // La tolérance évite qu'un produit comme 10 × 0,3 = 3,0000000000000004 soit arrondi à 4.
        int front = (int)Math.Ceiling(combatCompanies * options.FrontLineShare - RoundingTolerance);
        int reserve = (int)Math.Floor(combatCompanies * options.ReserveShare + RoundingTolerance);

        // Les parts font 1 : la première ligne et la réserve ne dépassent jamais le total.
        return (front, combatCompanies - front - reserve, reserve);
    }

    /// <summary>Compagnies qui tiennent un front ; les autres restent en réserve avec leur bataillon.</summary>
    public static bool IsCombatCompany(CompanyType type) => type is
        CompanyType.Infantry or
        CompanyType.MotorizedInfantry or
        CompanyType.HeavyWeapons or
        CompanyType.Armor or
        CompanyType.Engineer;

    /// <summary>
    /// Calcule le plan. Les zones doivent avoir été calculées pour cet engagement ;
    /// elles donnent le bord d'entrée de chaque division entrante.

    /// </summary>
    public static EngagementDeploymentPlan Compute(
        EngagementDeploymentZones zones, Engagement engagement, DeploymentPlanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(engagement);

        if (!ReferenceEquals(zones.Field.Province, engagement.BattlefieldProvince)
            || !zones.EnteringDivisions.Concat(zones.FacingDivisions).All(d => engagement.Participants.Contains(d)))
        {
            throw new ArgumentException("The zones were not computed for this engagement.", nameof(zones));
        }

        options ??= DeploymentPlanOptions.Default;
        options.Validate();

        var builder = new Builder(zones, options);
        var enteringFront = builder.Lines(zones.EnteringFrontSectorIds, oppositeSide: false);
        var facingFront = builder.Lines(zones.FacingFrontSectorIds, oppositeSide: false);

        // Soutien et arrière : une entrée sans grand secteur à elle prend la zone entière,
        // pour que la réserve reste dans la zone arrière de son camp.
        var enteringRear = builder.Lines(
            zones.EnteringRearSectorIds, oppositeSide: false, wholeZoneIfEmpty: true);
        var facingSupport = builder.Lines(
            zones.BattlefieldSectorIds
                .Where(id => zones.GetBand(id) == EngagementDeploymentZones.FacingFrontBand + 1)
                .ToArray(),
            oppositeSide: false,
            wholeZoneIfEmpty: true);
        var facingRear = builder.Lines(
            zones.FacingRearSectorIds, oppositeSide: true, wholeZoneIfEmpty: true);

        var enteringByLine = enteringFront.Select(_ => new List<Battalion>()).ToArray();
        var facingByLine = facingFront.Select(_ => new List<Battalion>()).ToArray();
        
        // Camp entrant : chaque division sur la ligne de son bord d'entrée, provenance ou voisine
        // de repli (3d.3). Une division sans bord retenu donne -1 : première ligne dont le front existe.
        foreach (var division in zones.EnteringDivisions)
        {
            var provenance = zones.GetEntryProvince(division);   
                int wanted = Array.FindIndex(
                zones.EntryProvinces.ToArray(), province => ReferenceEquals(province, provenance));

            enteringByLine[UsableLine(enteringFront, wanted)].AddRange(division.Battalions);
        }

        // Camp d'en face : bataillons distribués tour à tour entre les lignes dont le front existe.
        int[] facingLines = Enumerable.Range(0, facingFront.Length)
            .Where(i => facingFront[i].Count > 0)
            .ToArray();
        int dealt = 0;

        foreach (var battalion in zones.FacingDivisions.SelectMany(d => d.Battalions))
            facingByLine[facingLines[dealt++ % facingLines.Length]].Add(battalion);

        var result = new List<BattalionDeploymentAssignment>();

        for (int line = 0; line < enteringByLine.Length; line++)
        {
            builder.PlaceLine(result, DeploymentSide.Entering, line, enteringByLine[line],
                enteringFront[line], supportLine: null, enteringRear[line]);
        }

        for (int line = 0; line < facingByLine.Length; line++)
        {
            builder.PlaceLine(result, DeploymentSide.Facing, line, facingByLine[line],
                facingFront[line], facingSupport[line], facingRear[line]);
        }

        // Remet les bataillons dans l'ordre des camps, des divisions puis des bataillons.
        // Battalion ne redéfinit pas Equals : IndexOf compare les références.
        var order = zones.EnteringDivisions.Concat(zones.FacingDivisions)
            .SelectMany(d => d.Battalions)
            .ToList();

        result.Sort((a, b) => order.IndexOf(a.Battalion).CompareTo(order.IndexOf(b.Battalion)));

        return new EngagementDeploymentPlan(zones, options, result, enteringFront, facingFront);
    }

    /// <summary>La ligne demandée si son front existe, sinon la première ligne dont le front existe.</summary>
    private static int UsableLine(Line[] front, int wanted) =>
        wanted >= 0 && front[wanted].Count > 0
            ? wanted
            : Array.FindIndex(front, line => line.Count > 0);

    /// <summary>Une ligne : grands secteurs triés et leur coordonnée latérale.</summary>
    private sealed class Line
    {
        public int[] Ids { get; init; } = Array.Empty<int>();
        public double[] Lateral { get; init; } = Array.Empty<double>();
        public int Count => Ids.Length;

        /// <summary>Le grand secteur dont la coordonnée latérale est la plus proche ; -1 si la ligne est vide.</summary>
        public int Nearest(double lateral)
        {
            int best = -1;

            for (int i = 0; i < Ids.Length; i++)
            {
                if (best < 0 || Math.Abs(Lateral[i] - lateral) < Math.Abs(Lateral[best] - lateral))
                    best = i;
            }

            return best < 0 ? -1 : Ids[best];
        }
    }

    private sealed class Builder
    {
        private readonly EngagementDeploymentZones _zones;
        private readonly DeploymentPlanOptions _options;
        private readonly double[] _entryBearings;

        public Builder(EngagementDeploymentZones zones, DeploymentPlanOptions options)
        {
            _zones = zones;
            _options = options;

            // 3c ne retient que des provenances qui ont un bord dans le descripteur.
            _entryBearings = zones.EntryProvinces
                .Select(p => zones.Field.Grid.Descriptor.Neighbors
                    .First(n => n.NeighborProvinceId == p.Id).BearingDegrees)
                .ToArray();
        }

        /// <summary>
        /// Découpe une zone en une ligne ordonnée par bord d'entrée.
        /// wholeZoneIfEmpty : une entrée sans grand secteur reçoit toute la zone, ordonnée selon son axe.
        /// </summary>
        public Line[] Lines(IReadOnlyList<int> sectorIds, bool oppositeSide, bool wholeZoneIfEmpty = false)
        {
            double turn = oppositeSide ? 180 : 0;
            var groups = _entryBearings.Select(_ => new List<(int Id, double Lateral)>()).ToArray();

            foreach (int id in sectorIds)
            {
                var (x, y) = Offset(id);
                double bearing = Math.Atan2(x, y) * 180 / Math.PI;
                int closest = 0;

                // En cas d'égalité, la première entrée l'emporte.
                for (int i = 1; i < _entryBearings.Length; i++)
                {
                    if (AngularGap(bearing, _entryBearings[i] + turn)
                        < AngularGap(bearing, _entryBearings[closest] + turn))
                    {
                        closest = i;
                    }
                }

                groups[closest].Add((id, LateralOf(x, y, _entryBearings[closest])));
            }

            for (int i = 0; wholeZoneIfEmpty && i < groups.Length; i++)
            {
                if (groups[i].Count > 0)
                    continue;

                foreach (int id in sectorIds)
                {
                    var (x, y) = Offset(id);
                    groups[i].Add((id, LateralOf(x, y, _entryBearings[i])));
                }
            }

            return groups
                .Select(g => g.OrderBy(s => s.Lateral).ThenBy(s => s.Id).ToArray())
                .Select(g => new Line
                {
                    Ids = g.Select(s => s.Id).ToArray(),
                    Lateral = g.Select(s => s.Lateral).ToArray(),
                })
                .ToArray();
        }

        /// <summary>Place les bataillons d'une ligne : bloc centré au front, le reste en seconde ligne.</summary>
        public void PlaceLine(
            List<BattalionDeploymentAssignment> result,
            DeploymentSide side,
            int lineIndex,
            List<Battalion> battalions,
            Line front,
            Line? supportLine,
            Line rear)
        {
            if (battalions.Count == 0)
                return;

            var entry = _zones.EntryProvinces[lineIndex];
            int width = Math.Min(_options.BattalionFrontSectors, front.Count);
            int capacity = Math.Max(1, front.Count / _options.BattalionFrontSectors);

            var firstLine = battalions
                .Where(b => b.Companies.Any(c => IsCombatCompany(c.Type)))
                .Take(capacity)
                .ToList();

            // Bloc centré : autant de grands secteurs libres de chaque côté, à un près.
            int start = (front.Count - firstLine.Count * width) / 2;

            for (int i = 0; i < firstLine.Count; i++)
            {
                int first = start + i * width;
                int[] frontIds = front.Ids[first..(first + width)];
                double lateral = front.Lateral[first..(first + width)].Average();
                int middle = frontIds[width / 2];

                // Soutien et réserve juste derrière le bataillon, à la même coordonnée latérale.
                int support = supportLine is null ? middle : Or(supportLine.Nearest(lateral), middle);
                int reserve = Or(rear.Nearest(lateral), support);

                result.Add(new BattalionDeploymentAssignment(
                    firstLine[i], side, entry, BattalionEchelon.FirstLine,
                    Array.AsReadOnly(frontIds), support, reserve, Roles(firstLine[i])));
            }

            // Seconde ligne : bataillons en trop et bataillons sans compagnie de combat,
            // côte à côte au centre de la zone arrière (à défaut, du soutien ou du front).
            var secondLine = battalions.Where(b => !firstLine.Contains(b)).ToList();
            var place = rear.Count > 0 ? rear : supportLine is { Count: > 0 } ? supportLine : front;
            int secondStart = Math.Max(0, (place.Count - secondLine.Count) / 2);

            for (int j = 0; j < secondLine.Count; j++)
            {
                var reserves = secondLine[j].Companies
                    .Select(c => new CompanyRoleAssignment(c, CompanyDeploymentRole.Reserve))
                    .ToArray();

                result.Add(new BattalionDeploymentAssignment(
                    secondLine[j], side, entry, BattalionEchelon.SecondLine,
                    Array.Empty<int>(), null, place.Ids[(secondStart + j) % place.Count],
                    Array.AsReadOnly(reserves)));
            }
        }

        /// <summary>
        /// Rôles d'un bataillon de première ligne, dans l'ordre de ses compagnies : les premières
        /// compagnies de combat en première ligne, les suivantes en soutien, le reste en réserve.
        /// </summary>
        private IReadOnlyList<CompanyRoleAssignment> Roles(Battalion battalion)
        {
            var combat = battalion.Companies.Where(c => IsCombatCompany(c.Type)).ToList();
            var (front, support, _) = SplitRoles(combat.Count, _options);

            var roles = battalion.Companies
                .Select(company =>
                {
                    int rank = combat.IndexOf(company);
                    var role = rank < 0 || rank >= front + support ? CompanyDeploymentRole.Reserve
                        : rank < front ? CompanyDeploymentRole.FrontLine
                        : CompanyDeploymentRole.Support;
                    return new CompanyRoleAssignment(company, role);
                })
                .ToArray();

            return Array.AsReadOnly(roles);
        }

        /// <summary>Position moyenne d'un grand secteur par rapport au centre du cadre (x vers l'est, y vers le nord).</summary>
        private (double X, double Y) Offset(int sectorId)
        {
            var grid = _zones.Field.Grid;
            var cells = _zones.Field.Partition.GetSector(sectorId).Cells;
            return (cells.Average(c => grid.GetCenterXMeters(c)) - grid.FrameCenterXMeters,
                    cells.Average(c => grid.GetCenterYMeters(c)) - grid.FrameCenterYMeters);
        }

        private static int Or(int sector, int fallback) => sector >= 0 ? sector : fallback;
    }

    /// <summary>Coordonnée le long de l'axe perpendiculaire à l'entrée, orienté vers le relèvement + 90°.</summary>
    private static double LateralOf(double x, double y, double entryBearing)
    {
        double radians = entryBearing * Math.PI / 180;
        return x * Math.Cos(radians) - y * Math.Sin(radians);
    }

    /// <summary>Écart absolu entre deux relèvements, de 0 à 180°.</summary>
    private static double AngularGap(double first, double second)
    {
        double gap = (first - second) % 360;

        if (gap < -180)
            gap += 360;
        else if (gap >= 180)
            gap -= 360;

        return Math.Abs(gap);
    }
}
