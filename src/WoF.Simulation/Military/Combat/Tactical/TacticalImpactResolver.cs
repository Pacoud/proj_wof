using WoF.Simulation.Core;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Tirages aleatoires deterministes partages par
/// les resolveurs de contacts et d'effets.
/// Ne modifie pas les ressources militaires.
/// </summary>
internal static class TacticalImpactResolver
{
    /// <summary>
    /// Tirage binomial sans iterer sur chaque cartouche.
    /// Exact par sauts pour les faibles esperances,
    /// approximation normale pour les grandes.
    /// </summary>
    internal static long SampleBinomial(
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
    /// Delegue a <see cref="DeterministicRandom.NextUniform"/>.
    /// </summary>
    internal static double NextUniform(ref ulong state)
    {
        return DeterministicRandom.NextUniform(ref state);
    }
}
