
using WoF.Simulation.Military;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

public sealed record TacticalNetworkReport(
    int SourceBattalionId,
    int SourceCompanyId,
    TacticalEnemyContactReport Contact,
    long ReceivedMinute);

/// <summary>
/// Transport evenementiel des renseignements.
/// Une reception par un relais ne signifie pas
/// que toute sa hierarchie connait le rapport.
/// </summary>
public sealed class TacticalNetworkIntelligenceRelay
{
    private enum DeliveryKind
    {
        OriginBattalion,
        OriginDivision,
        SharedBattalion
    }

    private sealed record Packet(
        CompanyDeployment Source,
        Battalion OriginBattalion,
        Division OriginDivision,
        TacticalEnemyContactReport Contact,
        TacticalCommunicationNode Destination,
        DeliveryKind Kind);

    private sealed record InFlight(
        Packet Packet,
        TacticalCommunicationLink Link,
        long DueMinute);

    private readonly TacticalBattle _battle;
    private readonly TacticalPerceptionState _perception;
    private readonly TacticalCommunicationNetwork _network;
    private readonly TacticalMessageRouter _router;

    private readonly PriorityQueue<
        InFlight,
        (long Due, long Sequence)> _pending = new();

    private readonly Dictionary<
        (CompanyDeployment Source, int TrackId),
        long> _lastPublished = new();

    private readonly Dictionary<
        Battalion,
        Dictionary<
            (CompanyDeployment Source, int TrackId),
            TacticalNetworkReport>>
        _battalionReports =
            new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<
        Division,
        Dictionary<
            (CompanyDeployment Source, int TrackId),
            TacticalNetworkReport>>
        _divisionReports =
            new(ReferenceEqualityComparer.Instance);

    private long _sequence;
    private long? _lastStepMinute;

    public int PendingMessageCount => _pending.Count;
    public long DeliveredHopCount { get; private set; }
    public long DroppedHopCount { get; private set; }
    public long RoutingFailureCount { get; private set; }

    public TacticalNetworkIntelligenceRelay(
        TacticalBattle battle,
        TacticalPerceptionState perception,
        TacticalCommunicationNetwork network,
        TacticalMessageRouter router)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(perception);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(router);

        if (!ReferenceEquals(perception.Battle, battle)
            || !ReferenceEquals(network.Battle, battle)
            || !ReferenceEquals(router.Network, network))
        {
            throw new InvalidOperationException(
                "Relay dependencies do not share the same battle.");
        }

        _battle = battle;
        _perception = perception;
        _network = network;
        _router = router;
    }

    /// <summary>
    /// Publication explicite d'une nouvelle observation.
    /// Aucun parcours global des compagnies par tick.
    /// </summary>
    public bool Publish(
        CompanyDeployment source,
        TacticalEnemyContactReport report,
        long currentMinute)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(report);

        ValidateTime(currentMinute);
        _network.ForCompany(source);

        bool belongsToSource = _perception
            .GetReports(source)
            .Any(r => r == report);

        if (!belongsToSource || !report.IsCurrentlyObserved)
        {
            throw new InvalidOperationException(
                "Report is not an active observation of this source.");
        }

        if (report.LastObservedHour < 0
            || report.LastObservedHour > currentMinute / 60)
        {
            throw new InvalidOperationException(
                "Report cannot precede its own observation.");
        }

        var key = (Source: source, TrackId: report.TrackId);

        if (_lastPublished.TryGetValue(key, out long previous)
            && previous >= report.LastObservedHour)
        {
            return false;
        }

        Battalion battalion =
            source.Company.ParentBattalion!;

        Division division = battalion.ParentDivision!;

        var transmitted = report with
        {
            IsCurrentlyObserved = false,
            CurrentObservationLevel = null,
            CurrentObservedSectorId = null
        };

        var packet = new Packet(
            source,
            battalion,
            division,
            transmitted,
            _network.ForBattalion(battalion),
            DeliveryKind.OriginBattalion);

        if (!Dispatch(
            packet,
            _network.ForCompany(source),
            currentMinute))
        {
            return false;
        }

        // Ne retenir la publication que si un premier
        // segment de transmission a ete planifie.
        _lastPublished[key] = report.LastObservedHour;
        return true;
    }

    /// <summary>
    /// Redistribution volontaire d'un rapport recu
    /// par un bataillon vers un autre bataillon ami.
    ///
    /// Aucune diffusion automatique aux voisins.
    /// </summary>
    public bool ShareWithBattalion(
        Battalion sender,
        Battalion recipient,
        CompanyDeployment originalSource,
        int trackId,
        long currentMinute)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(originalSource);

        ValidateTime(currentMinute);

        if (ReferenceEquals(sender, recipient))
            return false;

        var from = _network.ForBattalion(sender);
        var to = _network.ForBattalion(recipient);

        if (!_battalionReports.TryGetValue(
            sender, out var reports)
            || !reports.TryGetValue(
                (originalSource, trackId), out var received))
        {
            return false;
        }

        var packet = new Packet(
            originalSource,
            originalSource.Company.ParentBattalion!,
            originalSource.Company.ParentBattalion!
                .ParentDivision!,
            received.Contact,
            to,
            DeliveryKind.SharedBattalion);

        return Dispatch(packet, from, currentMinute);
    }

    private bool Dispatch(
        Packet packet,
        TacticalCommunicationNode current,
        long minute)
    {
        var route = _router.FindRoute(
            current,
            packet.Destination);

        if (route is null || route.Hops.Count == 0)
        {
            RoutingFailureCount++;
            return false;
        }

        var next = route.Hops[0];

        long due = checked(minute + next.DelayMinutes);

        _pending.Enqueue(
            new InFlight(packet, next, due),
            (due, _sequence++));

        return true;
    }

    /// <summary>
    /// Traite uniquement les arrivées de messages
    /// dont la date est atteinte.
    /// </summary>
    public void Step(long currentMinute)
    {
        ValidateTime(currentMinute);

        while (_pending.TryPeek(
            out var next,
            out _)
            && next.DueMinute <= currentMinute)
        {
            _pending.Dequeue();

            // Une liaison neutralisee avant la reception
            // interrompt ce segment de transmission.
            if (!_network.IsAvailable(next.Link))
            {
                DroppedHopCount++;
                continue;
            }

            DeliveredHopCount++;

            var arrival = next.Link.To;

            if (!ReferenceEquals(
                arrival,
                next.Packet.Destination))
            {
                // Calculer le prochain segment en
                // fonction du reseau ACTUEL.
                Dispatch(
                    next.Packet,
                    arrival,
                    next.DueMinute);

                continue;
            }

            Deliver(next.Packet, next.DueMinute);
        }

        _lastStepMinute = currentMinute;
    }

    private void Deliver(Packet packet, long minute)
    {
        if (packet.Kind == DeliveryKind.OriginDivision)
        {
            StoreDivision(
                packet.OriginDivision,
                packet,
                minute);
            return;
        }

        var battalion =
            (Battalion)packet.Destination.Formation;

        bool accepted = StoreBattalion(
            battalion, packet, minute);

        if (!accepted
            || packet.Kind != DeliveryKind.OriginBattalion)
        {
            return;
        }

        // La division ne peut recevoir ce rapport
        // qu'APRES sa reception par le bataillon.
        var nextPacket = packet with
        {
            Destination = _network.ForDivision(
                packet.OriginDivision),
            Kind = DeliveryKind.OriginDivision
        };

        Dispatch(
            nextPacket,
            _network.ForBattalion(battalion),
            minute);
    }

    private bool StoreBattalion(
        Battalion battalion,
        Packet packet,
        long minute)
    {
        if (!_battalionReports.TryGetValue(
            battalion, out var reports))
        {
            reports = new Dictionary<
                (CompanyDeployment Source, int TrackId),
                TacticalNetworkReport>();

            _battalionReports.Add(battalion, reports);
        }

        var key = (
            Source: packet.Source,
            TrackId: packet.Contact.TrackId);

        if (reports.TryGetValue(key, out var old)
            && old.Contact.LastObservedHour
                >= packet.Contact.LastObservedHour)
        {
            return false;
        }

        reports[key] = CreateReport(packet, minute);
        return true;
    }

    private void StoreDivision(
        Division division,
        Packet packet,
        long minute)
    {
        if (!_divisionReports.TryGetValue(
            division, out var reports))
        {
            reports = new Dictionary<
                (CompanyDeployment Source, int TrackId),
                TacticalNetworkReport>();

            _divisionReports.Add(division, reports);
        }

        var key = (
            Source: packet.Source,
            TrackId: packet.Contact.TrackId);

        if (reports.TryGetValue(key, out var old)
            && old.Contact.LastObservedHour
                >= packet.Contact.LastObservedHour)
        {
            return;
        }

        reports[key] = CreateReport(packet, minute);
    }

    private static TacticalNetworkReport CreateReport(
        Packet packet, long minute)
    {
        return new TacticalNetworkReport(
            packet.OriginBattalion.Id,
            packet.Source.Company.Id,
            packet.Contact,
            minute);
    }

    public IReadOnlyList<TacticalNetworkReport>
        GetBattalionReports(Battalion battalion)
    {
        ArgumentNullException.ThrowIfNull(battalion);
        _network.ForBattalion(battalion);

        if (!_battalionReports.TryGetValue(
            battalion, out var reports))
        {
            return Array.Empty<TacticalNetworkReport>();
        }

        return Array.AsReadOnly(
            reports.Values
                .OrderBy(r => r.SourceBattalionId)
                .ThenBy(r => r.SourceCompanyId)
                .ThenBy(r => r.Contact.TrackId)
                .ToArray());
    }

    public IReadOnlyList<TacticalNetworkReport>
        GetDivisionReports(Division division)
    {
        ArgumentNullException.ThrowIfNull(division);
        _network.ForDivision(division);

        if (!_divisionReports.TryGetValue(
            division, out var reports))
        {
            return Array.Empty<TacticalNetworkReport>();
        }

        return Array.AsReadOnly(
            reports.Values
                .OrderBy(r => r.SourceBattalionId)
                .ThenBy(r => r.SourceCompanyId)
                .ThenBy(r => r.Contact.TrackId)
                .ToArray());
    }

    private void ValidateTime(long minute)
    {
        if (minute < 0
            || minute / 60 < _battle.Engagement.StartHour)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minute));
        }

        if (_lastStepMinute is long previous
            && minute < previous)
        {
            throw new InvalidOperationException(
                "Communication time cannot move backwards.");
        }
    }
}
