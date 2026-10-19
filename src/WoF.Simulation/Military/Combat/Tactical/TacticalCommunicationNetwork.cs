
using WoF.Simulation.Military;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

public enum TacticalCommunicationMedium
{
    Radio,
    FieldTelephone,
    Courier
}

public enum TacticalCommunicationNodeKind
{
    Company,
    Battalion,
    Division
}

public sealed class TacticalCommunicationNode
{
    public int Id { get; }
    public TacticalCommunicationNodeKind Kind { get; }

    internal object Formation { get; }

    internal TacticalCommunicationNode(
        int id,
        TacticalCommunicationNodeKind kind,
        object formation)
    {
        Id = id;
        Kind = kind;
        Formation = formation;
    }
}

public sealed class TacticalCommunicationLink
{
    public int Id { get; }
    public TacticalCommunicationNode From { get; }
    public TacticalCommunicationNode To { get; }
    public TacticalCommunicationMedium Medium { get; }
    public int DelayMinutes { get; }
    public bool IsOperational { get; internal set; }

    internal TacticalCommunicationLink(
        int id,
        TacticalCommunicationNode from,
        TacticalCommunicationNode to,
        TacticalCommunicationMedium medium,
        int delayMinutes)
    {
        Id = id;
        From = from;
        To = to;
        Medium = medium;
        DelayMinutes = delayMinutes;
        IsOperational = true;
    }
}

/// <summary>
/// Graphe oriente des liaisons de communication.
/// Le reseau appartient a une unique bataille.
/// </summary>
public sealed partial class TacticalCommunicationNetwork : IDisposable
{
    private readonly TacticalBattle _battle;

    private readonly Dictionary<object, TacticalCommunicationNode>
        _nodes = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<
        TacticalCommunicationNode,
        List<TacticalCommunicationLink>>
        _adjacency = new();

    private int _nextNodeId = 1;
    private int _nextLinkId = 1;

    public long Version { get; private set; }

    internal TacticalBattle Battle => _battle;

    public TacticalCommunicationNetwork(TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    public TacticalCommunicationNode ForCompany(
        CompanyDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        if (!_battle.Deployments.Contains(deployment))
            throw new InvalidOperationException(
                "Company deployment is outside this battle.");

        return GetOrCreate(
            deployment,
            TacticalCommunicationNodeKind.Company);
    }

    public TacticalCommunicationNode ForBattalion(
        Battalion battalion)
    {
        ArgumentNullException.ThrowIfNull(battalion);

        if (battalion.ParentDivision is null
            || !_battle.Engagement.Participants.Contains(
                battalion.ParentDivision)
            || !_battle.Deployments.Any(d =>
                ReferenceEquals(
                    d.Company.ParentBattalion,
                    battalion)))
        {
            throw new InvalidOperationException(
                "Battalion is outside this battle.");
        }

        return GetOrCreate(
            battalion,
            TacticalCommunicationNodeKind.Battalion);
    }

    public TacticalCommunicationNode ForDivision(
        Division division)
    {
        ArgumentNullException.ThrowIfNull(division);

        if (!_battle.Engagement.Participants.Contains(division))
            throw new InvalidOperationException(
                "Division is outside this battle.");

        return GetOrCreate(
            division,
            TacticalCommunicationNodeKind.Division);
    }

    private TacticalCommunicationNode GetOrCreate(
        object formation,
        TacticalCommunicationNodeKind kind)
    {
        if (_nodes.TryGetValue(formation, out var existing))
            return existing;

        var node = new TacticalCommunicationNode(
            _nextNodeId++, kind, formation);

        _nodes.Add(formation, node);
        _adjacency.Add(
            node,
            new List<TacticalCommunicationLink>());

        Version++;
        return node;
    }

    public TacticalCommunicationLink AddDirectedLink(
        TacticalCommunicationNode from,
        TacticalCommunicationNode to,
        TacticalCommunicationMedium medium,
        int delayMinutes)
    {
        ValidateNode(from);
        ValidateNode(to);

        if (ReferenceEquals(from, to))
            throw new InvalidOperationException(
                "Self-links are not allowed.");

        if (!Enum.IsDefined(medium))
            throw new ArgumentOutOfRangeException(nameof(medium));

        if (delayMinutes <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(delayMinutes));

        Division fromDivision = GetDivision(from);
        Division toDivision = GetDivision(to);

        if (fromDivision.Country is null
            || !ReferenceEquals(
                fromDivision.Country,
                toDivision.Country))
        {
            throw new InvalidOperationException(
                "Communication link cannot cross opposing countries.");
        }

        if (_adjacency[from].Any(link =>
            ReferenceEquals(link.To, to)
            && link.Medium == medium))
        {
            throw new InvalidOperationException(
                "Duplicate directed communication link.");
        }

        var created = new TacticalCommunicationLink(
            _nextLinkId++,
            from,
            to,
            medium,
            delayMinutes);

        _adjacency[from].Add(created);
        Version++;

        return created;
    }

    public void SetOperational(
        TacticalCommunicationLink link,
        bool operational)
    {
        ArgumentNullException.ThrowIfNull(link);

        ValidateNode(link.From);

        if (!_adjacency[link.From].Contains(link))
            throw new InvalidOperationException(
                "Link belongs to another network.");

        if (link.IsOperational == operational)
            return;

        link.IsOperational = operational;
        Version++;
    }

    internal IReadOnlyList<TacticalCommunicationLink>
        Outgoing(TacticalCommunicationNode node)
    {
        ValidateNode(node);
        return _adjacency[node];
    }

    internal bool IsAvailable(TacticalCommunicationLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        ValidateNode(link.From);

        return _adjacency[link.From].Contains(link)
            && link.IsOperational
            && IsPhysicallyAvailable(link);
    }

    internal void ValidateNode(TacticalCommunicationNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!_adjacency.ContainsKey(node))
            throw new InvalidOperationException(
                "Communication node belongs to another network.");
    }

    private static Division GetDivision(
        TacticalCommunicationNode node)
    {
        return node.Kind switch
        {
            TacticalCommunicationNodeKind.Company =>
                ((CompanyDeployment)node.Formation)
                    .Company.ParentBattalion!.ParentDivision!,

            TacticalCommunicationNodeKind.Battalion =>
                ((Battalion)node.Formation).ParentDivision!,

            TacticalCommunicationNodeKind.Division =>
                (Division)node.Formation,

            _ => throw new InvalidOperationException(
                "Unsupported communication node.")
        };
    }

    public bool CanUse(TacticalCommunicationLink link)
    {
        return IsAvailable(link);
    }
}
