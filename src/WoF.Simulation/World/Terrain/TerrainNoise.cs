using WoF.Simulation.Core;

namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Bruit de gradient 2D (type Perlin) et somme d'octaves.
///
/// Le gradient d'un nœud (ix, iy) vient d'un hachage enchaîné
/// de la graine, puis de ix, puis de iy : la valeur en un point
/// ne dépend pas de l'ordre de parcours des cases.
/// Seules des additions, multiplications et Math.Floor sont utilisées.
/// </summary>
internal static class TerrainNoise
{
    private const double Diagonal = 0.70710678118654752440;

    private static readonly (double X, double Y)[] Gradients =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (Diagonal, Diagonal), (-Diagonal, Diagonal),
        (Diagonal, -Diagonal), (-Diagonal, -Diagonal)
    };

    /// <summary>Valeur environ dans [-0,71 ; 0,71].</summary>
    internal static double Gradient(ulong seed, double x, double y)
    {
        double floorX = Math.Floor(x);
        double floorY = Math.Floor(y);

        long x0 = (long)floorX;
        long y0 = (long)floorY;

        double fx = x - floorX;
        double fy = y - floorY;

        double n00 = Dot(seed, x0, y0, fx, fy);
        double n10 = Dot(seed, x0 + 1, y0, fx - 1, fy);
        double n01 = Dot(seed, x0, y0 + 1, fx, fy - 1);
        double n11 = Dot(seed, x0 + 1, y0 + 1, fx - 1, fy - 1);

        double u = Fade(fx);
        double v = Fade(fy);

        double bottom = n00 + u * (n10 - n00);
        double top = n01 + u * (n11 - n01);

        return bottom + v * (top - bottom);
    }

    /// <summary>
    /// Somme d'octaves : fréquence doublée et amplitude divisée par deux
    /// à chaque octave, chaque octave ayant sa propre graine.
    /// </summary>
    internal static double Fractal(
        ulong seed,
        double x,
        double y,
        int octaves)
    {
        double sum = 0;
        double amplitude = 1;
        double frequency = 1;

        for (int octave = 0; octave < octaves; octave++)
        {
            ulong octaveSeed =
                DeterministicRandom.Hash(seed, octave);

            sum += amplitude * Gradient(
                octaveSeed,
                x * frequency,
                y * frequency);

            amplitude *= 0.5;
            frequency *= 2;
        }

        return sum;
    }

    private static double Dot(
        ulong seed,
        long ix,
        long iy,
        double dx,
        double dy)
    {
        ulong hash = DeterministicRandom.Hash(seed, ix, iy);

        var (gx, gy) = Gradients[(int)(hash >> 61)];

        return gx * dx + gy * dy;
    }

    private static double Fade(double t)
    {
        // 6t^5 - 15t^4 + 10t^3
        return t * t * t * (t * (t * 6 - 15) + 10);
    }
}
