
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Contacts de projectiles avec des silhouettes humaines.
/// Plusieurs contacts peuvent concerner la meme personne.
/// </summary>
internal readonly record struct TacticalPersonnelContactEvent(
    long Hour,
    CompanyDeployment Shooter,
    CompanyDeployment RealTarget,
    int TargetTrackId,
    long RoundsExpended,
    int ExposedPersonnel,
    double ProbabilityPerRound,
    long ContactEvents);

/// <summary>
/// Bilan agrege pour UNE compagnie cible.
/// ExpectedDistinctPersonnel est statistique :
/// ce n'est pas un nombre confirme de victimes.
/// </summary>
internal readonly record struct TacticalPersonnelTargetSummary(
    CompanyDeployment Target,
    int ExposedPersonnel,
    long ContactEvents,
    double ExpectedDistinctPersonnel);

internal sealed record TacticalPersonnelContactPhaseResult(
    long Hour,
    ulong Seed,
    IReadOnlyList<TacticalPersonnelContactEvent> Events,
    IReadOnlyList<TacticalPersonnelTargetSummary> Targets);

/// <summary>
/// Modele de contacts avec une population uniformement
/// distribuee dans une emprise rectangulaire.
///
/// Aucun objet par soldat ou projectile.
/// Aucune application de pertes.
/// </summary>
internal static class TacticalPersonnelContactResolver
{
    internal static TacticalPersonnelContactEvent Resolve(
        TacticalFireSnapshot shot,
        TacticalHitProfile hitProfile,
        TacticalDefenseProfile defenseProfile,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(hitProfile);
        ArgumentNullException.ThrowIfNull(defenseProfile);

        var fire = shot.FireEvent
            ?? throw new ArgumentException(
                "Missing fire event.", nameof(shot));

        if (fire.RoundsExpended <= 0
            || shot.RealTarget is null
            || !double.IsFinite(shot.TargetFrontageMeters)
            || shot.TargetFrontageMeters <= 0
            || !double.IsFinite(shot.TargetFacingDegrees)
            || shot.TargetFacingDegrees < 0
            || shot.TargetFacingDegrees >= 360)
        {
            throw new ArgumentException(
                "Invalid population fire snapshot.",
                nameof(shot));
        }

        var population = shot.TargetPersonnel;
        int exposed = population.Exposed;

        if (population.Total < 0
            || population.ExposedDepthMeters <= 0)
        {
            throw new InvalidOperationException(
                "Invalid population distribution.");
        }

        double probability = 0;

        if (exposed > 0)
        {
            double range = shot.ShooterPoint
                .SpatialDistanceTo(shot.ReportedTargetPoint);

            double distanceDispersion =
                hitProfile.DispersionAt100Meters
                * (range / 100.0);

            // Seule la dispersion physique est utilisee.
            // L'erreur de renseignement s'exprime deja
            // par l'ecart entre le point vise et la cible.
            double sigma = double.Hypot(
                hitProfile.MinimumDispersionMeters,
                distanceDispersion);

            if (!double.IsFinite(sigma) || sigma <= 0)
            {
                throw new InvalidOperationException(
                    "Invalid firing dispersion.");
            }

            double angle =
                shot.TargetFacingDegrees * Math.PI / 180.0;

            // Vecteur lateral de la compagnie.
            double lx = Math.Cos(angle);
            double ly = -Math.Sin(angle);

            // Vecteur vers l'avant.
            double fx = Math.Sin(angle);
            double fy = Math.Cos(angle);

            // Point vise relatif au centre reel.
            double dx = shot.ReportedTargetPoint.XMeters
                - shot.RealTargetPoint.XMeters;

            double dy = shot.ReportedTargetPoint.YMeters
                - shot.RealTargetPoint.YMeters;

            double lateralOffset = dx * lx + dy * ly;
            double forwardOffset = dx * fx + dy * fy;

            double halfWidth = shot.TargetFrontageMeters / 2.0;
            double halfDepth =
                population.ExposedDepthMeters / 2.0;

            // Probabilite que le projectile tombe
            // dans le rectangle de la compagnie.
            double lateralProbability = NormalInterval(
                -halfWidth, halfWidth, lateralOffset, sigma);

            double depthProbability = NormalInterval(
                -halfDepth, halfDepth, forwardOffset, sigma);

            double zoneProbability =
                lateralProbability * depthProbability;

            // Approximation d'occupation spatiale :
            // silhouettes reparties uniformement,
            // avec possible chevauchement.
            double area =
                shot.TargetFrontageMeters
                * population.ExposedDepthMeters;

            double silhouetteArea =
                Math.PI
                * hitProfile.EffectiveTargetRadiusMeters
                * hitProfile.EffectiveTargetRadiusMeters;

            double density =
                exposed * silhouetteArea / area;

            double humanOccupancy = -double.ExpM1(-density);

            double exposureFactor =
                defenseProfile.GetExposureFactor(
                    shot.TargetDefense.Stance);

            double interception =
                defenseProfile.GetInterceptionFraction(
                    shot.TargetDefense.Cover);

            probability = Math.Clamp(
                zoneProbability
                * humanOccupancy
                * exposureFactor
                * (1.0 - interception),
                0.0,
                1.0);
        }

        long contacts = TacticalImpactResolver.SampleBinomial(
            fire.RoundsExpended,
            probability,
            seed);

        return new TacticalPersonnelContactEvent(
            Hour: fire.Hour,
            Shooter: fire.Shooter,
            RealTarget: shot.RealTarget,
            TargetTrackId: fire.TargetTrackId,
            RoundsExpended: fire.RoundsExpended,
            ExposedPersonnel: exposed,
            ProbabilityPerRound: probability,
            ContactEvents: contacts);
    }

    /// <summary>
    /// Esperance du nombre de personnes distinctes
    /// atteintes parmi N personnes uniformement
    /// selectionnees par K contacts.
    ///
    /// On regroupe d'abord toutes les salves d'une
    /// phase visant la meme compagnie.
    /// </summary>
    internal static double ExpectedDistinct(
        int exposed,
        long contacts)
    {
        if (exposed < 0 || contacts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exposed));
        }

        if (exposed == 0 || contacts == 0)
            return 0;

        if (exposed == 1)
            return 1;

        double result = exposed * (
            1.0 - Math.Exp(
                contacts * Math.Log(1.0 - 1.0 / exposed)));

        return Math.Clamp(
            result,
            0.0,
            Math.Min((double)exposed, (double)contacts));
    }

    private static double NormalInterval(
        double lower,
        double upper,
        double mean,
        double sigma)
    {
        double a = (lower - mean) / sigma;
        double b = (upper - mean) / sigma;

        return Math.Clamp(
            NormalCdf(b) - NormalCdf(a),
            0.0,
            1.0);
    }

    /// <summary>
    /// Approximation numerique de la fonction
    /// de repartition normale standard.
    /// </summary>
    private static double NormalCdf(double x)
    {
        double absolute = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.2316419 * absolute);

        double polynomial = t * (
            0.319381530 + t * (
            -0.356563782 + t * (
             1.781477937 + t * (
            -1.821255978 + t * 1.330274429))));

        double tail =
            Math.Exp(-0.5 * absolute * absolute)
            / Math.Sqrt(2.0 * Math.PI)
            * polynomial;

        return x >= 0
            ? 1.0 - tail
            : tail;
    }
}
