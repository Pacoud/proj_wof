using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat;

public sealed class Engagement
{
    private readonly List<Division> _participants = new();

    public int Id { get; }

    public long StartHour { get; }

    public EngagementType Type { get; }

    public Province? Province { get; }

    public Province? ConnectionA { get; }

    public Province? ConnectionB { get; }

    public TerrainType BattleTerrain { get; }

    public double? ConnectionProgress { get; }

    public Country? WinnerCountry { get; private set; }

    public IReadOnlyList<Division> Participants =>
        _participants;

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

    internal void AddParticipant(
        Division division)
    {
        if (division.IsBroken)
            return;

        if (_participants.Contains(division))
            return;

        _participants.Add(division);

        division.JoinEngagement(this);
    }

    internal void End(
    Country? winnerCountry)
    {
        if (!IsActive)
            return;

        WinnerCountry = winnerCountry;
        IsActive = false;
    }
}