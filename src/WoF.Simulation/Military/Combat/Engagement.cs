using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat;

public sealed class Engagement
{
    private readonly List<Division> _participants = new();

    public int Id { get; }

    public long StartHour { get; }

    public EngagementLocationType LocationType { get; }

    public Province? Province { get; }

    public Province? ConnectionA { get; }

    public Province? ConnectionB { get; }

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

        LocationType =
            EngagementLocationType.Province;

        Province = province;
    }

    public Engagement(
        int id,
        long startHour,
        Province connectionA,
        Province connectionB)
    {
        Id = id;
        StartHour = startHour;

        LocationType =
            EngagementLocationType.Connection;

        ConnectionA = connectionA;
        ConnectionB = connectionB;
    }

    internal void AddParticipant(
        Division division)
    {
        if (_participants.Contains(division))
            return;

        _participants.Add(division);

        division.JoinEngagement(this);
    }
}