
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Resultat interne d'une salve resolue.
/// Impacts ne signifie pas victimes distinctes.
/// </summary>
internal readonly record struct TacticalImpactEvent(
    long Hour,
    CompanyDeployment Shooter,
    int TargetTrackId,
    long RoundsExpended,
    double RealAimOffsetMeters,
    double ImpactProbabilityPerRound,
    long Impacts);

internal sealed record TacticalImpactPhaseResult(
    long Hour,
    ulong Seed,
    IReadOnlyList<TacticalImpactEvent> Events);

/// <summary>
/// Resolution statistique des impacts.
/// Ne modifie pas les ressources militaires.
/// </summary>
internal static class TacticalImpactResolver
{
    internal static TacticalImpactEvent Resolve(
        TacticalFireSnapshot snapshot,
        TacticalHitProfile hitProfile,
        TacticalDefenseProfile defenseProfile,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(hitProfile);
        ArgumentNullException.ThrowIfNull(defenseProfile);

        var fire = snapshot.FireEvent
            ?? throw new ArgumentException(
                "Missing fire event.", nameof(snapshot));

        if (fire.RoundsExpended <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshot));
        }

        // Portee vers le point connu du tireur.
        double range = snapshot.ShooterPoint
            .SpatialDistanceTo(snapshot.ReportedTargetPoint);

        double rangeDispersion =
            hitProfile.DispersionAt100Meters * range / 100.0;

        // Dispersion physique du tir.
        // Ne pas ajouter l'incertitude de localisation :
        // nous connaissons ici l'erreur reelle observee.
        double dispersion = double.Hypot(
            hitProfile.MinimumDispersionMeters,
            rangeDispersion);

        if (!double.IsFinite(dispersion)
            || dispersion <= 0)
        {
            throw new InvalidOperationException(
                "Invalid physical shot dispersion.");
        }

        // Ecart horizontal entre point vise et
        // centre reel de la compagnie cible.
        double aimOffset = snapshot.ReportedTargetPoint
            .HorizontalDistanceTo(snapshot.RealTargetPoint);

        // Approximation d'une cible circulaire :
        // probabilite si parfaitement centree.
        double radiusRatio =
            hitProfile.EffectiveTargetRadiusMeters / dispersion;

        double centeredProbability =
            1.0 - Math.Exp(
                -0.5 * radiusRatio * radiusRatio);

        // Approximation de decalage pour une
        // cible excentree. Ce n'est pas encore
        // l'integrale exacte d'une gaussienne
        // sur un disque decale.
        double offsetRatio = aimOffset / dispersion;
        double offsetFactor = Math.Exp(
            -0.5 * offsetRatio * offsetRatio);

        double exposure = defenseProfile.GetExposureFactor(
            snapshot.TargetDefense.Stance);

        double interception =
            defenseProfile.GetInterceptionFraction(
                snapshot.TargetDefense.Cover);

        double probability = Math.Clamp(
            centeredProbability
            * offsetFactor
            * exposure
            * (1.0 - interception),
            0.0,
            1.0);

        long impacts = SampleBinomial(
            fire.RoundsExpended,
            probability,
            seed);

        return new TacticalImpactEvent(
            Hour: fire.Hour,
            Shooter: fire.Shooter,
            TargetTrackId: fire.TargetTrackId,
            RoundsExpended: fire.RoundsExpended,
            RealAimOffsetMeters: aimOffset,
            ImpactProbabilityPerRound: probability,
            Impacts: impacts);
    }

    /// <summary>
    /// Tirage binomial sans iterer sur chaque cartouche.
    /// Exact par sauts pour les faibles esperances,
    /// approximation normale pour les grandes.
    /// </summary>
    private static long SampleBinomial(
        long count,
        double probability,
        ulong seed)
    {
        if (count <= 0)
            return 0;

        if (probability <= 0)
            return 0;

        if (probability >= 1)
            return count;

        bool invert = probability > 0.5;
        double p = invert
            ? 1.0 - probability
            : probability;

        long successes;
        double expected = count * p;

        if (expected >= 25 && count * (1.0 - p) >= 25)
        {
            // Approximation normale pour les grands
            // nombres de tirs. Cout constant.
            double u1 = NextUniform(ref seed);
            double u2 = NextUniform(ref seed);

            double normal = Math.Sqrt(-2.0 * Math.Log(u1))
                * Math.Cos(2.0 * Math.PI * u2);

            double deviation =
                Math.Sqrt(count * p * (1.0 - p));

            double sample = expected + deviation * normal;

            if (sample <= 0)
                successes = 0;
            else if (sample >= count)
                successes = count;
            else
                successes = (long)Math.Floor(sample + 0.5);
        }
        else
        {
            // Echantillonnage par nombre d'echecs
            // entre deux impacts.
            double logFailure = Math.Log(1.0 - p);

            if (logFailure == 0.0)
            {
                double treshold = Math.Exp(-expected);
                double product = 1.0;
                long draws = 0;
                do 
                {
                    draws ++;
                    product *= NextUniform(ref seed);
                }
                while (product > treshold);

                successes = draws - 1;
            }
            else
            {
                successes = 0;
                long remaining = count;

                while (remaining > 0)
                {
                    double u = NextUniform(ref seed);

                    double gap =
                        Math.Floor(Math.Log(u) / logFailure);

                    if (gap >= remaining)
                        break;

                    remaining -= (long)gap + 1;
                    successes++;
                }
            }
        }

        return invert
            ? count - successes
            : successes;
    }

    /// <summary>
    /// Generateur deterministe SplitMix64.
    /// La graine est locale a chaque salve.
    /// </summary>
    private static double NextUniform(ref ulong state)
    {
        state = unchecked(
            state + 0x9E3779B97F4A7C15UL);

        ulong z = state;

        z = unchecked(
            (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);

        z = unchecked(
            (z ^ (z >> 27)) * 0x94D049BB133111EBUL);

        z ^= z >> 31;

        // Valeur dans ]0,1[, avec 53 bits de precision.
        return ((z >> 12) + 0.5)
            / 4503599627370496.0;
    }
}
