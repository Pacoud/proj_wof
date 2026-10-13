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

    public int Id { get; }

    public long StartHour { get; }

    public long? EndHour { get; private set; }

    public EngagementType Type { get; }

    public Province? Province { get; }

    public Province? ConnectionA { get; }

    public Province? ConnectionB { get; }

    public TerrainType BattleTerrain { get; }

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

        BattleTerrain = province.Terrain;
    }

    public Engagement(
        int id,
        long startHour,
        Province connectionA,
        Province connectionB,
        double connectionProgress)
    {
        if (connectionProgress < 0
        || connectionProgress > 1)

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

        BattleTerrain = 
            BattleTerrainResolver.ResolveMeetingTerrain(
                connectionA,
                connectionB,
                connectionProgress
            );
    }

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
