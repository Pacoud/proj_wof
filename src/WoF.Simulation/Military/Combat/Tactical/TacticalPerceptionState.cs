
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Vue renseignement, sans référence publique à
/// l'objet réel de la compagnie ennemie.
///
/// LastKnownPosition est null si aucune position
/// n'a encore été localisée.
/// </summary>
public sealed record TacticalEnemyContactReport(
    int TrackId,
    int LastKnownSectorId,
    TacticalPoint? LastKnownPosition,
    long LastObservedHour,
    bool IsCurrentlyObserved)
{
    public TacticalKnowledgeLevel BestKnownLevel { get; init; }

    public TacticalKnowledgeLevel? CurrentObservationLevel
    {
        get;
        init;
    }

    // Le secteur rapporté lors de l'observation en cours.
    // Il peut différer de celui de la dernière localisation.
    public int? CurrentObservedSectorId { get; init; }

    public double? PositionUncertaintyMeters { get; init; }

    // Date de la dernière localisation géographique,
    // distincte de la dernière activité observée.
    public long? LastLocatedHour { get; init; }

    // Catégorie estimée, jamais copiée automatiquement
    // depuis la véritable compagnie ennemie.
    public CompanyType? EstimatedCompanyType { get; init; }
}

/// <summary>
/// Événement interne représentant une information
/// réellement fournie par un capteur ou observateur.
///
/// Les paramètres ajoutés sont optionnels pour
/// conserver les appels de C2.2c.1.
/// </summary>
internal sealed record TacticalObservationEvent(
    CompanyDeployment Observer,
    CompanyDeployment Target,
    TacticalPoint? ObservedPosition,
    int ObservedSectorId,
    TacticalKnowledgeLevel KnowledgeLevel =
        TacticalKnowledgeLevel.Located,
    double? PositionUncertaintyMeters = 0,
    CompanyType? EstimatedCompanyType = null);

public sealed class TacticalPerceptionState
{
    private sealed class Track
    {
        public int Id { get; }

        public int SectorId { get; private set; }

        public TacticalPoint? Position { get; private set; }

        public double? PositionUncertaintyMeters
        {
            get;
            private set;
        }

        public long? LastLocatedHour { get; private set; }

        public long LastObservedHour { get; private set; }

        public bool CurrentlyObserved { get; private set; }

        public TacticalKnowledgeLevel BestKnownLevel
        {
            get;
            private set;
        }

        public TacticalKnowledgeLevel? CurrentObservationLevel
        {
            get;
            private set;
        }

        public int? CurrentObservedSectorId
        {
            get;
            private set;
        }

        public CompanyType? EstimatedCompanyType
        {
            get;
            private set;
        }

        public Track(
            int id,
            TacticalObservationEvent observation,
            long hour)
        {
            Id = id;
            SectorId = observation.ObservedSectorId;
            BestKnownLevel = TacticalKnowledgeLevel.Suspected;

            Update(observation, hour);
        }

        public void MarkNotObserved()
        {
            CurrentlyObserved = false;
            CurrentObservationLevel = null;
            CurrentObservedSectorId = null;
        }

        public void Update(
            TacticalObservationEvent observation,
            long hour)
        {
            LastObservedHour = hour;
            CurrentlyObserved = true;

            CurrentObservationLevel =
                observation.KnowledgeLevel;

            CurrentObservedSectorId =
                observation.ObservedSectorId;

            if (observation.KnowledgeLevel > BestKnownLevel)
            {
                BestKnownLevel = observation.KnowledgeLevel;
            }

            if (observation.KnowledgeLevel
                >= TacticalKnowledgeLevel.Located)
            {
                // Une véritable localisation rafraîchit
                // la position, le secteur et sa précision.
                SectorId = observation.ObservedSectorId;
                Position = observation.ObservedPosition;

                PositionUncertaintyMeters =
                    observation.PositionUncertaintyMeters;

                LastLocatedHour = hour;
            }
            else if (Position is null)
            {
                // Une simple suspicion peut fournir
                // un secteur, mais aucune position.
                SectorId = observation.ObservedSectorId;
            }

            if (observation.KnowledgeLevel
                == TacticalKnowledgeLevel.Identified)
            {
                // L'identification provient du rapport,
                // jamais des propriétés réelles de la cible.
                EstimatedCompanyType =
                    observation.EstimatedCompanyType;
            }
        }

        public TacticalEnemyContactReport Capture()
        {
            return new TacticalEnemyContactReport(
                Id,
                SectorId,
                Position,
                LastObservedHour,
                CurrentlyObserved)
            {
                BestKnownLevel = BestKnownLevel,
                CurrentObservationLevel =
                    CurrentObservationLevel,
                CurrentObservedSectorId =
                    CurrentObservedSectorId,
                PositionUncertaintyMeters =
                    PositionUncertaintyMeters,
                LastLocatedHour = LastLocatedHour,
                EstimatedCompanyType =
                    EstimatedCompanyType
            };
        }
    }

    private readonly TacticalBattle _battle;

    internal TacticalBattle Battle => _battle;

    // La correspondance avec la véritable cible
    // n'existe que dans l'état interne du moteur.
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

    public IReadOnlyList<TacticalEnemyContactReport> GetReports(
        CompanyDeployment observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        if (!_battle.ContainsDeployment(observer))
        {
            throw new InvalidOperationException(
                "Observer does not belong to this battle.");
        }

        if (!_tracks.TryGetValue(observer, out var tracks))
        {
            return Array.Empty<TacticalEnemyContactReport>();
        }

        return Array.AsReadOnly(
            tracks.Values
                .OrderBy(t => t.Id)
                .Select(t => t.Capture())
                .ToArray());
    }

    internal void EnsureCanUpdate(long hour)
    {
        if (hour < _battle.Engagement.StartHour)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hour),
                "Update precedes the engagement.");
        }

        if (_lastUpdateHour is long last && hour < last)
        {
            throw new InvalidOperationException(
                "Perception time cannot move backwards.");
        }
    }

    /// <summary>
    /// Les informations observées pendant une passe
    /// rafraîchissent les pistes correspondantes.
    ///
    /// Une piste non réobservée devient périmée,
    /// mais son dernier renseignement est conservé.
    /// </summary>
    internal void Apply(
        long hour,
        IReadOnlyList<TacticalObservationEvent> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        EnsureCanUpdate(hour);

        // Valider toute la passe avant de modifier l'état.
        var pairs = new HashSet<
            (CompanyDeployment, CompanyDeployment)>();

        foreach (var observation in observations)
        {
            ValidateObservation(observation);

            if (!pairs.Add((
                observation.Observer,
                observation.Target)))
            {
                throw new InvalidOperationException(
                    "Duplicate observation in one update.");
            }
        }

        foreach (var observerTracks in _tracks.Values)
        {
            foreach (var track in observerTracks.Values)
                track.MarkNotObserved();
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

                _tracks.Add(observation.Observer, observerTracks);
            }

            if (!observerTracks.TryGetValue(
                observation.Target,
                out var track))
            {
                track = new Track(
                    _nextTrackId++,
                    observation,
                    hour);

                observerTracks.Add(observation.Target, track);
            }
            else
            {
                track.Update(observation, hour);
            }
        }

        _lastUpdateHour = hour;
    }

    private void ValidateObservation(
        TacticalObservationEvent observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (observation.Observer is null
            || observation.Target is null
            || ReferenceEquals(
                observation.Observer, observation.Target)
            || !_battle.ContainsDeployment(observation.Observer)
            || !_battle.ContainsDeployment(observation.Target))
        {
            throw new InvalidOperationException(
                "Invalid observation participants.");
        }

        if (!Enum.IsDefined(observation.KnowledgeLevel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(observation),
                "Invalid knowledge level.");
        }

        if (!_battle.Battlefield.Sectors.Any(
            s => s.Id == observation.ObservedSectorId))
        {
            throw new InvalidOperationException(
                "Observed sector does not belong to the battle.");
        }

        if (observation.KnowledgeLevel
            == TacticalKnowledgeLevel.Suspected)
        {
            if (observation.ObservedPosition is not null
                || observation.PositionUncertaintyMeters is not null
                || observation.EstimatedCompanyType is not null)
            {
                throw new InvalidOperationException(
                    "A suspicion cannot contain a precise position " +
                    "or unit identification.");
            }

            return;
        }

        if (observation.ObservedPosition is null)
        {
            throw new InvalidOperationException(
                "Localized observations require a position.");
        }

        if (observation.PositionUncertaintyMeters
            is not double uncertainty
            || !double.IsFinite(uncertainty)
            || uncertainty < 0)
        {
            throw new InvalidOperationException(
                "Invalid position uncertainty.");
        }

        if (observation.KnowledgeLevel
            == TacticalKnowledgeLevel.Identified)
        {
            if (observation.EstimatedCompanyType
                is not CompanyType category
                || !Enum.IsDefined(category))
            {
                throw new InvalidOperationException(
                    "Identification requires a valid unit category.");
            }
        }
        else if (observation.EstimatedCompanyType is not null)
        {
            throw new InvalidOperationException(
                "A located contact is not yet identified.");
        }
    }


    /// <summary>
    /// Verifie qu'un observateur possede une localisation
    /// ACTUELLE de cette cible, sans exposer l'identite
    /// reelle de la cible dans un rapport public.
    /// </summary>
    internal bool HasCurrentLocalization(
        CompanyDeployment observer,
        CompanyDeployment target,
        long currentHour)
    {
        if (!_battle.ContainsDeployment(observer)
            || !_battle.ContainsDeployment(target))
        {
            return false;
        }

        return _tracks.TryGetValue(observer, out var contacts)
            && contacts.TryGetValue(target, out var track)
            && track.CurrentlyObserved
            && track.LastObservedHour == currentHour
            && track.LastLocatedHour == currentHour
            && track.CurrentObservationLevel
                >= TacticalKnowledgeLevel.Located
            && track.Position is not null;
    }

}
