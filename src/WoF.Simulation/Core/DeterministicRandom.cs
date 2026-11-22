namespace WoF.Simulation.Core;

/// <summary>
/// Tirages déterministes du projet, fondés sur SplitMix64.
///
/// Deux usages :
/// - une suite de tirages à partir d'un état local
///   (<see cref="NextUInt64"/>, <see cref="NextUniform"/>) ;
/// - un hachage sans état d'une graine et de coordonnées
///   (<see cref="Hash(ulong, long)"/>), dont le résultat
///   ne dépend pas de l'ordre de parcours.
///
/// Aucune dépendance à l'horloge ni à System.Random.
/// </summary>
internal static class DeterministicRandom
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;

    /// <summary>
    /// Avance l'état d'un pas SplitMix64 et renvoie 64 bits.
    /// </summary>
    internal static ulong NextUInt64(ref ulong state)
    {
        state = unchecked(state + Gamma);

        return Finalize(state);
    }

    /// <summary>
    /// Valeur dans ]0,1[, avec 53 bits de précision.
    /// Suite identique à l'ancien TacticalImpactResolver.NextUniform.
    /// </summary>
    internal static double NextUniform(ref ulong state)
    {
        return ToUniform(NextUInt64(ref state));
    }

    /// <summary>
    /// Entier dans [minInclusive, maxInclusive].
    /// </summary>
    internal static int NextInt(
        ref ulong state,
        int minInclusive,
        int maxInclusive)
    {
        if (maxInclusive < minInclusive)
            throw new ArgumentOutOfRangeException(
                nameof(maxInclusive));

        long range =
            (long)maxInclusive - minInclusive + 1;

        long offset =
            (long)(NextUniform(ref state) * range);

        // NextUniform est strictement inférieur à 1,
        // mais l'arrondi du produit peut atteindre range.
        if (offset >= range)
            offset = range - 1;

        return (int)(minInclusive + offset);
    }

    /// <summary>
    /// Mélange une valeur de 64 bits (un pas SplitMix64 depuis cette valeur).
    /// </summary>
    internal static ulong Mix(ulong value)
    {
        return Finalize(unchecked(value + Gamma));
    }

    /// <summary>
    /// Hachage enchaîné : la graine est mélangée, puis la valeur.
    /// </summary>
    internal static ulong Hash(ulong seed, long a)
    {
        return Mix(Mix(seed) ^ unchecked((ulong)a));
    }

    /// <summary>
    /// Hachage enchaîné : graine, puis a, puis b.
    /// </summary>
    internal static ulong Hash(ulong seed, long a, long b)
    {
        return Mix(Hash(seed, a) ^ unchecked((ulong)b));
    }

    /// <summary>
    /// Convertit 64 bits en valeur dans ]0,1[.
    /// </summary>
    internal static double ToUniform(ulong bits)
    {
        return ((bits >> 12) + 0.5)
            / 4503599627370496.0;
    }

    private static ulong Finalize(ulong z)
    {
        z = unchecked(
            (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);

        z = unchecked(
            (z ^ (z >> 27)) * 0x94D049BB133111EBUL);

        return z ^ (z >> 31);
    }
}
