using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>Raison pour laquelle les zones d'un engagement ne peuvent pas être calculées.</summary>
public enum DeploymentZoneRefusal
{
    None,

    /// <summary>
    /// Il manque un des deux camps : pas d'attaquant ou pas de défenseur dans une province,
    /// pas de division venue du bord de rencontre ou aucune en face sur une liaison.
    /// </summary>
    MissingSide,

    /// <summary>Aucune provenance des entrants ne correspond à un bord de la grille.</summary>
    NoEntryBorder,

    /// <summary>L'étendue retenue n'atteint pas la bande du front de l'autre camp.</summary>
    BattlefieldTooShallow,
}

/// <summary>
/// Zones de déploiement d'un engagement sur la grille de sa province (étape 3c).
///
/// Le camp entrant arrive par un ou plusieurs bords ; l'autre camp lui fait face.
/// Les grands secteurs sont rangés en bandes, mesurées en grands secteurs depuis tous
/// les bords d'entrée à la fois : la bande 0 touche un bord d'entrée, la bande n+1 est
/// voisine de la bande n. Le camp entrant a son arrière en bande 0 et son front en bande 1.
/// Après InitialEmptyBands bandes vides, le front de l'autre camp occupe la bande suivante.
/// Son arrière est le bord du cadre opposé à chaque entrée, au-delà de son front ;
/// à défaut, la bande la plus éloignée du champ, si elle est au-delà de son front.
///
/// Calcul pur : ne modifie ni l'engagement, ni les divisions, ni la grille.
/// </summary>
public sealed class EngagementDeploymentZones
{
    /// <summary>Nombre de bandes vides entre le front entrant et le front qui lui fait face.</summary>
    public const int InitialEmptyBands = 1;

    /// <summary>
    /// Demi-angle, vu du centre du cadre, des cases retenues sur un bord d'entrée
    /// autour du relèvement de la voisine. 30° correspond à l'arc d'une province à six voisines.
    /// </summary>
    public const double EntryHalfAngleDegrees = 30;

    /// <summary>Bande du front du camp qui fait face à l'entrée.</summary>
    public const int FacingFrontBand = 2 + InitialEmptyBands;

    private readonly Dictionary<int, int> _bandBySector;

    public ProvinceBattlefieldGrid Field { get; }

    /// <summary>Provinces des bords d'entrée retenus, dans l'ordre des participants.</summary>
    public IReadOnlyList<Province> EntryProvinces { get; }

    public IReadOnlyList<Division> EnteringDivisions { get; }
    public IReadOnlyList<Division> FacingDivisions { get; }

    /// <summary>Grands secteurs du champ de bataille, triés : toute la province par défaut.</summary>
    public IReadOnlyList<int> BattlefieldSectorIds { get; }

    public IReadOnlyList<int> EnteringRearSectorIds { get; }
    public IReadOnlyList<int> EnteringFrontSectorIds { get; }
    public IReadOnlyList<int> FacingFrontSectorIds { get; }

    /// <summary>Peut être vide, par exemple quand l'autre camp est pris entre deux entrées opposées.</summary>
    public IReadOnlyList<int> FacingRearSectorIds { get; }

    private EngagementDeploymentZones(
        ProvinceBattlefieldGrid field,
        IReadOnlyList<Province> entryProvinces,
        IReadOnlyList<Division> entering,
        IReadOnlyList<Division> facing,
        Dictionary<int, int> bandBySector,
        int[] battlefield,
        int[] enteringRear,
        int[] enteringFront,
        int[] facingFront,
        int[] facingRear)
    {
        Field = field;
        EntryProvinces = entryProvinces;
        EnteringDivisions = entering;
        FacingDivisions = facing;
        _bandBySector = bandBySector;
        BattlefieldSectorIds = Array.AsReadOnly(battlefield);
        EnteringRearSectorIds = Array.AsReadOnly(enteringRear);
        EnteringFrontSectorIds = Array.AsReadOnly(enteringFront);
        FacingFrontSectorIds = Array.AsReadOnly(facingFront);
        FacingRearSectorIds = Array.AsReadOnly(facingRear);
    }

    /// <summary>Bande d'un grand secteur du champ de bataille.</summary>
    public int GetBand(int sectorId) =>
        _bandBySector.TryGetValue(sectorId, out int band)
            ? band
            : throw new ArgumentOutOfRangeException(nameof(sectorId));

    /// <summary>
    /// Calcule les zones. Renvoie faux, avec la raison, si l'engagement ne s'y prête pas.
    /// extentBands limite le champ aux bandes 0 à extentBands ; nul = la province entière.
    /// </summary>
    public static bool TryCompute(
        ProvinceBattlefieldGrid field,
        Engagement engagement,
        out EngagementDeploymentZones? zones,
        out DeploymentZoneRefusal refusal,
        int? extentBands = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(engagement);

        if (!ReferenceEquals(field.Province, engagement.BattlefieldProvince))
            throw new ArgumentException(
                "The grid does not belong to the province of this engagement.", nameof(field));

        if (extentBands < 0)
            throw new ArgumentOutOfRangeException(nameof(extentBands));

        zones = null;

        // 1. Les deux camps.
        Division[] entering;
        Division[] facing;

        if (engagement.Type == EngagementType.MeetingEngagement)
        {
            entering = engagement.Participants
                .Where(d => ReferenceEquals(engagement.GetEntryProvince(d), engagement.MeetingBorderNeighbor))
                .ToArray();
            facing = engagement.Participants.Except(entering).ToArray();
        }
        else
        {
            entering = engagement.Attackers.ToArray();
            facing = engagement.Defenders.ToArray();
        }

        if (entering.Length == 0 || facing.Length == 0)
        {
            refusal = DeploymentZoneRefusal.MissingSide;
            return false;
        }

        // 2. Les bords d'entrée : provenances distinctes, voisines réelles, présentes sur la grille.
        var grid = field.Grid;
        var partition = field.Partition;
        var entries = new List<Province>();
        var entryBearings = new List<double>();
        var startSectors = new SortedSet<int>();

        foreach (var division in entering)
        {
            var province = engagement.GetEntryProvince(division);

            if (province is null
                || entries.Any(e => ReferenceEquals(e, province))
                || !engagement.BattlefieldProvince.Neighbours.Any(n => ReferenceEquals(n, province)))
            {
                continue;
            }

            var border = grid.Descriptor.Neighbors
                .FirstOrDefault(n => n.NeighborProvinceId == province.Id);

            if (border is null)
                continue;

            var sectors = grid.GetBorderCells(province.Id)
                .Where(cell => AngularGap(BearingOf(grid, cell), border.BearingDegrees) <= EntryHalfAngleDegrees)
                .Select(cell => partition.GetSectorOfCell(cell).Id)
                .ToArray();

            if (sectors.Length == 0)
                continue;

            entries.Add(province);
            entryBearings.Add(border.BearingDegrees);
            startSectors.UnionWith(sectors);
        }

        if (startSectors.Count == 0)
        {
            refusal = DeploymentZoneRefusal.NoEntryBorder;
            return false;
        }

        // 3. Bandes : parcours en largeur depuis tous les bords d'entrée à la fois.
        var bands = new Dictionary<int, int>();
        var queue = new Queue<int>();

        foreach (int id in startSectors)
        {
            bands.Add(id, 0);
            queue.Enqueue(id);
        }

        while (queue.Count > 0)
        {
            int id = queue.Dequeue();

            foreach (int next in partition.GetSector(id).NeighborIds)
            {
                if (bands.TryAdd(next, bands[id] + 1))
                    queue.Enqueue(next);
            }
        }

        int[] battlefield = bands
            .Where(pair => extentBands is null || pair.Value <= extentBands)
            .Select(pair => pair.Key)
            .OrderBy(id => id)
            .ToArray();

        int deepest = battlefield.Max(id => bands[id]);

        if (deepest < FacingFrontBand)
        {
            refusal = DeploymentZoneRefusal.BattlefieldTooShallow;
            return false;
        }

        int[] InBand(int band) => battlefield.Where(id => bands[id] == band).ToArray();

        // 4. Arrière de l'autre camp : bord du cadre opposé à chaque entrée, au-delà de son front.
        var included = new HashSet<int>(battlefield);
        var opposite = new SortedSet<int>();

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            if (!grid.IsFrameBoundary(cell))
                continue;

            double bearing = BearingOf(grid, cell);

            if (!entryBearings.Any(b => AngularGap(bearing, b + 180) <= EntryHalfAngleDegrees))
                continue;

            int id = partition.GetSectorOfCell(cell).Id;

            if (included.Contains(id) && bands[id] > FacingFrontBand)
                opposite.Add(id);
        }

        int[] facingRear = opposite.Count > 0
            ? opposite.ToArray()
            : deepest > FacingFrontBand ? InBand(deepest) : Array.Empty<int>();

        zones = new EngagementDeploymentZones(
            field,
            entries.AsReadOnly(),
            Array.AsReadOnly(entering),
            Array.AsReadOnly(facing),
            battlefield.ToDictionary(id => id, id => bands[id]),
            battlefield,
            InBand(0),
            InBand(1),
            InBand(FacingFrontBand),
            facingRear);

        refusal = DeploymentZoneRefusal.None;
        return true;
    }

    private static double BearingOf(ProvinceHexGrid grid, int cell) =>
        ProvinceBorderLayout.BearingFromCenter(grid, cell);

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