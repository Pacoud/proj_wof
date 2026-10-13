
namespace WoF.Simulation.Military.Combat.History;

public sealed class BattleHistory
{
    private readonly List<BattleReport> _reports = new();

    private readonly HashSet<int> _archivedIds = new();

    private readonly IReadOnlyList<BattleReport> _reportsView;

    public BattleHistory()
    {
        _reportsView = _reports.AsReadOnly();
    }

    public IReadOnlyList<BattleReport> Reports =>
        _reportsView;

    internal void Add(BattleReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!_archivedIds.Add(report.EngagementId))
        {
            throw new InvalidOperationException(
                "This battle has already been archived.");
        }

        _reports.Add(report);
    }
}
