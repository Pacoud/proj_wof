
namespace WoF.Simulation.Military.Combat.Tactical;

internal readonly record struct TacticalSuppressionTargetReport(
    CompanyDeployment Target,
    double ExpectedThreateningRounds,
    double PressureBefore,
    double PressureAdded,
    double PressureAfter);

internal sealed record TacticalSuppressionPhaseResult(
    long Hour,
    IReadOnlyList<TacticalSuppressionTargetReport> Targets);

/// <summary>
/// Suppression persistante par compagnie.
///
/// Aucun stock de soldats supplementaire.
/// Aucune perte humaine ou materielle.
/// </summary>
internal sealed class TacticalSuppressionState
{
    private readonly TacticalBattle _battle;

    private readonly Dictionary<CompanyDeployment, double>
        _pressure = new(ReferenceEqualityComparer.Instance);

    private long? _lastAppliedHour;

    internal TacticalSuppressionState(TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    internal double GetProjectedPressure(
        CompanyDeployment deployment,
        long hour,
        TacticalSuppressionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateDeployment(deployment);

        if (hour < _battle.Engagement.StartHour)
            throw new ArgumentOutOfRangeException(nameof(hour));

        if (_lastAppliedHour is long previous && hour < previous)
            throw new InvalidOperationException(
                "Suppression time cannot move backwards.");

        double current = _pressure.TryGetValue(
            deployment, out double stored) ? stored : 0;

        if (_lastAppliedHour is not long last)
            return current;

        double recovered = (hour - last) * profile.RecoveryPerHour;

        return Math.Max(0.0, current - recovered);
    }

    /// <summary>
    /// Limite les armes engagees en fonction
    /// de la suppression avant les tirs de cette heure.
    /// </summary>
    internal int GetEffectiveWeapons(
        CompanyDeployment deployment,
        int availableWeapons,
        long hour,
        TacticalSuppressionProfile profile)
    {
        if (availableWeapons < 0)
            throw new ArgumentOutOfRangeException(
                nameof(availableWeapons));

        double pressure =
            GetProjectedPressure(deployment, hour, profile);

        double fraction = Math.Clamp(
            1.0
            - profile.MaxFireReductionFraction * pressure / 100.0,
            0.0,
            1.0);

        return Math.Clamp(
            (int)Math.Ceiling(availableWeapons * fraction),
            0,
            availableWeapons);
    }

    /// <summary>
    /// Applique une seule fois la pression d'une heure.
    /// Toutes les salves visant une compagnie
    /// contribuent au meme total.
    /// </summary>
    internal TacticalSuppressionPhaseResult ApplyHour(
        long hour,
        IReadOnlyList<TacticalSuppressionThreat> threats,
        TacticalSuppressionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(threats);
        ArgumentNullException.ThrowIfNull(profile);

        if (hour < _battle.Engagement.StartHour)
            throw new ArgumentOutOfRangeException(nameof(hour));

        if (_lastAppliedHour is long previous
            && hour <= previous)
        {
            throw new InvalidOperationException(
                "Suppression phase already applied.");
        }

        var threateningRounds =
            new Dictionary<CompanyDeployment, double>(
                ReferenceEqualityComparer.Instance);

        // Validation et regroupement avant mutation.
        foreach (var threat in threats)
        {
            ValidateDeployment(threat.Target);

            if (threat.RoundsExpended <= 0
                || !double.IsFinite(threat.DangerZoneProbability)
                || threat.DangerZoneProbability < 0
                || threat.DangerZoneProbability > 1
                || !double.IsFinite(threat.ExpectedThreateningRounds)
                || threat.ExpectedThreateningRounds < 0
                || threat.ExpectedThreateningRounds
                    > threat.RoundsExpended)
            {
                throw new InvalidOperationException(
                    "Invalid suppression threat.");
            }

            threateningRounds.TryGetValue(
                threat.Target, out double previousRounds);

            threateningRounds[threat.Target] =
                previousRounds + threat.ExpectedThreateningRounds;
        }

        var next = new Dictionary<CompanyDeployment, double>(
            ReferenceEqualityComparer.Instance);

        var reports = new List<TacticalSuppressionTargetReport>(
            threateningRounds.Count);

        // Calcul complet avant publication.
        foreach (var deployment in _battle.Deployments)
        {
            double before =
                GetProjectedPressure(deployment, hour, profile);

            double threatening = threateningRounds.TryGetValue(
                deployment, out double rounds) ? rounds : 0;

            double after = Math.Clamp(
                before
                + threatening * profile.PressurePerThreateningRound,
                0.0,
                100.0);

            next.Add(deployment, after);

            if (threateningRounds.ContainsKey(deployment))
            {
                reports.Add(new TacticalSuppressionTargetReport(
                    Target: deployment,
                    ExpectedThreateningRounds: threatening,
                    PressureBefore: before,
                    PressureAdded: after - before,
                    PressureAfter: after));
            }
        }

        _pressure.Clear();

        foreach (var pair in next)
            _pressure.Add(pair.Key, pair.Value);

        _lastAppliedHour = hour;

        return new TacticalSuppressionPhaseResult(
            hour,
            Array.AsReadOnly(reports.ToArray()));
    }

    private void ValidateDeployment(
        CompanyDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        if (!_battle.ContainsDeployment(deployment))
        {
            throw new InvalidOperationException(
                "Suppression target belongs to another battle.");
        }
    }
}
