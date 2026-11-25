using WoF.Simulation.Core;
using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>Raison pour laquelle les zones d'un engagement ne peuvent pas être calculées.</summary>
public enum DeploymentZoneRefusal
{
    None,

    /// <summary>
    /// Il manque un des deux camps, par exemple une rencontre sur une liaison
    /// sans division venue du bord de rencontre.
    /// </summary>
    MissingSide,

    /// <summary>Aucune division entrante n'a de bord d'entrée sur la grille.</summary>
    NoEntryBorder,

    /// <summary>L'étendue retenue n'atteint pas la bande du front de l'autre camp.</summary>
    BattlefieldTooShallow,

    /// <summary>
    /// Un participant n'appartient à aucun camp : il est en guerre avec les deux camps,
    /// ou avec aucun. Provisoire : à revoir si des alliances apparaissent.
    /// </summary>
    ParticipantWithoutSide,
}

/// <summary>
/// Zones de déploiement d'un engagement sur la grille de sa province (étapes 3c et 3d.3).
///
/// Camps (3d.3, convention de déploiement provisoire, qui ne fixe aucune mission) :
/// - sur une liaison, les divisions venues de MeetingBorderNeighbor entrent, les autres font face ;
/// - dans une province avec attaquants et défenseurs, les attaquants entrent, les défenseurs font face ;
/// - dans une province sans rôles, le camp du pays qui contrôle la province fait face, s'il participe.
///   Sinon, un tirage déterministe désigne ce pays parmi ceux des participants. Les divisions
///   en guerre avec lui entrent ;
/// - un participant encore sans camp rejoint le camp avec lequel il n'est pas en guerre ;
///   en guerre avec les deux, ou avec aucun, le calcul est refusé (ParticipantWithoutSide).
///
/// Bords d'entrée : la provenance gelée par l'engagement. Une division sans provenance utilisable,
/// par exemple placée là au début du scénario, entre par le bord de la première voisine contrôlée
/// par son pays, dans l'ordre des identifiants.
///
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

    // Domaine du tirage du camp qui fait face, quand aucun participant ne contrôle la province.
    private const long FacingDrawDomain = 0x46414345; // « FACE »

    private readonly Dictionary<int, int> _bandBySector;
    private readonly Dictionary<Division, Province> _entryByDivision;

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
        Dictionary<Division, Province> entryByDivision,
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
        _entryByDivision = entryByDivision;
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
    /// Province du bord par lequel entre une division du camp entrant : sa provenance,
    /// ou la voisine de repli contrôlée par son pays. Nulle si elle n'a aucun bord retenu.
    /// </summary>
    public Province? GetEntryProvince(Division division) =>
        _entryByDivision.TryGetValue(division, out var province) ? province : null;

    /// <summary>
    /// Calcule les zones. Renvoie faux, avec la raison, si l'engagement ne s'y prête pas.
    /// extentBands limite le champ aux bandes 0 à extentBands ; nul = la province entière.
    /// diplomacy dit qui est en guerre avec qui, pour les participants sans rôle ; sans elle,
    /// deux pays différents sont tenus pour ennemis, ce qui est toujours vrai à deux pays.
    /// </summary>
    public static bool TryCompute(
        ProvinceBattlefieldGrid field,
        Engagement engagement,
        out EngagementDeploymentZones? zones,
        out DeploymentZoneRefusal refusal,
        int? extentBands = null,
        DiplomacySystem? diplomacy = null)
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
        if (!TryResolveSides(field, engagement, diplomacy, out var entering, out var facing, out refusal))
            return false;

        // 2. Les bords d'entrée : provenance de chaque division entrante, sinon voisine de repli.
        var grid = field.Grid;
        var partition = field.Partition;
        var entries = new List<Province>();
        var entryBearings = new List<double>();
        var entryByDivision = new Dictionary<Division, Province>();
        var startSectors = new SortedSet<int>();

        // Grands secteurs du bord d'une voisine, à 30° au plus de son relèvement ; vide si inutilisable.
        int[] BorderSectors(Province? province, out double bearing)
        {
            bearing = 0;

            if (province is null
                || !engagement.BattlefieldProvince.Neighbours.Any(n => ReferenceEquals(n, province)))
            {
                return Array.Empty<int>();
            }

            var border = grid.Descriptor.Neighbors.FirstOrDefault(n => n.NeighborProvinceId == province.Id);

            if (border is null)
                return Array.Empty<int>();

            double borderBearing = border.BearingDegrees;
            bearing = borderBearing;

            return grid.GetBorderCells(province.Id)
                .Where(cell => AngularGap(BearingOf(grid, cell), borderBearing) <= EntryHalfAngleDegrees)
                .Select(cell => partition.GetSectorOfCell(cell).Id)
                .ToArray();
        }

        foreach (var division in entering)
        {
            var province = engagement.GetEntryProvince(division);
            int[] sectors = BorderSectors(province, out double bearing);

            if (sectors.Length == 0)
            {
                // Sans provenance utilisable : première voisine contrôlée par son pays.
                province = engagement.BattlefieldProvince.Neighbours
                    .Where(n => division.Country is not null && n.Controller?.Id == division.Country.Id)
                    .OrderBy(n => n.Id)
                    .FirstOrDefault(n => BorderSectors(n, out _).Length > 0);
                sectors = BorderSectors(province, out bearing);
            }

            if (province is null || sectors.Length == 0)
                continue;

            entryByDivision.Add(division, province);

            if (entries.Any(e => ReferenceEquals(e, province)))
                continue;

            entries.Add(province);
            entryBearings.Add(bearing);
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
            entryByDivision,
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

    /// <summary>
    /// Répartit les participants entre le camp qui entre et celui qui fait face, dans l'ordre
    /// des participants. Voir la convention de 3d.3 en tête de classe.
    /// </summary>
    private static bool TryResolveSides(
        ProvinceBattlefieldGrid field,
        Engagement engagement,
        DiplomacySystem? diplomacy,
        out Division[] entering,
        out Division[] facing,
        out DeploymentZoneRefusal refusal)
    {
        var participants = engagement.Participants;
        var side = new Dictionary<Division, bool>(); // vrai : entre ; faux : fait face.

        bool AtWar(Division first, Division second) =>
            first.Country is not null
            && second.Country is not null
            && (diplomacy?.AreAtWar(first.Country, second.Country)
                ?? first.Country.Id != second.Country.Id);

        if (engagement.Type == EngagementType.MeetingEngagement)
        {
            foreach (var division in participants)
                side[division] = ReferenceEquals(engagement.GetEntryProvince(division), engagement.MeetingBorderNeighbor);
        }
        else if (engagement.Attackers.Any() && engagement.Defenders.Any())
        {
            foreach (var division in engagement.Attackers)
                side[division] = true;

            foreach (var division in engagement.Defenders)
                side[division] = false;
        }
        else
        {
            // Sans rôles : le pays qui contrôle la province fait face, s'il participe ;
            // sinon un tirage tiré de la province et de l'heure de début, pas de l'ordre de création.
            var countries = participants
                .Where(d => d.Country is not null)
                .Select(d => d.Country!)
                .DistinctBy(c => c.Id)
                .OrderBy(c => c.Id)
                .ToArray();

            int? controllerId = engagement.BattlefieldProvince.Controller?.Id;
            var holder = countries.FirstOrDefault(c => c.Id == controllerId);

            if (holder is null && countries.Length > 0)
            {
                ulong draw = DeterministicRandom.Hash(
                    field.Grid.Descriptor.ProvinceSeed, FacingDrawDomain, engagement.StartHour);
                holder = countries[(int)(draw % (ulong)countries.Length)];
            }

            var holding = holder is null
                ? Array.Empty<Division>()
                : participants.Where(d => d.Country?.Id == holder.Id).ToArray();

            foreach (var division in participants)
            {
                if (holding.Contains(division))
                    side[division] = false;
                else if (holding.Any(h => AtWar(division, h)))
                    side[division] = true;
            }
        }

        // Participants encore sans camp : le camp avec lequel ils ne sont pas en guerre.
        var assigned = side.ToArray();

        foreach (var division in participants.Where(d => !side.ContainsKey(d)))
        {
            bool againstEntering = assigned.Any(p => p.Value && AtWar(division, p.Key));
            bool againstFacing = assigned.Any(p => !p.Value && AtWar(division, p.Key));

            if (againstEntering == againstFacing)
            {
                entering = facing = Array.Empty<Division>();
                refusal = DeploymentZoneRefusal.ParticipantWithoutSide;
                return false;
            }

            side[division] = againstFacing;
        }

        entering = participants.Where(d => side[d]).ToArray();
        facing = participants.Where(d => !side[d]).ToArray();

        if (entering.Length == 0 || facing.Length == 0)
        {
            refusal = DeploymentZoneRefusal.MissingSide;
            return false;
        }

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