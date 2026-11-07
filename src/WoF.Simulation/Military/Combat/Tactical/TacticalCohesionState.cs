
namespace WoF.Simulation.Military.Combat.Tactical;

internal readonly record struct TacticalCohesionCompanyReport(
    CompanyDeployment Company,
    double CohesionBefore,
    double LossFromCasualties,
    double LossFromSustainedFire,
    double Recovered,
    double CohesionAfter);

internal sealed record TacticalCohesionPhaseResult(
    long Hour,
    IReadOnlyList<TacticalCohesionCompanyReport> Companies);

/// <summary>
/// Cohesion tactique persistante par compagnie.
/// Source distincte des effectifs et de la suppression.
/// </summary>
internal sealed class TacticalCohesionState
{
    private readonly TacticalBattle _battle;

    private readonly Dictionary<CompanyDeployment, double>
        _values = new(ReferenceEqualityComparer.Instance);

    private long? _lastAppliedHour;

    internal TacticalCohesionState(TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    internal double Get(CompanyDeployment company)
    {
        ValidateCompany(company);

        return _values.TryGetValue(company, out double value)
            ? value
            : 100.0;
    }

    /// <summary>
    /// Reduit les armes engagees, sans detruire
    /// les armes physiques possedees.
    /// </summary>
    internal int GetEffectiveWeapons(
        CompanyDeployment company,
        int availableWeapons,
        TacticalCohesionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (availableWeapons < 0)
            throw new ArgumentOutOfRangeException(
                nameof(availableWeapons));

        double cohesion = Get(company);

        double factor = Math.Clamp(
            1.0 - profile.MaxFireReductionFraction
                * (1.0 - cohesion / 100.0),
            0.0, 1.0);

        return Math.Clamp(
            (int)Math.Floor(availableWeapons * factor),
            0,
            availableWeapons);
    }

    /// <summary>
    /// Applique la degradation et la recuperation
    /// pour une heure complete.
    ///
    /// Les pertes sont deja appliquees aux PersonnelPool.
    /// La suppression est deja resolue pour cette heure.
    /// </summary>
    internal TacticalCohesionPhaseResult ApplyHour(
        long hour,
        TacticalCasualtyPhaseResult casualties,
        TacticalSuppressionPhaseResult suppression,
        TacticalSuppressionProfile suppressionProfile,
        TacticalCohesionProfile cohesionProfile)
    {
        ArgumentNullException.ThrowIfNull(casualties);
        ArgumentNullException.ThrowIfNull(suppression);
        ArgumentNullException.ThrowIfNull(suppressionProfile);
        ArgumentNullException.ThrowIfNull(cohesionProfile);

        if (hour < _battle.Engagement.StartHour)
            throw new ArgumentOutOfRangeException(nameof(hour));

        if (_lastAppliedHour is long previous
            && hour <= previous)
        {
            throw new InvalidOperationException(
                "Cohesion hour already applied.");
        }

        if (casualties.Hour != hour
            || suppression.Hour != hour)
        {
            throw new InvalidOperationException(
                "Cohesion inputs belong to different hours.");
        }

        var losses = new Dictionary<
            CompanyDeployment,
            TacticalCompanyCasualtyApplication>(
                ReferenceEqualityComparer.Instance);

        foreach (var report in casualties.Companies)
        {
            ValidateCompany(report.Target);

            int total = report.Casualties.Total;

            if (report.AvailableBefore < 0
                || report.AvailableAfter < 0
                || total < 0
                || total > report.AvailableBefore
                || report.AvailableBefore - total
                    != report.AvailableAfter
                || report.Target.Company.Composition!
                    .Manpower.Current != report.AvailableAfter)
            {
                throw new InvalidOperationException(
                    "Invalid applied casualty report.");
            }

            if (!losses.TryAdd(report.Target, report))
            {
                throw new InvalidOperationException(
                    "Duplicate casualty target.");
            }
        }

        var pressure = new Dictionary<
            CompanyDeployment,
            TacticalSuppressionTargetReport>(
                ReferenceEqualityComparer.Instance);

        foreach (var report in suppression.Targets)
        {
            ValidateCompany(report.Target);

            if (!double.IsFinite(report.PressureBefore)
                || report.PressureBefore < 0
                || report.PressureBefore > 100
                || !double.IsFinite(report.PressureAfter)
                || report.PressureAfter < 0
                || report.PressureAfter > 100
                || !double.IsFinite(
                    report.ExpectedThreateningRounds)
                || report.ExpectedThreateningRounds < 0)
            {
                throw new InvalidOperationException(
                    "Invalid suppression report.");
            }

            if (!pressure.TryAdd(report.Target, report))
            {
                throw new InvalidOperationException(
                    "Duplicate suppression target.");
            }
        }

        var next = new Dictionary<CompanyDeployment, double>(
            ReferenceEqualityComparer.Instance);

        var reports = new List<TacticalCohesionCompanyReport>(
            _battle.Deployments.Count);

        // Calculer tous les nouveaux etats avant mutation.
        foreach (var company in _battle.Deployments)
        {
            double before = Get(company);

            losses.TryGetValue(company, out var casualty);
            bool hasIncoming = 
                pressure.TryGetValue(company, out var incoming);

            int lost = casualty?.Casualties.Total ?? 0;

            // Pertes relatives aux hommes presents
            // au debut de la phase, pas a l'effectif
            // autorise sur le papier.
            double casualtyPercent = lost == 0
                ? 0.0
                : 100.0 * lost
                    / casualty!.AvailableBefore;

            double rawCasualtyLoss =
                casualtyPercent
                * cohesionProfile.LossPerCasualtyPercent;

            bool underFire =
                hasIncoming &&
                incoming.ExpectedThreateningRounds > 0;

            // La premiere salve ne provoque pas
            // automatiquement une perte de cohesion
            // liee a la suppression.
            // On regarde la pression preexistante.
            double previousPressure =
                hasIncoming ? incoming.PressureBefore : 0;

            double rawFireLoss = underFire
                ? Math.Max(
                    0,
                    previousPressure
                        - cohesionProfile
                            .SustainedSuppressionThreshold)
                  * cohesionProfile
                      .LossPerExcessPressurePoint
                : 0;

            double appliedCasualtyLoss =
                Math.Min(before, rawCasualtyLoss);

            double afterCasualties =
                before - appliedCasualtyLoss;

            double appliedFireLoss =
                Math.Min(afterCasualties, rawFireLoss);

            double afterDamage =
                afterCasualties - appliedFireLoss;

            // Suppression restante apres la resolution
            // de l'heure courante (y compris recuperation).
            double currentPressure =
                _battle.Suppression.GetProjectedPressure(
                    company, hour, suppressionProfile);

            bool canRecover =
                lost == 0
                && !underFire
                && currentPressure
                    <= cohesionProfile
                        .RecoverySuppressionCeiling;

            double recovered = canRecover
                ? Math.Min(
                    100.0 - afterDamage,
                    cohesionProfile.RecoveryPerQuietHour)
                : 0.0;

            double after = Math.Clamp(
                afterDamage + recovered,
                0.0, 100.0);

            next.Add(company, after);

            reports.Add(new TacticalCohesionCompanyReport(
                company,
                before,
                appliedCasualtyLoss,
                appliedFireLoss,
                recovered,
                after));
        }

        // Publication seulement apres validation
        // de tous les rapports et calculs.
        _values.Clear();

        foreach (var pair in next)
            _values.Add(pair.Key, pair.Value);

        _lastAppliedHour = hour;

        return new TacticalCohesionPhaseResult(
            hour,
            Array.AsReadOnly(reports.ToArray()));
    }

    private void ValidateCompany(CompanyDeployment company)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (!_battle.ContainsDeployment(company))
        {
            throw new InvalidOperationException(
                "Company belongs to another tactical battle.");
        }
    }
}
