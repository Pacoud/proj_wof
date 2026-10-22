
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Repartition agregee des hommes d'une compagnie.
/// Aucun objet Soldier individuel n'est cree.
/// </summary>
internal readonly record struct TacticalPersonnelExposure
{
    public int Exposed { get; }
    public int Sheltered { get; }
    public int Reserve { get; }

    // Profondeur de la zone occupee par les
    // combattants exposes, en metres.
    public double ExposedDepthMeters { get; }

    public int Total => checked(
        Exposed + Sheltered + Reserve);

    internal TacticalPersonnelExposure(
        int exposed,
        int sheltered,
        int reserve,
        double exposedDepthMeters)
    {
        if (exposed < 0 || sheltered < 0 || reserve < 0)
            throw new ArgumentOutOfRangeException(
                nameof(exposed),
                "Personnel counts must be nonnegative.");

        if (!double.IsFinite(exposedDepthMeters)
            || exposedDepthMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exposedDepthMeters));
        }

        // Verifie egalement les depassements int.
        _ = checked(exposed + sheltered + reserve);

        Exposed = exposed;
        Sheltered = sheltered;
        Reserve = reserve;
        ExposedDepthMeters = exposedDepthMeters;
    }
}

/// <summary>
/// Repartition des hommes reellement affectes
/// aux compagnies de cette bataille.
/// </summary>
internal sealed class TacticalPersonnelExposureState
{
    private readonly TacticalBattle _battle;

    private readonly Dictionary<
        CompanyDeployment,
        TacticalPersonnelExposure> _states =
            new(ReferenceEqualityComparer.Instance);

    internal TacticalPersonnelExposureState(
        TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));

    }

    internal void Set(
        CompanyDeployment deployment,
        TacticalPersonnelExposure exposure)
    {
        Validate(deployment);

        int available =
            deployment.Company.Composition!.Manpower.Current;

        if (exposure.Total != available)
        {
            throw new InvalidOperationException(
                "Personnel distribution must match available manpower.");
        }

        _states[deployment] = exposure;
    }

    internal TacticalPersonnelExposure Get(
        CompanyDeployment deployment)
    {
        Validate(deployment);

        int available =
            deployment.Company.Composition!.Manpower.Current;

        if (_states.TryGetValue(deployment, out var exposure))
        {
            // Une modification physique des effectifs
            // oblige a redefinir leur repartition.
            if (exposure.Total != available)
            {
                throw new InvalidOperationException(
                    "Personnel exposure is outdated.");
            }

            return exposure;
        }

        // Choix conservateur : sans affectation
        // explicite, aucun homme n'est considere
        // expose au tir direct.
        return new TacticalPersonnelExposure(
            exposed: 0,
            sheltered: 0,
            reserve: available,
            exposedDepthMeters: 20);
    }


    /// <summary>
    /// Ajuste les repartitions explicites apres une
    /// phase de pertes tactiques deja appliquee.
    ///
    /// Les victimes proviennent des hommes exposes.
    /// Les repartitions implicites, sans affectation
    /// explicite, restent gerees par Get().
    /// </summary>
    internal void ReconcileAppliedCasualties(
        TacticalCasualtyPhaseResult phase)
    {
        ArgumentNullException.ThrowIfNull(phase);

        var adjustments = new List<(
            CompanyDeployment Target,
            TacticalPersonnelExposure Updated)>();

        // Valider toutes les repartitions avant
        // de commencer leur modification.
        foreach (var report in phase.Companies)
        {
            Validate(report.Target);

            if (!_states.TryGetValue(
                report.Target, out var previous))
            {
                continue;
            }

            int lost = report.Casualties.Total;

            if (previous.Total != report.AvailableBefore
                || lost < 0
                || lost > previous.Exposed
                || report.AvailableAfter
                    != report.AvailableBefore - lost
                || report.Target.Company.Composition!
                    .Manpower.Current != report.AvailableAfter)
            {
                throw new InvalidOperationException(
                    "Cannot reconcile tactical personnel exposure.");
            }

            adjustments.Add((
                report.Target,
                new TacticalPersonnelExposure(
                    exposed: previous.Exposed - lost,
                    sheltered: previous.Sheltered,
                    reserve: previous.Reserve,
                    exposedDepthMeters:
                        previous.ExposedDepthMeters)));
        }

        foreach (var adjustment in adjustments)
            _states[adjustment.Target] = adjustment.Updated;
    }


    private void Validate(CompanyDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        if (!_battle.ContainsDeployment(deployment))
        {
            throw new InvalidOperationException(
                "Deployment belongs to another battle.");
        }
    }
}
