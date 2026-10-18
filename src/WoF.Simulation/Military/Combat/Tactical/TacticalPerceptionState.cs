
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Rapport de renseignement sans référence publique
/// au véritable déploiement ennemi.
/// </summary>
public sealed record TacticalEnemyContactReport(
    int TrackId,
    int LastKnownSectorId,
    TacticalPoint LastKnownPosition,
    long LastObservedHour,
    bool IsCurrentlyObserved);

/// <summary>
/// Information transmise en interne lorsqu'une
/// observation satisfait les règles de perception.
/// </summary>
internal sealed record TacticalObservationEvent(
    CompanyDeployment Observer,
    CompanyDeployment Target,
    TacticalPoint ObservedPosition,
    int ObservedSectorId);

public sealed class TacticalPerceptionState
{
    private sealed class Track
    {
        public int Id { get; }
        public int SectorId { get; set; }
        public TacticalPoint Position { get; set; }
        public long LastObservedHour { get; set; }
        public bool CurrentlyObserved { get; set; }

        public Track(
            int id,
            int sectorId,
            TacticalPoint position,
            long hour)
        {
            Id = id;
            SectorId = sectorId;
            Position = position;
            LastObservedHour = hour;
            CurrentlyObserved = true;
        }

        public TacticalEnemyContactReport Capture() =>
            new(
                Id,
                SectorId,
                Position,
                LastObservedHour,
                CurrentlyObserved);
    }

    private readonly TacticalBattle _battle;

    internal TacticalBattle Battle => _battle;

    // La référence à la vraie cible reste interne.
    // Chaque observateur possède ses propres pistes.
    private readonly Dictionary<
        CompanyDeployment,
        Dictionary<CompanyDeployment, Track>>
        _tracks = new(ReferenceEqualityComparer.Instance);

    private int _nextTrackId = 1;
    private long? _lastUpdateHour;

    public TacticalPerceptionState(TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    /// <summary>
    /// Renvoie une copie des renseignements détenus
    /// par une compagnie, jamais les objets ennemis.
    /// </summary>
    public IReadOnlyList<TacticalEnemyContactReport> GetReports(
        CompanyDeployment observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        if (!_battle.Deployments.Contains(observer))
            throw new InvalidOperationException(
                "Observer does not belong to this battle.");

        if (!_tracks.TryGetValue(observer, out var tracks))
            return Array.Empty<TacticalEnemyContactReport>();

        return Array.AsReadOnly(
            tracks.Values
                .OrderBy(t => t.Id)
                .Select(t => t.Capture())
                .ToArray());
    }

    internal void EnsureCanUpdate(long hour)
    {
        if (hour < _battle.Engagement.StartHour)
            throw new ArgumentOutOfRangeException(
                nameof(hour),
                "Update precedes the engagement.");

        if (_lastUpdateHour is long last && hour < last)
            throw new InvalidOperationException(
                "Perception time cannot move backwards.");
    }

    /// <summary>
    /// Applique toutes les observations d'une passe.
    /// Les pistes non réobservées deviennent périmées.
    /// </summary>
    internal void Apply(
        long hour,
        IReadOnlyList<TacticalObservationEvent> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        EnsureCanUpdate(hour);

        // Validation complète avant toute mutation.
        var pairs = new HashSet<
            (CompanyDeployment, CompanyDeployment)>();

        foreach (var observation in observations)
        {
            if (!_battle.Deployments.Contains(observation.Observer)
                || !_battle.Deployments.Contains(observation.Target)
                || ReferenceEquals(
                    observation.Observer,
                    observation.Target))
            {
                throw new InvalidOperationException(
                    "Invalid observation participants.");
            }

            if (!pairs.Add((
                observation.Observer,
                observation.Target)))
            {
                throw new InvalidOperationException(
                    "Duplicate observation in one update.");
            }
        }

        // Perdre une observation ne fait pas disparaître
        // automatiquement son dernier emplacement connu.
        foreach (var observerTracks in _tracks.Values)
        {
            foreach (var track in observerTracks.Values)
                track.CurrentlyObserved = false;
        }

        foreach (var observation in observations)
        {
            if (!_tracks.TryGetValue(
                observation.Observer,
                out var observerTracks))
            {
                observerTracks = new Dictionary<
                    CompanyDeployment, Track>(
                        ReferenceEqualityComparer.Instance);

                _tracks.Add(
                    observation.Observer,
                    observerTracks);
            }

            if (!observerTracks.TryGetValue(
                observation.Target,
                out var track))
            {
                track = new Track(
                    _nextTrackId++,
                    observation.ObservedSectorId,
                    observation.ObservedPosition,
                    hour);

                observerTracks.Add(
                    observation.Target,
                    track);
            }
            else
            {
                track.SectorId =
                    observation.ObservedSectorId;

                track.Position =
                    observation.ObservedPosition;

                track.LastObservedHour = hour;
                track.CurrentlyObserved = true;
            }
        }

        _lastUpdateHour = hour;
    }
}
