
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
