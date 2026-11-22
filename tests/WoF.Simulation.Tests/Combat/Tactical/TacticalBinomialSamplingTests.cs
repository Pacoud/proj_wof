
using WoF.Simulation.Military.Combat.Tactical;

namespace WoF.Simulation.Tests.Combat.Tactical;

/// <summary>
/// Tirage binomial partage par les resolveurs de
/// contacts et d'effets. Teste directement, car plus
/// aucun resolveur ne l'expose seul.
/// </summary>
public sealed class TacticalBinomialSamplingTests
{
    public enum Branch
    {
        NormalApproximation,
        GeometricGaps,
        PoissonProduct
    }

    private static ulong SeedFor(int index) =>
        unchecked(12345UL + (ulong)index * 0x9E3779B97F4A7C15UL);

    [Fact]
    public void Sample_StaysWithinBounds()
    {
        long[] counts = { 1, 10, 40, 1000 };
        double[] probabilities = { 0.01, 0.3, 0.7, 0.99 };

        foreach (long count in counts)
        {
            foreach (double p in probabilities)
            {
                for (int i = 0; i < 50; i++)
                {
                    long sample = TacticalImpactResolver
                        .SampleBinomial(count, p, SeedFor(i));

                    Assert.InRange(sample, 0, count);
                }
            }
        }
    }

    [Fact]
    public void ImpossibleAndCertainProbabilities_AreExact()
    {
        Assert.Equal(0, TacticalImpactResolver.SampleBinomial(0, 0.5, 1));
        Assert.Equal(0, TacticalImpactResolver.SampleBinomial(-5, 0.5, 1));
        Assert.Equal(0, TacticalImpactResolver.SampleBinomial(100, 0, 1));
        Assert.Equal(0, TacticalImpactResolver.SampleBinomial(100, -0.1, 1));
        Assert.Equal(100, TacticalImpactResolver.SampleBinomial(100, 1, 1));
        Assert.Equal(100, TacticalImpactResolver.SampleBinomial(100, 1.5, 1));
    }

    [Fact]
    public void SameSeed_GivesSameResult()
    {
        // Un cas par branche du tirage.
        (long Count, double P)[] cases =
        {
            (1000, 0.3),
            (40, 0.1),
            (200_000_000_000_000_000, 1e-17)
        };

        foreach (var (count, p) in cases)
        {
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(
                    TacticalImpactResolver.SampleBinomial(count, p, SeedFor(i)),
                    TacticalImpactResolver.SampleBinomial(count, p, SeedFor(i)));
            }
        }

        ulong first = 7;
        ulong second = 7;

        for (int i = 0; i < 100; i++)
        {
            double u = TacticalImpactResolver.NextUniform(ref first);

            Assert.Equal(
                u,
                TacticalImpactResolver.NextUniform(ref second));

            Assert.True(u > 0 && u < 1);
        }
    }

    /// <summary>
    /// La moyenne de nombreux tirages doit rester proche
    /// de count * p, dans chacune des trois branches.
    /// p = 0,9 couvre l'inversion (tirage sur 1 - p).
    /// Les graines sont fixes : le test est deterministe.
    /// </summary>
    [Theory]
    [InlineData(1000L, 0.3, Branch.NormalApproximation)]
    [InlineData(40L, 0.1, Branch.GeometricGaps)]
    [InlineData(40L, 0.9, Branch.GeometricGaps)]
    [InlineData(200_000_000_000_000_000L, 1e-17, Branch.PoissonProduct)]
    public void SampleMean_MatchesExpectation(
        long count,
        double probability,
        Branch branch)
    {
        // Les parametres doivent bien atteindre la
        // branche annoncee, sans quoi le test ment.
        double expected = count * probability;
        double failure = count * (1.0 - probability);
        bool normal = expected >= 25 && failure >= 25;
        bool poisson = Math.Log(1.0 - probability) == 0.0;

        Branch reached = normal
            ? Branch.NormalApproximation
            : poisson
                ? Branch.PoissonProduct
                : Branch.GeometricGaps;

        Assert.Equal(branch, reached);

        const int samples = 4000;
        double sum = 0;

        for (int i = 0; i < samples; i++)
        {
            sum += TacticalImpactResolver.SampleBinomial(
                count, probability, SeedFor(i));
        }

        double mean = sum / samples;

        // La loi de Poisson a pour variance sa moyenne ;
        // c'est aussi la limite de count * p * (1 - p).
        double variance = branch == Branch.PoissonProduct
            ? expected
            : expected * (1.0 - probability);

        double standardError = Math.Sqrt(variance / samples);

        Assert.InRange(
            mean,
            expected - 5 * standardError,
            expected + 5 * standardError);
    }
}
