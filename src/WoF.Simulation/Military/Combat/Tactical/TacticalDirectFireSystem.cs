
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Execute les salves d'une bataille tactique.
/// Une seule phase de tir est autorisee par heure.
/// Aucun impact ni degat n'est calcule ici.
/// </summary>
internal sealed class TacticalDirectFireSystem
{
    private readonly TacticalBattle _battle;

    private long? _lastProcessedHour;


    // Informations figees au moment de l'execution.
    private TacticalFireSnapshot[] _lastSnapshots =
        Array.Empty<TacticalFireSnapshot>();

    // Identite de la derniere phase deja resolue.
    private TacticalFirePhaseResult? _resolvedSource;

    internal TacticalImpactPhaseResult? LastImpactPhase
    {
        get;
        private set;
    }

    private TacticalFirePhaseResult? _personnelResolvedSource;

    internal TacticalPersonnelContactPhaseResult? LastPersonnelPhase
    {
        get;
        private set;
    }

    private TacticalPersonnelContactPhaseResult? _effectsResolvedSource;

    internal TacticalPersonnelEffectsPhaseResult? LastPersonnelEffectsPhase
    {
        get;
        private set;
    }


    internal TacticalFirePhaseResult? LastPhase { get; private set; }

    internal TacticalDirectFireSystem(TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }


    /// <summary>
    /// Estime les impacts de la derniere phase de tir.
    /// A appeler avant la prochaine mise a jour des contacts.
    /// Ne consomme aucune munition supplementaire.
    /// </summary>
    internal IReadOnlyList<TacticalHitEstimate> EstimateLastPhase(
        TacticalGeometryMap geometry,
        TacticalPerceptionState perception,
        TacticalHitProfile profile)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(perception);
        ArgumentNullException.ThrowIfNull(profile);

        if (!ReferenceEquals(geometry.Battle, _battle)
            || !ReferenceEquals(perception.Battle, _battle))
        {
            throw new InvalidOperationException(
                "Estimation services belong to another battle.");
        }

        var phase = LastPhase
            ?? throw new InvalidOperationException(
                "No fire phase has been executed.");

        var estimates = new TacticalHitEstimate[phase.Events.Count];

        for (int i = 0; i < phase.Events.Count; i++)
        {
            estimates[i] = TacticalHitProbabilityEvaluator.Evaluate(
                phase.Events[i],
                geometry,
                perception,
                profile);
        }

        return Array.AsReadOnly(estimates);
    }


    /// <summary>
    /// Estime les impacts non interceptes de la derniere
    /// phase, avec la defense REELLE de chaque cible.
    ///
    /// A appeler avant changement des perceptions ou
    /// dispositions defensives concernees.
    /// Ne modifie aucune ressource.
    /// </summary>
    internal IReadOnlyList<TacticalProtectedHitEstimate>
        EstimateProtectedLastPhase(
            TacticalGeometryMap geometry,
            TacticalPerceptionState perception,
            TacticalHitProfile hitProfile,
            TacticalDefenseProfile defenseProfile)
    {
        ArgumentNullException.ThrowIfNull(defenseProfile);

        // Cette methode valide deja les appartenances
        // de bataille et la fraicheur des contacts.
        var geometricEstimates = EstimateLastPhase(
            geometry,
            perception,
            hitProfile);

        var phase = LastPhase
            ?? throw new InvalidOperationException(
                "No fire phase has been executed.");

        var results = new TacticalProtectedHitEstimate[
            phase.Events.Count];

        for (int i = 0; i < phase.Events.Count; i++)
        {
            var fire = phase.Events[i];

            // La cible reelle n'est retrouvee qu'ici,
            // dans le traitement interne du moteur.
            var target = perception.ResolveCurrentTarget(
                fire.Shooter,
                fire.TargetTrackId,
                fire.Hour)
                ?? throw new InvalidOperationException(
                    "No current target for this salvo.");

            var disposition = _battle.DefensiveState.Get(target);

            results[i] = TacticalProtectedHitEvaluator.Evaluate(
                geometricEstimates[i],
                disposition,
                defenseProfile);
        }

        return Array.AsReadOnly(results);
    }



    private readonly record struct PlannedSalvo(
        CompanyAmmunitionInventory Inventory,
        TacticalFireEvent Event,
        TacticalFireSnapshot Snapshot);

    internal TacticalFirePhaseResult Execute(
        IReadOnlyList<TacticalFireOrder> orders,
        TacticalGeometryMap geometry,
        DiplomacySystem diplomacy,
        TacticalPerceptionState perception,
        long hour)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(perception);

        // Tous les services doivent travailler
        // sur la meme bataille.
        if (!ReferenceEquals(geometry.Battle, _battle)
            || !ReferenceEquals(perception.Battle, _battle))
        {
            throw new InvalidOperationException(
                "Fire services belong to different battles.");
        }

        if (hour < _battle.Engagement.StartHour)
        {
            throw new ArgumentOutOfRangeException(nameof(hour));
        }

        // Empeche de rejouer accidentellement une phase.
        if (_lastProcessedHour is long previous
            && hour <= previous)
        {
            throw new InvalidOperationException(
                "Fire phase already processed or time moved backwards.");
        }

        // PREMIERE PASSE : validation de tous les ordres.
        // Aucune ressource n'est modifiee ici.
        var shooters = new HashSet<CompanyDeployment>(
            ReferenceEqualityComparer.Instance);

        foreach (var order in orders)
        {
            ArgumentNullException.ThrowIfNull(order);
            ArgumentNullException.ThrowIfNull(order.Shooter);
            ArgumentNullException.ThrowIfNull(order.Target);
            ArgumentNullException.ThrowIfNull(order.Profile);

            if (order.WeaponsCommitted <= 0
                || order.RoundsPerWeapon <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(orders),
                    "Weapon and round counts must be positive.");
            }

            if (!_battle.ContainsDeployment(order.Shooter)
                || !_battle.ContainsDeployment(order.Target))
            {
                throw new InvalidOperationException(
                    "Fire order contains a foreign deployment.");
            }

            if (!shooters.Add(order.Shooter))
            {
                throw new InvalidOperationException(
                    "A company has multiple orders in one fire phase.");
            }
        }

        // DEUXIEME PASSE : preparer les salves.
        // Toujours aucune consommation.
        var planned = new List<PlannedSalvo>(orders.Count);

        var outcomes = new List<TacticalFireOrderOutcome>(
            orders.Count);

        foreach (var order in orders)
        {
            var authorization = TacticalDirectFireEvaluator.Evaluate(
                _battle,
                geometry,
                diplomacy,
                perception,
                order.Shooter,
                order.Target,
                hour,
                order.Profile);

            if (!authorization.CanFire)
            {
                outcomes.Add(new TacticalFireOrderOutcome(
                    TacticalFireOrderStatus.AuthorizationDenied,
                    authorization.Status));

                continue;
            }

            var resources = order.Shooter.Company.Composition!;

            // Premier modele : un combattant disponible
            // peut servir au maximum une arme d'infanterie.
            int availableWeapons = Math.Min(
                resources.Manpower.Current,
                resources.InfantryEquipment.Current);

            if (order.WeaponsCommitted > availableWeapons)
            {
                outcomes.Add(new TacticalFireOrderOutcome(
                    TacticalFireOrderStatus.InsufficientWeapons,
                    TacticalDirectFireStatus.Allowed));

                continue;
            }

            // Multiplication en long : pas de debordement
            // par multiplication d'entiers 32 bits.
            long rounds = checked(
                (long)order.WeaponsCommitted
                * order.RoundsPerWeapon);

            var inventory = resources.Ammunition;

            if (inventory.GetAvailable(AmmunitionType.SmallArms)
                < rounds)
            {
                outcomes.Add(new TacticalFireOrderOutcome(
                    TacticalFireOrderStatus.InsufficientAmmunition,
                    TacticalDirectFireStatus.Allowed));

                continue;
            }

            // La piste est necessairement actuelle
            // puisque l'autorisation a reussi.
            int trackId = perception.GetCurrentLocalizedTrackId(
                order.Shooter,
                order.Target,
                hour)
                ?? throw new InvalidOperationException(
                    "Authorized target has no current track.");

            var fireEvent = new TacticalFireEvent(
                Hour: hour,
                Shooter: order.Shooter,
                TargetTrackId: trackId,
                AmmunitionType: AmmunitionType.SmallArms,
                WeaponsCommitted: order.WeaponsCommitted,
                RoundsExpended: rounds);

            var snapshot = CaptureFireSnapshot(
                order,
                fireEvent,
                geometry,
                perception
            );

            planned.Add(new PlannedSalvo(
                inventory,
                fireEvent,
                snapshot));

            outcomes.Add(new TacticalFireOrderOutcome(
                TacticalFireOrderStatus.Fired,
                TacticalDirectFireStatus.Allowed));
        }

        // Preparer les resultats consultables avant
        // de commencer les mutations physiques.
        var events = Array.AsReadOnly(
            planned.Select(p => p.Event).ToArray());

        var result = new TacticalFirePhaseResult(
            hour,
            Array.AsReadOnly(outcomes.ToArray()),
            events);

        // TROISIEME PASSE : application physique.
        // Une seule commande par compagnie garantit
        // qu'un meme inventaire n'est pas debite deux fois.
        // Le moteur est monothread a ce stade.
        foreach (var salvo in planned)
        {
            if (!salvo.Inventory.TryConsume(
                salvo.Event.AmmunitionType,
                salvo.Event.RoundsExpended))
            {
                throw new InvalidOperationException(
                    "Prepared ammunition consumption failed.");
            }
        }

        _lastSnapshots = planned 
            .Select(p => p.Snapshot)
            .ToArray();

        _lastProcessedHour = hour;
        LastPhase = result;

        _resolvedSource = null;
        LastImpactPhase = null;

        _personnelResolvedSource = null;
        LastPersonnelPhase = null;

        _effectsResolvedSource = null;
        LastPersonnelEffectsPhase = null;

        return result;
    }



    /// <summary>
    /// Capture toutes les donnees necessaires a la
    /// resolution future, au moment du tir.
    /// </summary>
    private TacticalFireSnapshot CaptureFireSnapshot(
        TacticalFireOrder order,
        TacticalFireEvent fire,
        TacticalGeometryMap geometry,
        TacticalPerceptionState perception)
    {
        var shooterPosition =
            geometry.GetPosition(order.Shooter);

        var targetPosition =
            geometry.GetPosition(order.Target);

        var report = perception.GetCurrentTrackReport(
            order.Shooter,
            fire.TargetTrackId,
            fire.Hour);

        if (shooterPosition is null
            || targetPosition is null
            || report?.LastKnownPosition is null
            || report.PositionUncertaintyMeters is null)
        {
            throw new InvalidOperationException(
                "Cannot capture an incomplete fire snapshot.");
        }

        var defense = _battle.DefensiveState.Get(order.Target);

        var personnel = _battle.PersonnelExposure.Get(order.Target);

        return new TacticalFireSnapshot(
            FireEvent: fire,
            RealTarget: order.Target,
            ShooterPoint: shooterPosition.Point,
            ReportedTargetPoint: report.LastKnownPosition.Value,
            RealTargetPoint: targetPosition.Point,
            LocalizationUncertaintyMeters:
                report.PositionUncertaintyMeters.Value,
            TargetDefense: defense,
            TargetPersonnel : personnel,
            TargetFrontageMeters : targetPosition.FrontageMeters,
            TargetFacingDegrees: targetPosition.FacingDegrees);
    }



    /// <summary>
    /// Resout une seule fois les impacts de la derniere phase.
    /// Aucun nouveau tir et aucune mutation des effectifs.
    /// </summary>
    internal TacticalImpactPhaseResult ResolveLastPhase(
        TacticalHitProfile hitProfile,
        TacticalDefenseProfile defenseProfile,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(hitProfile);
        ArgumentNullException.ThrowIfNull(defenseProfile);

        var phase = LastPhase
            ?? throw new InvalidOperationException(
                "No fire phase has been executed.");

        if (ReferenceEquals(_resolvedSource, phase))
        {
            throw new InvalidOperationException(
                "This fire phase has already been resolved.");
        }

        if (_lastSnapshots.Length != phase.Events.Count)
        {
            throw new InvalidOperationException(
                "Fire snapshots do not match the executed phase.");
        }

        var impacts = new TacticalImpactEvent[
            _lastSnapshots.Length];

        // Chaque salve possede une graine derivee.
        // L'ordre est celui des evenements executes.
        for (int i = 0; i < _lastSnapshots.Length; i++)
        {
            ulong salvoSeed = unchecked(
                seed + (ulong)i * 0x9E3779B97F4A7C15UL);

            impacts[i] = TacticalImpactResolver.Resolve(
                _lastSnapshots[i],
                hitProfile,
                defenseProfile,
                salvoSeed);
        }

        var result = new TacticalImpactPhaseResult(
            Hour: phase.Hour,
            Seed: seed,
            Events: Array.AsReadOnly(impacts));

        _resolvedSource = phase;
        LastImpactPhase = result;

        return result;
    }


    /// <summary>
    /// Resout les contacts avec la population exposee.
    /// Ne modifie ni les hommes ni les munitions.
    /// Une seule resolution de population par phase.
    /// </summary>
    internal TacticalPersonnelContactPhaseResult
        ResolvePersonnelLastPhase(
            TacticalHitProfile hitProfile,
            TacticalDefenseProfile defenseProfile,
            ulong seed)
    {
        ArgumentNullException.ThrowIfNull(hitProfile);
        ArgumentNullException.ThrowIfNull(defenseProfile);

        var phase = LastPhase
            ?? throw new InvalidOperationException(
                "No fire phase has been executed.");

        if (ReferenceEquals(_personnelResolvedSource, phase))
        {
            throw new InvalidOperationException(
                "Personnel contacts already resolved.");
        }

        if (_lastSnapshots.Length != phase.Events.Count)
        {
            throw new InvalidOperationException(
                "Fire snapshots do not match the phase.");
        }

        var events = new TacticalPersonnelContactEvent[
            _lastSnapshots.Length];

        // Regroupement par vraie compagnie.
        // Ne jamais utiliser le secteur comme identite
        // militaire : une compagnie couvre plusieurs
        // secteurs sans etre dupliquee.
        var totals = new Dictionary<
            CompanyDeployment,
            (int Exposed, long Contacts)>(
                ReferenceEqualityComparer.Instance);

        for (int i = 0; i < _lastSnapshots.Length; i++)
        {
            var snapshot = _lastSnapshots[i];

            ulong salvoSeed = unchecked(
                seed + (ulong)i * 0x9E3779B97F4A7C15UL);

            var contact = TacticalPersonnelContactResolver.Resolve(
                snapshot,
                hitProfile,
                defenseProfile,
                salvoSeed);

            events[i] = contact;

            if (totals.TryGetValue(
                contact.RealTarget,
                out var current))
            {
                if (current.Exposed != contact.ExposedPersonnel)
                {
                    throw new InvalidOperationException(
                        "Inconsistent target exposure within one phase.");
                }

                totals[contact.RealTarget] = (
                    current.Exposed,
                    checked(current.Contacts + contact.ContactEvents));
            }
            else
            {
                totals.Add(
                    contact.RealTarget,
                    (
                        contact.ExposedPersonnel,
                        contact.ContactEvents
                    ));
            }
        }

        // Evaluation des personnes distinctes sur
        // le TOTAL des tirs diriges vers une compagnie,
        // plutot qu'en additionnant des estimations
        // independantes par salve.
        var targets = new List<TacticalPersonnelTargetSummary>(
            totals.Count);

        foreach (var entry in totals)
        {
            targets.Add(new TacticalPersonnelTargetSummary(
                Target: entry.Key,
                ExposedPersonnel: entry.Value.Exposed,
                ContactEvents: entry.Value.Contacts,
                ExpectedDistinctPersonnel:
                    TacticalPersonnelContactResolver.ExpectedDistinct(
                        entry.Value.Exposed,
                        entry.Value.Contacts)));
        }

        var result = new TacticalPersonnelContactPhaseResult(
            Hour: phase.Hour,
            Seed: seed,
            Events: Array.AsReadOnly(events),
            Targets: Array.AsReadOnly(targets.ToArray()));

        // Publier le resultat seulement apres avoir
        // termine tous les calculs et validations.
        _personnelResolvedSource = phase;
        LastPersonnelPhase = result;

        return result;
    }



    /// <summary>
    /// Convertit les contacts de la derniere phase en
    /// consequences sur les combattants distincts.
    ///
    /// Doit etre execute apres ResolvePersonnelLastPhase.
    /// N'applique PAS encore les pertes a PersonnelPool.
    /// </summary>
    internal TacticalPersonnelEffectsPhaseResult
        ResolvePersonnelEffectsLastPhase(
            TacticalPersonnelEffectProfile profile,
            ulong seed)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var contacts = LastPersonnelPhase
            ?? throw new InvalidOperationException(
                "Personnel contacts have not been resolved.");

        // Les contacts doivent correspondre a
        // la phase de tir actuellement conservee.
        if (LastPhase is null
            || !ReferenceEquals(_personnelResolvedSource, LastPhase))
        {
            throw new InvalidOperationException(
                "Personnel contacts are not from the current fire phase.");
        }

        if (ReferenceEquals(_effectsResolvedSource, contacts))
        {
            throw new InvalidOperationException(
                "Personnel effects already resolved for this phase.");
        }

        var results = new TacticalPersonnelEffectResult[
            contacts.Targets.Count];

        for (int i = 0; i < contacts.Targets.Count; i++)
        {
            var target = contacts.Targets[i];

            if (!_battle.ContainsDeployment(target.Target))
            {
                throw new InvalidOperationException(
                    "Effect target belongs to another tactical battle.");
            }

            ulong targetSeed = unchecked(
                seed + (ulong)i * 0x9E3779B97F4A7C15UL);

            results[i] = TacticalPersonnelEffectResolver.Resolve(
                target,
                profile,
                targetSeed);
        }

        var result = new TacticalPersonnelEffectsPhaseResult(
            Hour: contacts.Hour,
            Seed: seed,
            Targets: Array.AsReadOnly(results));

        // Publier uniquement apres le traitement complet.
        _effectsResolvedSource = contacts;
        LastPersonnelEffectsPhase = result;

        return result;
    }

}
