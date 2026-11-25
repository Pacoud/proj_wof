using WoF.Simulation.World;

using WoF.Simulation.Military.Combat.History;

namespace WoF.Simulation.Military.Combat;

public sealed class Engagement
{
    private readonly List<Division> _participants = new();

    private readonly Dictionary<Division, EngagementRole> _roles = new();

    private readonly Dictionary<Division, ParticipantBattleSnapshot>
        _initialSnapshots = new();

    private readonly Dictionary<Division, long>
        _joinHours = new();

    // Provenance de chaque participant, gelée à son entrée dans l'engagement.
    private readonly Dictionary<Division, Province?> _entryProvinces = new();

    public int Id { get; }

    public long StartHour { get; }

    public long? EndHour { get; private set; }

    public EngagementType Type { get; }

    public Province? Province { get; }

    public Province? ConnectionA { get; }

    public Province? ConnectionB { get; }

    /// <summary>Province du champ, fixée lors de la création de cet engagement.</summary>
    public Province BattlefieldProvince { get; }

    /// <summary>Sur une liaison, province voisine désignant le bord du champ.</summary>
    public Province? MeetingBorderNeighbor { get; }

    public TerrainType BattleTerrain => BattlefieldProvince.Terrain;

    public double? ConnectionProgress { get; }

    public Country? WinnerCountry { get; private set; }

    public IReadOnlyList<Division> Participants =>
        _participants;


    public IEnumerable<Division> Attackers =>
    _participants.Where(
        division =>
            GetRole(division)
                == EngagementRole.Attacker
    );

    public IEnumerable<Division> Defenders =>
        _participants.Where(
            division =>
                GetRole(division)
                    == EngagementRole.Defender
        );

    public EngagementRole GetRole(
    Division division)
    {
        return _roles.TryGetValue(
            division,
            out EngagementRole role
        )
            ? role
            : EngagementRole.None;
    }

    public bool IsActive { get; private set; } = true;

    public Engagement(
        int id,
        long startHour,
        Province province)
    {
        Id = id;
        StartHour = startHour;

        Type =
            EngagementType.ProvinceBattle;

        Province = province;

        ArgumentNullException.ThrowIfNull(province);
        BattlefieldProvince = province;   
    }

    public Engagement(
        int id,
        long startHour,
        Province connectionA,
        Province connectionB,
        double connectionProgress)
    {
         if (!double.IsFinite(connectionProgress) || connectionProgress < 0 || connectionProgress > 1)

        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionProgress)
        );

        }
        Id = id;
        StartHour = startHour;

        Type =
            EngagementType.MeetingEngagement;

        ConnectionA = connectionA;
        ConnectionB = connectionB;

        ConnectionProgress = 
            Math.Round(
                connectionProgress,
                6
            );

        // Le choix utilise la mesure d'origine, même quand ConnectionProgress
        // a été arrondie à 0,500000. Les mouvements ultérieurs ne le modifient pas.
        BattlefieldProvince = BattleTerrainResolver.ResolveMeetingProvince(
            connectionA, connectionB, connectionProgress);
        MeetingBorderNeighbor = ReferenceEquals(BattlefieldProvince, connectionA)
            ? connectionB : connectionA;
    }

    /// <summary>
    /// Province d'où venait ce participant quand il a rejoint l'engagement.
    /// La valeur ne change plus ensuite, même après un demi-tour ou un nouveau trajet.
    /// Nulle si la division n'avait pas de provenance ou ne participe pas.
    /// </summary>
    public Province? GetEntryProvince(Division division) =>
        _entryProvinces.TryGetValue(division, out var province) ? province : null;

    internal ParticipantBattleSnapshot GetInitialSnapshot(
        Division division)
    {
        return _initialSnapshots[division];
    }

    internal long GetJoinHour(Division division)
    {
        return _joinHours[division];
    }


    internal void AddParticipant(
        Division division,
        long currentHour,
        EngagementRole role = EngagementRole.None)
    {
        if (!IsActive)
            throw new InvalidOperationException("Cannot join an ended engagement.");

        if (currentHour < StartHour)
            throw new ArgumentOutOfRangeException(nameof(currentHour));

        if (division.IsBroken)
            return;

        if (_participants.Contains(division))
            {
                        // Permet d'attribuer un rôle ultérieurement
                // à un participant encore non classifié.
                if (GetRole(division)
                        == EngagementRole.None
                    &&
                    role != EngagementRole.None)
                {
                    _roles[division] = role;
                }

                return;
            }
        _initialSnapshots[division] =
            ParticipantBattleSnapshot.Capture(division);

        _joinHours[division] = currentHour;

        // Sur une liaison, la division est encore en trajet : sa provenance est
        // l'origine du trajet. Dans une province, c'est la province d'où elle y est entrée.
        _entryProvinces[division] = division.CurrentProvince is null
            ? division.Transit?.Origin
            : division.EnteredFrom;

        _participants.Add(division);

        _roles[division] = role;

        division.JoinEngagement(this);
    }

    internal bool End(
        Country? winnerCountry,
        long currentHour)
    {
        if (!IsActive)
            return false;

        if (currentHour < StartHour
            || _joinHours.Values.Any(hour => hour > currentHour))
        {
            throw new ArgumentOutOfRangeException(nameof(currentHour));
        }

        WinnerCountry = winnerCountry;
        EndHour = currentHour;
        IsActive = false;
        return true;
    }
}
