
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Suit l'etat des secteurs prioritaires par bataillon.
/// Historique borne : un etat courant par bataillon.
/// </summary>
internal sealed class TacticalBattalionReactionState
{
    private readonly TacticalBattle _battle;

    private readonly record struct Tracking(
        BattleSector? Sector,
        TacticalBattalionFrontState State,
        int CriticalHours,
        int RecoveryHours,
        TacticalBattalionReactionType? LastRequest,
        long Hour);

    private readonly Dictionary<Battalion, Tracking>
        _tracking = new(ReferenceEqualityComparer.Instance);

    private long? _lastAppliedHour;

    internal TacticalBattalionReactionPhaseResult? LastPhase
    {
        get;
        private set;
    }

    internal TacticalBattalionReactionState(TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    internal void EnsureCanApply(
        long hour,
        TacticalBattalionAssessmentProfile assessmentProfile,
        TacticalBattalionReactionProfile reactionProfile)
    {
        ArgumentNullException.ThrowIfNull(assessmentProfile);
        ArgumentNullException.ThrowIfNull(reactionProfile);

        reactionProfile.ValidateFor(assessmentProfile);

        if (!_battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "The tactical engagement has ended.");

        if (hour < _battle.Engagement.StartHour)
            throw new ArgumentOutOfRangeException(nameof(hour));

        if (_lastAppliedHour is long previous
            && hour <= previous)
        {
            throw new InvalidOperationException(
                "Battalion reaction phase already applied.");
        }
    }

    internal TacticalBattalionReactionPhaseResult ApplyHour(
        long hour,
        IReadOnlyList<TacticalBattalionAssessment> assessments,
        TacticalBattalionAssessmentProfile assessmentProfile,
        TacticalBattalionReactionProfile reactionProfile)
    {
        ArgumentNullException.ThrowIfNull(assessments);

        EnsureCanApply(hour, assessmentProfile, reactionProfile);

        var represented = _battle.Deployments
            .Select(d => d.Company.ParentBattalion
                ?? throw new InvalidOperationException(
                    "Deployed company has no battalion."))
            .ToHashSet<Battalion>(ReferenceEqualityComparer.Instance);

        if (assessments.Count != represented.Count)
            throw new InvalidOperationException(
                "Incomplete battalion assessment phase.");

        var seen = new HashSet<Battalion>(
            ReferenceEqualityComparer.Instance);

        // Preparer integralement le nouvel etat
        // sans modifier la bataille.
        var next = new Dictionary<Battalion, Tracking>(
            _tracking, ReferenceEqualityComparer.Instance);

        var reports = new List<TacticalBattalionReactionReport>();
        var requests = new List<TacticalBattalionReactionRequest>();

        foreach (var assessment in assessments)
        {
            ValidateAssessment(
                assessment, represented, seen, assessmentProfile);

            Battalion battalion = assessment.Battalion;
            BattleSector? sector = assessment.PrioritySector;

            bool sameMission =
                _tracking.TryGetValue(battalion, out var previous)
                && ReferenceEquals(previous.Sector, sector);

            if (!sameMission)
            {
                previous = new Tracking(
                    sector,
                    TacticalBattalionFrontState.Unassigned,
                    0, 0, null, hour - 1);
            }

            bool consecutive =
                sameMission && previous.Hour == hour - 1;

            int criticalHours = 0;
            int recoveryHours = 0;

            TacticalBattalionFrontState state;

            if (sector is null)
            {
                state = TacticalBattalionFrontState.Unassigned;
            }
            else
            {
                double readiness =
                    assessment.PrioritySectorReadiness!.Value;

                double risk = assessmentProfile.RiskThreshold;
                double hold = assessmentProfile.HoldThreshold;
                double recoveryTarget =
                    hold + reactionProfile.RecoveryMargin;

                if (previous.State ==
                    TacticalBattalionFrontState.RuptureConfirmed)
                {
                    // Une rupture confirmee persiste jusqu'a
                    // plusieurs heures de retablissement.
                    if (readiness >= recoveryTarget)
                    {
                        recoveryHours = consecutive
                            ? checked(previous.RecoveryHours + 1)
                            : 1;
                    }

                    state = recoveryHours >=
                        reactionProfile.RecoveryConfirmationHours
                        ? TacticalBattalionFrontState.Holding
                        : TacticalBattalionFrontState.RuptureConfirmed;
                }
                else if (readiness < risk)
                {
                    criticalHours = consecutive
                        ? checked(previous.CriticalHours + 1)
                        : 1;

                    state = criticalHours >=
                        reactionProfile.CriticalConfirmationHours
                        ? TacticalBattalionFrontState.RuptureConfirmed
                        : TacticalBattalionFrontState.Threatened;
                }
                else if (previous.State ==
                         TacticalBattalionFrontState.Threatened)
                {
                    // Hysteresis : une position menacee
                    // ne redevient stable qu'au-dessus
                    // du seuil majore.
                    state = readiness >= recoveryTarget
                        ? TacticalBattalionFrontState.Holding
                        : TacticalBattalionFrontState.Threatened;
                }
                else
                {
                    state = readiness >= hold
                        ? TacticalBattalionFrontState.Holding
                        : TacticalBattalionFrontState.Threatened;
                }
            }

            var deployments =
                _battle.GetBattalionDeployments(battalion);

            var frontline = deployments
                .Where(d =>
                    sector is not null
                    && ReferenceEquals(d.Sector, sector)
                    && d.Role == CompanyDeploymentRole.FrontLine)
                .ToArray();

            var reserves = deployments
                .Where(d =>
                    d.Role == CompanyDeploymentRole.Reserve
                    && d.Company.Composition!.Manpower.Current > 0)
                .ToArray();

            TacticalBattalionReactionType? desired = state switch
            {
                TacticalBattalionFrontState.Threatened =>
                    TacticalBattalionReactionType.Reorganize,

                TacticalBattalionFrontState.RuptureConfirmed =>
                    reserves.Length > 0
                        ? TacticalBattalionReactionType.Reinforce
                        : TacticalBattalionReactionType.RequestWithdrawal,

                _ => null
            };

            TacticalBattalionReactionRequest? newRequest = null;

            // Une nouvelle demande uniquement si l'etat
            // ou le type de reaction change.
            if (sector is not null
                && desired is TacticalBattalionReactionType type
                && (!sameMission
                    || previous.State != state
                    || previous.LastRequest != type))
            {
                newRequest = new TacticalBattalionReactionRequest(
                    hour,
                    battalion,
                    sector,
                    type,
                    Array.AsReadOnly(frontline),
                    Array.AsReadOnly(reserves));

                requests.Add(newRequest);
            }

            next[battalion] = new Tracking(
                sector,
                state,
                criticalHours,
                recoveryHours,
                desired,
                hour);

            reports.Add(new TacticalBattalionReactionReport(
                battalion,
                sector,
                previous.State,
                state,
                assessment.PrioritySectorReadiness,
                criticalHours,
                recoveryHours,
                newRequest));
        }

        // Publication seulement apres toutes les validations.
        _tracking.Clear();

        foreach (var pair in next)
            _tracking.Add(pair.Key, pair.Value);

        var phase = new TacticalBattalionReactionPhaseResult(
            hour,
            Array.AsReadOnly(reports.ToArray()),
            Array.AsReadOnly(requests.ToArray()));

        _lastAppliedHour = hour;
        LastPhase = phase;

        return phase;
    }

    private void ValidateAssessment(
        TacticalBattalionAssessment assessment,
        HashSet<Battalion> represented,
        HashSet<Battalion> seen,
        TacticalBattalionAssessmentProfile profile)
    {
        if (assessment is null
            || assessment.Battalion is null
            || !represented.Contains(assessment.Battalion)
            || !seen.Add(assessment.Battalion))
        {
            throw new InvalidOperationException(
                "Foreign or duplicated battalion assessment.");
        }

        var battalion = assessment.Battalion;
        var personnel = battalion.GetResourceTotals().Personnel;

        if (assessment.CompanyCount != battalion.Companies.Count
            || assessment.AuthorizedPersonnel != personnel.Authorized
            || assessment.AvailablePersonnel != personnel.Available)
        {
            throw new InvalidOperationException(
                "Battalion assessment resources are inconsistent.");
        }

        if (!double.IsFinite(assessment.Cohesion)
            || assessment.Cohesion < 0
            || assessment.Cohesion > 100
            || !double.IsFinite(assessment.CombatReadiness)
            || assessment.CombatReadiness < 0
            || assessment.CombatReadiness > 100)
        {
            throw new InvalidOperationException(
                "Invalid battalion cohesion or readiness.");
        }

        if (assessment.PrioritySector is null)
        {
            if (assessment.PrioritySectorReadiness is not null
                || assessment.MissionStatus !=
                    TacticalBattalionMissionStatus.Unassigned)
            {
                throw new InvalidOperationException(
                    "Unassigned mission has an invalid status.");
            }

            return;
        }

        if (!_battle.Battlefield.ContainsSector(
                assessment.PrioritySector)
            || assessment.PrioritySectorReadiness is not double value
            || !double.IsFinite(value)
            || value < 0 || value > 100)
        {
            throw new InvalidOperationException(
                "Invalid priority sector readiness.");
        }

        var expected = value >= profile.HoldThreshold
            ? TacticalBattalionMissionStatus.Capable
            : value >= profile.RiskThreshold
                ? TacticalBattalionMissionStatus.AtRisk
                : TacticalBattalionMissionStatus.UnableToHold;

        if (assessment.MissionStatus != expected)
        {
            throw new InvalidOperationException(
                "Assessment mission status is inconsistent.");
        }
    }
}
