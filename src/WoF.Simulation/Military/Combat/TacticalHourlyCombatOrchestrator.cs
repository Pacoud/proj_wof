
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Bilan compact d'une heure de combat tactique.
/// Les pertes detaillees restent dans LastCasualtyPhase.
/// </summary>
internal readonly record struct TacticalHourlyCombatResult(
    long Hour,
    int OrdersIssued,
    int SalvosFired,
    long RoundsExpended,
    int Killed,
    int Wounded);

internal readonly record struct TacticalMobileCombatSummary(
    long Hour,
    int ConfirmedContacts,
    int MobileOrdersIssued,
    int MobileSalvosFired);

/// <summary>
/// Coordonne une heure de combat tactique.
///
/// Reutilise detection, perception, tir, contacts,
/// effets et application des pertes existants.
///
/// La selection des tirs est provisoire. Elle pourra
/// etre remplacee par le commandement tactique.
/// </summary>
internal sealed class TacticalHourlyCombatOrchestrator
{
    private readonly TacticalBattle _battle;
    private readonly TacticalGeometryMap _geometry;
    private readonly TacticalPerceptionState _perception;
    private readonly TacticalSignatureFrame _signatures;
    private readonly DiplomacySystem _diplomacy;
    private readonly TacticalHourlyCombatSettings _settings;
    private readonly TacticalPhaseTimingRecorder? _timingRecorder;
    private readonly TacticalBattalionReinforcementCommandSystem? _automaticReinforcementCommander;

    private long? _lastCompletedHour;

    internal TacticalBattle Battle => _battle;


    internal TacticalPerceptionState Perception => _perception;

    internal TacticalBattalionAssessmentProfile?
        BattalionAssessmentProfile =>
            _settings.BattalionAssessment;


    internal TacticalHourlyCombatResult? LastResult
    {
        get;
        private set;
    }


    internal TacticalAutomaticReinforcementPhaseResult? LastAutomaticReinforcementPhase
    {
        get;
        private set;
    }


    internal TacticalMobileCombatSummary? LastMobileCombatSummary { get; private set; }


    internal TacticalBattalionReactionPhaseResult?
        LastBattalionReactionPhase
     {
        get;
        private set;
    }


    internal TacticalReactionExecutionState? ReactionExecution
    {
        get;
    }



    internal TacticalMovementPhaseResult? LastMovementPhase =>
        ReactionExecution?.LastMovementPhase;


    internal TacticalCohesionPhaseResult? LastCohesionPhase
        { get; private set; }



    internal IReadOnlyList<TacticalBattalionAssessment>
            LastBattalionAssessments { get; private set; } =
                Array.Empty<TacticalBattalionAssessment>();





    private sealed record RegisteredCommandFront(
        TacticalCommandSituationRegistration Registration,
        Battalion Battalion,
        IReadOnlyList<BattleSector> Sectors,
        long MaxAgeHours);

    
    private Dictionary<
        TacticalCommandSituationRegistration,
        TacticalCommandSituationReadResult>
        _lastPublishedByRegistration =
            new(ReferenceEqualityComparer.Instance);


    private readonly List<RegisteredCommandFront>
        _registeredCommandFronts = new();

    internal TacticalCommandSituationPhaseResult?
        LastCommandSituationPhase
    {
        get;
        private set;
    }


    private readonly Func<
        Battalion,
        IReadOnlyList<BattleSector>,
        long,
        TacticalBattalionCommandSituationSnapshot>?
            _commandSituationCaptureOverride;







    internal TacticalHourlyCombatOrchestrator(
        TacticalBattle battle,
        TacticalGeometryMap geometry,
        TacticalPerceptionState perception,
        TacticalSignatureFrame signatures,
        DiplomacySystem diplomacy,
        TacticalHourlyCombatSettings settings,
        TacticalPhaseTimingRecorder? timingRecorder = null,
        Func<
            Battalion,
            IReadOnlyList<BattleSector>,
            long,
            TacticalBattalionCommandSituationSnapshot>?
                commandSituationCaptureOverride = null)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));

        _geometry = geometry
            ?? throw new ArgumentNullException(nameof(geometry));

        _perception = perception
            ?? throw new ArgumentNullException(nameof(perception));

        _signatures = signatures
            ?? throw new ArgumentNullException(nameof(signatures));

        _diplomacy = diplomacy
            ?? throw new ArgumentNullException(nameof(diplomacy));

        _settings = settings
            ?? throw new ArgumentNullException(nameof(settings));

        _timingRecorder = timingRecorder;

        _commandSituationCaptureOverride = commandSituationCaptureOverride;

        if (!ReferenceEquals(geometry.Battle, battle)
            || !ReferenceEquals(perception.Battle, battle)
            || !ReferenceEquals(signatures.Battle, battle))
        {
            throw new InvalidOperationException(
                "Tactical services must belong to the same battle.");
        }

        if (_settings.ReactionMovement is not null)
        {
            ReactionExecution = new TacticalReactionExecutionState(
                _battle,
                _geometry,
                _diplomacy,
                _settings.ReactionMovement,
                _settings.EnableMobileCombat,
                _timingRecorder);
        }
        
        if (_settings.EnableAutomaticReinforcements)
        {
            _automaticReinforcementCommander =
                new TacticalBattalionReinforcementCommandSystem(
                    this);
        }
    }


    private TacticalBattalionCommandSituationSnapshot
    CaptureRegisteredCommandSituation(
        RegisteredCommandFront registered)
    {
        // Point d'injection interne permettant de
        // reproduire une erreur de capture en test.
        if (_commandSituationCaptureOverride is not null)
        {
            return _commandSituationCaptureOverride(
                registered.Battalion,
                registered.Sectors,
                registered.MaxAgeHours);
        }

        // Chemin normal : reutilisation de 9l.
        return new TacticalBattalionCommandSituationSystem(
            this).Capture(
                registered.Battalion,
                registered.Sectors,
                registered.MaxAgeHours);
    }



    /// <summary>
    /// Active la publication horaire pour un bataillon
    /// et son perimetre explicite de responsabilite.
    ///
    /// A appeler avant la premiere heure tactique.
    /// </summary>
    internal TacticalCommandSituationRegistration RegisterCommandSituation(
        Battalion battalion,
        IReadOnlyList<BattleSector> sectors,
        long maxAgeHours = 2)
    {
        ArgumentNullException.ThrowIfNull(battalion);
        ArgumentNullException.ThrowIfNull(sectors);

        if (_lastCompletedHour is not null)
        {
            throw new InvalidOperationException(
                "Command situation registration must " +
                "precede the first completed tactical hour.");
        }

        if (!_battle.Engagement.IsActive)
        {
            throw new InvalidOperationException(
                "Cannot register command situation " +
                "for an ended battle.");
        }

        if (_settings.BattalionAssessment is null
            || _settings.BattalionReactions is null
            || _settings.Suppression is null
            || _settings.Cohesion is null)
        {
            throw new InvalidOperationException(
                "Command situation requires assessments, " +
                "reactions, suppression and cohesion.");
        }

        if (maxAgeHours < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAgeHours));
        }

        if (sectors.Count == 0)
        {
            throw new ArgumentException(
                "Responsibility sectors cannot be empty.",
                nameof(sectors));
        }

        if (_registeredCommandFronts.Any(r =>
            ReferenceEquals(r.Battalion, battalion)))
        {
            throw new InvalidOperationException(
                "Battalion is already registered.");
        }

        var uniqueSectors = new HashSet<BattleSector>(
            ReferenceEqualityComparer.Instance);

        foreach (var sector in sectors)
        {
            if (sector is null
                || !_battle.Battlefield.ContainsSector(sector)
                || !uniqueSectors.Add(sector))
            {
                throw new ArgumentException(
                    "Invalid or duplicate responsibility sector.",
                    nameof(sectors));
            }
        }

        var deployments =
            _battle.GetBattalionDeployments(battalion);

        var representedCompanies = new HashSet<Company>(
            ReferenceEqualityComparer.Instance);

        if (deployments.Count == 0
            || deployments.Count != battalion.Companies.Count)
        {
            throw new InvalidOperationException(
                "Battalion must be fully deployed.");
        }

        foreach (var deployment in deployments)
        {
            if (!_battle.ContainsDeployment(deployment)
                || !ReferenceEquals(
                    deployment.Company.ParentBattalion,
                    battalion)
                || !representedCompanies.Add(deployment.Company))
            {
                throw new InvalidOperationException(
                    "Invalid battalion deployments.");
            }
        }


        var registration =
            new TacticalCommandSituationRegistration();

        _registeredCommandFronts.Add(
            new RegisteredCommandFront(
                registration,
                battalion,
                Array.AsReadOnly(sectors.ToArray()),
                maxAgeHours));

        return registration;

    }


    /// <summary>
    /// Controle les conditions connues avant
    /// toute mise a jour des renseignements ou
    /// consommation de munitions.
    /// </summary>
    internal void ValidateCanRun(long hour)
    {
        if (!_battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "The engagement has ended.");

        if (hour < _battle.Engagement.StartHour)
            throw new ArgumentOutOfRangeException(nameof(hour));

        if (_lastCompletedHour is long completed
            && hour <= completed)
        {
            throw new InvalidOperationException(
                "Tactical hour already completed.");
        }

        var previous = _battle.DirectFire.LastPhase;

        if (previous is not null)
        {
            if (previous.Hour >= hour)
            {
                throw new InvalidOperationException(
                    "Fire phase is already present for this hour.");
            }

            if (_battle.DirectFire.LastCasualtyPhase?.Hour
                != previous.Hour)
            {
                throw new InvalidOperationException(
                    "Previous casualty phase is incomplete.");
            }
        }

        // Tous les participants actifs doivent
        // posseder un deploiement tactique.
        var representedDivisions = _battle.Deployments
            .Select(d => d.Company.ParentBattalion!
                .ParentDivision!)
            .ToHashSet(ReferenceEqualityComparer.Instance);

        foreach (var division in _battle.Engagement.Participants)
        {
            if (!division.IsBroken
                && ReferenceEquals(
                    division.CurrentEngagement,
                    _battle.Engagement)
                && !representedDivisions.Contains(division))
            {
                throw new InvalidOperationException(
                    "Engaged division has no tactical deployment.");
            }
        }

        // Les positions et repartitions doivent
        // etre valides avant de traiter la phase.
        foreach (var deployment in _battle.Deployments)
        {
            if (_geometry.GetPosition(deployment) is null)
            {
                throw new InvalidOperationException(
                    "A deployed company has no tactical position.");
            }

            _battle.PersonnelExposure.Get(deployment);
        }

        // Verifier la couverture des bataillons AVANT
        // de tirer ou de modifier les ressources.
        if (_settings.BattalionAssessment is not null)
        {
            _battle.BattalionAssessments.EvaluateAll(
                _settings.BattalionAssessment);
        }


        // Verifier avant le tir que la phase de reactions
        // n'a pas deja ete appliquee.
        if (_settings.BattalionReactions is not null)
        {
            _battle.BattalionReactions.EnsureCanApply(
                hour,
                _settings.BattalionAssessment!,
                _settings.BattalionReactions);
        }

        ReactionExecution?.EnsureCanAdvance(hour);

        _perception.EnsureCanUpdate(hour);
    }

    /// <summary>
    /// Execute l'ensemble de la chaine pour une heure.
    /// N'avance pas lui-meme l'horloge generale.
    /// </summary>

    internal TacticalHourlyCombatResult Run(long hour)
    {
        var timer = _timingRecorder;

        // 0. Validation avant toute mutation.
        var validationMark = timer?.Start();

        ValidateCanRun(hour);

        if (validationMark is { } validationStart)
        {
            timer!.Stop(
                TacticalBenchmarkPhase.Validation,
                validationStart);
        }

        // Les mouvements seront instrumentes en detail
        // pendant C2.4d.8d.3c.
        var movementPhase = ReactionExecution?.AdvanceHour(hour);

        // Preparation des participants aux contacts mobiles.
        var mobileMark = timer?.Start();

        var signatures = _signatures;

        if (_settings.EnableMobileCombat)
        {
            var contacts =
                movementPhase?.ConfirmedMobileContacts
                ?? Array.Empty<TacticalConfirmedMobileContact>();

            _battle.MobileEngagements.Publish(
                hour,
                contacts,
                _geometry,
                _diplomacy,
                _settings.ReactionMovement!
                    .TransitContactDistanceMeters);

            if (contacts.Count > 0)
            {
                signatures =
                    PrepareMobileCombatParticipants(contacts);
            }
        }

        if (mobileMark is { } mobileStart)
        {
            timer!.Stop(
                TacticalBenchmarkPhase.MobileParticipantPreparation,
                mobileStart);
        }

        // 1. Detection et renseignement.
        var detectionMark = timer?.Start();

        TacticalDetectionSystem.Update(
            _battle,
            _geometry,
            _diplomacy,
            _perception,
            signatures,
            hour,
            _settings.Detection);

        if (detectionMark is { } detectionStart)
        {
            timer!.Stop(
                TacticalBenchmarkPhase.Detection,
                detectionStart);
        }

        // 2. Construction des ordres de tir.
        var planningMark = timer?.Start();

        var orders = BuildFireOrders(hour);

        if (planningMark is { } planningStart)
        {
            timer!.Stop(
                TacticalBenchmarkPhase.FireOrderPlanning,
                planningStart);
        }

        // 3. Execution unique de la phase de tir.
        var fireMark = timer?.Start();

        var fire = _battle.DirectFire.Execute(
            orders,
            _geometry,
            _diplomacy,
            _perception,
            hour);

        if (fireMark is { } fireStart)
        {
            timer!.Stop(
                TacticalBenchmarkPhase.FireExecution,
                fireStart);
        }

        ulong phaseSeed = unchecked(
            _settings.BaseSeed
            + (ulong)hour * 0x9E3779B97F4A7C15UL);

        // 4. Contacts entre projectiles et personnel.
        var contactsMark = timer?.Start();

        _battle.DirectFire.ResolvePersonnelLastPhase(
            _settings.Hit,
            _settings.Defense,
            seed: phaseSeed ^ 0xA0761D6478BD642FUL);

        if (contactsMark is { } contactsStart)
        {
            timer!.Stop(
                TacticalBenchmarkPhase.PersonnelContacts,
                contactsStart);
        }

        // 5. Calcul des effets humains.
        var effectsMark = timer?.Start();

        _battle.DirectFire.ResolvePersonnelEffectsLastPhase(
            _settings.Effects,
            seed: phaseSeed ^ 0xE7037ED1A0B428DBUL);

        if (effectsMark is { } effectsStart)
        {
            timer!.Stop(
                TacticalBenchmarkPhase.PersonnelEffects,
                effectsStart);
        }

        // 6. Application unique des pertes.
        var casualtiesMark = timer?.Start();

        var casualties =
            _battle.DirectFire.ApplyPersonnelEffectsLastPhase();

        if (casualtiesMark is { } casualtiesStart)
        {
            timer!.Stop(
                TacticalBenchmarkPhase.CasualtyApplication,
                casualtiesStart);
        }

        // 7. Synchronisation des effectifs exposes.
        var exposureMark = timer?.Start();

        _battle.PersonnelExposure.ReconcileAppliedCasualties(
            casualties);

        if (exposureMark is { } exposureStart)
        {
            timer!.Stop(
                TacticalBenchmarkPhase.ExposureReconciliation,
                exposureStart);
        }

        // 8. Suppression.
        TacticalSuppressionPhaseResult? suppressionResult = null;

        if (_settings.Suppression is not null)
        {
            var suppressionMark = timer?.Start();

            suppressionResult =
                _battle.DirectFire.ResolveSuppressionLastPhase(
                    _settings.Hit,
                    _settings.Suppression);

            if (suppressionMark is { } suppressionStart)
            {
                timer!.Stop(
                    TacticalBenchmarkPhase.Suppression,
                    suppressionStart);
            }
        }

        TacticalCohesionPhaseResult? cohesionPhase = null;

        // 9. Cohesion apres pertes et suppression.
        if (_settings.Cohesion is not null)
        {
            if (suppressionResult is null
                || _settings.Suppression is null)
            {
                throw new InvalidOperationException(
                    "Cohesion requires completed suppression.");
            }

            var cohesionMark = timer?.Start();

            cohesionPhase = _battle.Cohesion.ApplyHour(
                hour,
                casualties,
                suppressionResult,
                _settings.Suppression,
                _settings.Cohesion);

            if (cohesionMark is { } cohesionStart)
            {
                timer!.Stop(
                    TacticalBenchmarkPhase.Cohesion,
                    cohesionStart);
            }
        }

        // 10. Evaluation des bataillons.
        var assessmentMark = timer?.Start();

        var battalionReports =
            _settings.BattalionAssessment is null
                ? Array.Empty<TacticalBattalionAssessment>()
                : _battle.BattalionAssessments
                    .EvaluateAll(_settings.BattalionAssessment)
                    .ToArray();

        if (assessmentMark is { } assessmentStart)
        {
            timer!.Stop(
                TacticalBenchmarkPhase.BattalionAssessment,
                assessmentStart);
        }

        // 11. Reactions tactiques.
        TacticalBattalionReactionPhaseResult? reactionPhase = null;

        if (_settings.BattalionReactions is not null)
        {
            var reactionsMark = timer?.Start();

            reactionPhase = _battle.BattalionReactions.ApplyHour(
                hour,
                battalionReports,
                _settings.BattalionAssessment!,
                _settings.BattalionReactions);

            if (reactionsMark is { } reactionsStart)
            {
                timer!.Stop(
                    TacticalBenchmarkPhase.BattalionReactions,
                    reactionsStart);
            }
        }

        // 12. Calcul du bilan horaire.
        int killed = 0;
        int wounded = 0;

        foreach (var company in casualties.Companies)
        {
            killed = checked(
                killed + company.Casualties.KilledInAction);

            wounded = checked(
                wounded + company.Casualties.WoundedInAction);
        }

        long expended = 0;

        foreach (var salvo in fire.Events)
        {
            expended = checked(
                expended + salvo.RoundsExpended);
        }

        var result = new TacticalHourlyCombatResult(
            Hour: hour,
            OrdersIssued: orders.Count,
            SalvosFired: fire.Events.Count,
            RoundsExpended: expended,
            Killed: killed,
            Wounded: wounded);

        LastBattalionAssessments =
            Array.AsReadOnly(battalionReports);

        LastBattalionReactionPhase = reactionPhase;

        _lastCompletedHour = hour;

        LastMobileCombatSummary = _settings.EnableMobileCombat
            ? SummarizeMobileCombat(hour, orders, fire)
            : null;

        LastResult = result;
        LastCohesionPhase = cohesionPhase;
        PublishCommandSituations(hour);  

        if (_automaticReinforcementCommander is not null)
        {
            var decisions = new List<
                TacticalAutomaticReinforcementDecision>();

            if (reactionPhase is not null)
            {
                bool battleCanContinue = HasCombatCapableOpposition();
                foreach (var request in reactionPhase.Requests)
                {
                    // 9i ne traite que les renforts.
                    if (request.Type !=
                        TacticalBattalionReactionType.Reinforce)
                    {
                        continue;
                    }

                    if (!battleCanContinue)
                    {
                        decisions.Add(
                            new TacticalAutomaticReinforcementDecision(
                                Request: request,
                                Status:
                                    TacticalAutomaticReinforcementStatus
                                        .SkippedBattleEnding,
                                Movement: null,
                                FailureReason: null));

                        continue;
                    }

                    try
                    {
                        var approved =
                            _automaticReinforcementCommander
                                .TryApprove(request);

                        decisions.Add(
                            new TacticalAutomaticReinforcementDecision(
                                Request: request,
                                Status: approved is null
                                    ? TacticalAutomaticReinforcementStatus
                                        .NoEligibleReserve
                                    : TacticalAutomaticReinforcementStatus
                                        .Approved,
                                Movement: approved?.Movement,
                                FailureReason: null));
                    }
                    catch (InvalidOperationException ex)
                    {
                        decisions.Add(
                            new TacticalAutomaticReinforcementDecision(
                                request,
                                TacticalAutomaticReinforcementStatus
                                    .ApprovalFailed,
                                null,
                                ex.Message));
                    }
                    catch (ArgumentException ex)
                    {
                        decisions.Add(
                            new TacticalAutomaticReinforcementDecision(
                                request,
                                TacticalAutomaticReinforcementStatus
                                    .ApprovalFailed,
                                null,
                                ex.Message));
                    }
                }
            }

            LastAutomaticReinforcementPhase =
                new TacticalAutomaticReinforcementPhaseResult(
                    hour,
                    Array.AsReadOnly(decisions.ToArray()));
        }

        return result;
    }


    private TacticalMobileCombatSummary SummarizeMobileCombat(
        long hour,
        IReadOnlyList<TacticalFireOrder> orders,
        TacticalFirePhaseResult fire)
    {
        int mobileOrders = 0;
        int mobileSalvos = 0;
        for (int i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            if (_battle.MobileEngagements.Find(order.Shooter, order.Target, hour) is null)
                continue;

            mobileOrders++;
            if (fire.Outcomes[i].Status == TacticalFireOrderStatus.Fired)
                mobileSalvos++;
        }

        return new TacticalMobileCombatSummary(
            hour, _battle.MobileEngagements.GetContacts(hour).Count,
            mobileOrders, mobileSalvos);
    }

    private TacticalSignatureFrame PrepareMobileCombatParticipants(
        IReadOnlyList<TacticalConfirmedMobileContact> contacts)
    {
        var participants = new HashSet<CompanyDeployment>(
            ReferenceEqualityComparer.Instance);
        foreach (var contact in contacts)
        {
            participants.Add(contact.First);
            participants.Add(contact.Second);
        }

        // La signature de mouvement vaut pour cette passe de detection.
        var signatures = _signatures.WithMovingParticipants(participants);
        double exposedFraction = _settings.ReactionMovement!.FrontLineExposureFraction;

        foreach (var company in participants)
        {
            var exposure = _battle.PersonnelExposure.Get(company);
            int desiredExposed = (int)Math.Ceiling(exposure.Total * exposedFraction);
            int additional = desiredExposed - exposure.Exposed;
            if (additional <= 0)
                continue;

            int fromReserve = Math.Min(additional, exposure.Reserve);
            int fromShelter = additional - fromReserve;
            _battle.PersonnelExposure.Set(company, new TacticalPersonnelExposure(
                exposed: desiredExposed,
                sheltered: exposure.Sheltered - fromShelter,
                reserve: exposure.Reserve - fromReserve,
                exposedDepthMeters: exposure.ExposedDepthMeters));
        }

        return signatures;
    }

    /// <summary>
    /// Politique provisoire :
    /// - une salve maximum par compagnie et par heure ;
    /// - premiere piste directement localisee permettant un tir autorise ;
    /// - en transit, priorite au contact confirme le plus tot dans l'heure,
    ///   puis a l'ordre des pistes en cas d'egalite ;
    /// - pas de tir au-dela des ressources disponibles.
    ///
    /// Aucun renseignement d'une autre compagnie
    /// n'est utilise pour choisir la cible.
    /// </summary>
    private List<TacticalFireOrder> BuildFireOrders(long hour)
    {
        var orders = new List<TacticalFireOrder>();

        foreach (var shooter in _battle.Deployments)
        {


            bool mobileParticipant =
                _settings.EnableMobileCombat
                && _battle.MobileEngagements.IsParticipant(
                    shooter, hour);

            // Une compagnie en transit ne tire que
            // si elle participe a un contact confirme.
            if (ReactionExecution?.IsMoving(shooter) == true
                && !mobileParticipant)
            {
                continue;
            }

            if (shooter.Role != CompanyDeploymentRole.FrontLine
                && !mobileParticipant)
            {
                continue;
            }


            var resources = shooter.Company.Composition!;

            int exposed =
                _battle.PersonnelExposure.Get(shooter).Exposed;

            int weapons = Math.Min(
                _settings.MaxWeaponsPerCompany,
                Math.Min(
                    exposed,
                    Math.Min(
                        resources.Manpower.Current,
                        resources.InfantryEquipment.Current)));

            // Les compagnies supprimees engagent moins
            // d'armes pendant cette heure.
            if (_settings.Suppression is not null)
            {
                weapons = _battle.Suppression.GetEffectiveWeapons(
                    shooter,
                    weapons,
                    hour,
                    _settings.Suppression);
            }

            // La cohesion reduit la capacite durable a
            // coordonner les armes encore utilisables.
            // Elle ne detruit aucun equipement physique.
            if (_settings.Cohesion is not null)
            {
                weapons = _battle.Cohesion.GetEffectiveWeapons(
                    shooter,
                    weapons,
                    _settings.Cohesion);
            }

            long availableRounds =
                resources.Ammunition.GetAvailable(
                    AmmunitionType.SmallArms);

            weapons = (int)Math.Min(
                (long)weapons,
                availableRounds / _settings.RoundsPerWeapon);

            if (weapons <= 0)
                continue;

            TacticalFireOrder? selectedOrder = null;
            double selectedContactTime = double.PositiveInfinity;
            foreach (var report in _perception.GetReports(shooter))
            {
                if (!report.IsCurrentlyObserved
                    || report.LastObservedHour != hour
                    || report.LastLocatedHour != hour
                    || report.CurrentObservationLevel
                        < TacticalKnowledgeLevel.Located)
                {
                    continue;
                }

                // Resolution interne de la piste.
                // La decision du tireur part uniquement
                // d'un rapport qu'il a effectivement recu.
                var target = _perception.ResolveCurrentTarget(
                    shooter,
                    report.TrackId,
                    hour);

                if (target is null)
                    continue;

                var mobileContact = _battle.MobileEngagements.Find(
                    shooter, target, hour);

                // Une cible ou un tireur en transit doit appartenir
                // a une paire de contact mobile confirmee.
                if ((shooter.IsInTransit || target.IsInTransit)
                    && mobileContact is null)
                {
                    continue;
                }

                double contactTime = mobileContact?.ContactTimeFraction
                    ?? double.PositiveInfinity;
                if (selectedOrder is not null && contactTime >= selectedContactTime)
                    continue;

                var decision = TacticalDirectFireEvaluator.Evaluate(
                    _battle,
                    _geometry,
                    _diplomacy,
                    _perception,
                    shooter,
                    target,
                    hour,
                    _settings.Fire);

                if (!decision.CanFire)
                    continue;

                int weaponsForThisTarget = weapons;

                if (mobileContact is not null)
                {
                    double remainingHour = Math.Clamp(
                        1.0 - mobileContact.ContactTimeFraction, 0, 1);

                    weaponsForThisTarget = (int)Math.Floor(
                        weapons * remainingHour + 1e-9);
                }

                if (weaponsForThisTarget <= 0)
                    continue;

                selectedOrder = new TacticalFireOrder(
                    shooter,
                    target,
                    _settings.Fire,
                    WeaponsCommitted: weaponsForThisTarget,
                    RoundsPerWeapon: _settings.RoundsPerWeapon);
                selectedContactTime = contactTime;

                if (!shooter.IsInTransit)
                    break;
            }

            if (selectedOrder is not null)
                orders.Add(selectedOrder);
        }

        return orders;
    }



    internal bool HasCombatCapableOpposition()
    {
        var engagement = _battle.Engagement;

        if (!engagement.IsActive)
            return false;

        return engagement.Participants
            .Where(d =>
                ReferenceEquals(d.CurrentEngagement, engagement)
                && !d.IsBroken
                && d.Country is not null)
            .Select(d => d.Country!.Id)
            .Distinct()
            .Take(2)
            .Count() == 2;
    }



    private void PublishCommandSituations(long hour)
    {
        if (_registeredCommandFronts.Count == 0)
            return;

        var snapshots =
            new List<TacticalBattalionCommandSituationSnapshot>();

        var failures =
            new List<TacticalCommandSituationCaptureFailure>();

        var byRegistration = new Dictionary<
            TacticalCommandSituationRegistration,
            TacticalCommandSituationReadResult>(
                ReferenceEqualityComparer.Instance);

        foreach (var registered in _registeredCommandFronts)
        {
            TacticalBattalionCommandSituationSnapshot?
                snapshot = null;

            TacticalCommandSituationCaptureFailure?
                failure = null;

            string divisionName =
                registered.Battalion.ParentDivision?.Name
                ?? string.Empty;

            try
            {
                snapshot = CaptureRegisteredCommandSituation(
                    registered);

                if (snapshot is null
                    || snapshot.Hour != hour
                    || snapshot.BattalionId
                        != registered.Battalion.Id
                    || snapshot.BattalionName
                        != registered.Battalion.Name
                    || snapshot.FriendlyDivisionName
                        != divisionName)
                {
                    throw new InvalidOperationException(
                        "Captured situation does not match " +
                        "its registration or hour.");
                }
            }
            catch (InvalidOperationException ex)
            {
                snapshot = null;

                failure =
                    new TacticalCommandSituationCaptureFailure(
                        registered.Battalion.Id,
                        divisionName,
                        ex.Message);
            }
            catch (ArgumentException ex)
            {
                snapshot = null;

                failure =
                    new TacticalCommandSituationCaptureFailure(
                        registered.Battalion.Id,
                        divisionName,
                        ex.Message);
            }
            catch (Exception ex) when (
                ex is not OutOfMemoryException
                && ex is not StackOverflowException
                && ex is not AccessViolationException)
            {
                snapshot = null;

                failure =
                    new TacticalCommandSituationCaptureFailure(
                        registered.Battalion.Id,
                        divisionName,
                        $"Unexpected {ex.GetType().Name}: " +
                        ex.Message);
            }

            if (snapshot is not null)
                snapshots.Add(snapshot);

            if (failure is not null)
                failures.Add(failure);

            // L'identite utilisee ici est celle de la
            // cle opaque, jamais le BattalionId.
            byRegistration.Add(
                registered.Registration,
                new TacticalCommandSituationReadResult(
                    snapshot,
                    failure));
        }

        // La publication n'a lieu qu'apres
        // le traitement de tous les enregistrements.
        var phase = new TacticalCommandSituationPhaseResult(
            hour,
            Array.AsReadOnly(snapshots.ToArray()),
            Array.AsReadOnly(failures.ToArray()));

        _lastPublishedByRegistration = byRegistration;
        LastCommandSituationPhase = phase;
    }


    /// <summary>
    /// Lit uniquement le resultat associe a la cle
    /// d'enregistrement fournie.
    ///
    /// Retourne null si aucune publication valide
    /// n'existe pour l'heure demandee.
    ///
    /// Une capture echouee est retournee avec
    /// Snapshot == null et Failure != null.
    /// </summary>
    internal TacticalCommandSituationReadResult?
        ReadCommandSituation(
            TacticalCommandSituationRegistration registration,
            long expectedHour)
    {
        ArgumentNullException.ThrowIfNull(registration);

        // Une cle d'une autre bataille, ou une cle
        // jamais enregistree, n'est pas reconnue.
        bool registeredHere = _registeredCommandFronts.Any(
            r => ReferenceEquals(
                r.Registration,
                registration));

        if (!registeredHere)
        {
            throw new InvalidOperationException(
                "Command registration does not belong " +
                "to this tactical orchestrator.");
        }

        // Interdire la lecture d'une publication
        // appartenant a une bataille terminee.
        if (!_battle.Engagement.IsActive)
            return null;

        // Interdire les rapports anciens ou futurs.
        if (LastCommandSituationPhase?.Hour != expectedHour
            || LastResult?.Hour != expectedHour)
        {
            return null;
        }

        if (!_lastPublishedByRegistration.TryGetValue(
            registration, out var result))
        {
            throw new InvalidOperationException(
                "Published command situation is missing " +
                "a registered battalion.");
        }

        return result;
    }


}
