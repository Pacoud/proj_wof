
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Composition;

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

    private long? _lastCompletedHour;

    internal TacticalBattle Battle => _battle;

    internal TacticalHourlyCombatResult? LastResult
    {
        get;
        private set;
    }

    internal TacticalHourlyCombatOrchestrator(
        TacticalBattle battle,
        TacticalGeometryMap geometry,
        TacticalPerceptionState perception,
        TacticalSignatureFrame signatures,
        DiplomacySystem diplomacy,
        TacticalHourlyCombatSettings settings)
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

        if (!ReferenceEquals(geometry.Battle, battle)
            || !ReferenceEquals(perception.Battle, battle)
            || !ReferenceEquals(signatures.Battle, battle))
        {
            throw new InvalidOperationException(
                "Tactical services must belong to the same battle.");
        }
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

        _perception.EnsureCanUpdate(hour);
    }

    /// <summary>
    /// Execute l'ensemble de la chaine pour une heure.
    /// N'avance pas lui-meme l'horloge generale.
    /// </summary>
    internal TacticalHourlyCombatResult Run(long hour)
    {
        ValidateCanRun(hour);

        // 1. Mise a jour du renseignement.
        TacticalDetectionSystem.Update(
            _battle,
            _geometry,
            _diplomacy,
            _perception,
            _signatures,
            hour,
            _settings.Detection);

        // 2. Ordres provisoires, uniquement sur
        // des pistes actuellement localisees.
        var orders = BuildFireOrders(hour);

        // 3. Execution effective, avec consommation
        // des munitions une seule fois.
        var fire = _battle.DirectFire.Execute(
            orders,
            _geometry,
            _diplomacy,
            _perception,
            hour);

        ulong phaseSeed = unchecked(
            _settings.BaseSeed
            + (ulong)hour * 0x9E3779B97F4A7C15UL);

        // 4. Contacts avec les soldats exposes.
        _battle.DirectFire.ResolvePersonnelLastPhase(
            _settings.Hit,
            _settings.Defense,
            seed: phaseSeed ^ 0xA0761D6478BD642FUL);

        // 5. Consequences humaines simulees.
        _battle.DirectFire.ResolvePersonnelEffectsLastPhase(
            _settings.Effects,
            seed: phaseSeed ^ 0xE7037ED1A0B428DBUL);

        // 6. Application aux compagnies.
        var casualties =
            _battle.DirectFire.ApplyPersonnelEffectsLastPhase();

        // 7. Synchroniser les hommes exposes restants
        // pour que l'heure suivante soit preparable.
        _battle.PersonnelExposure.ReconcileAppliedCasualties(
            casualties);

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

        _lastCompletedHour = hour;
        LastResult = result;

        return result;
    }

    /// <summary>
    /// Politique provisoire :
    /// - une salve maximum par compagnie et par heure ;
    /// - premiere piste directement localisee
    ///   permettant un tir autorise ;
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
            if (shooter.Role != CompanyDeploymentRole.FrontLine)
                continue;

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

            long availableRounds =
                resources.Ammunition.GetAvailable(
                    AmmunitionType.SmallArms);

            weapons = (int)Math.Min(
                (long)weapons,
                availableRounds / _settings.RoundsPerWeapon);

            if (weapons <= 0)
                continue;

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

                orders.Add(new TacticalFireOrder(
                    shooter,
                    target,
                    _settings.Fire,
                    WeaponsCommitted: weapons,
                    RoundsPerWeapon: _settings.RoundsPerWeapon));

                // Une seule cible pour cette compagnie.
                break;
            }
        }

        return orders;
    }
}
