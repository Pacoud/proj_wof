
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat.History;

public sealed record BattleReport
{
    public int EngagementId { get; }
    public EngagementType Type { get; }
    public string Location { get; }
    public TerrainType Terrain { get; }
    public long StartHour { get; }
    public long EndHour { get; }
    public int? WinnerCountryId { get; }
    public string? WinnerCountryName { get; }
    public IReadOnlyList<ParticipantBattleReport> Participants { get; }

    public BattleReport(
        int EngagementId,
        EngagementType Type,
        string Location,
        TerrainType Terrain,
        long StartHour,
        long EndHour,
        int? WinnerCountryId,
        string? WinnerCountryName,
        IReadOnlyList<ParticipantBattleReport> Participants)
    {
        ArgumentNullException.ThrowIfNull(Participants);

        this.EngagementId = EngagementId;
        this.Type = Type;
        this.Location = Location;
        this.Terrain = Terrain;
        this.StartHour = StartHour;
        this.EndHour = EndHour;
        this.WinnerCountryId = WinnerCountryId;
        this.WinnerCountryName = WinnerCountryName;

        // IReadOnlyList alone does not prevent the caller from changing its list.
        // Copy it, then expose only a read-only wrapper around our own array.
        this.Participants = Array.AsReadOnly(Participants.ToArray());
    }

    public long DurationHours =>
        EndHour - StartHour;

    public int TotalKilled =>
        Participants.Sum(p => p.Killed);

    public int TotalWounded =>
        Participants.Sum(p => p.Wounded);

    public int TotalMissingOrCaptured =>
        Participants.Sum(p => p.MissingOrCaptured);

    public int TotalCasualties =>
        Participants.Sum(p => p.TotalCasualties);

    public int TotalTanksDestroyed =>
        Participants.Sum(p => p.TanksDestroyed);
}
