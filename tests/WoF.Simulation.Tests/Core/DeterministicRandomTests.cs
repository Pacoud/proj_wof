using WoF.Simulation.Core;
using WoF.Simulation.Military.Combat.Tactical;

namespace WoF.Simulation.Tests.Core;

public sealed class DeterministicRandomTests
{
    [Fact]
    public void NextUInt64_MatchesReferenceSplitMix64Sequence()
    {
        // Valeurs de référence publiées de SplitMix64 pour un état initial nul.
        ulong state = 0;

        Assert.Equal(0xE220A8397B1DCDAFUL, DeterministicRandom.NextUInt64(ref state));
        Assert.Equal(0x6E789E6AA1B965F4UL, DeterministicRandom.NextUInt64(ref state));
        Assert.Equal(0x06C45D188009454FUL, DeterministicRandom.NextUInt64(ref state));
    }

    [Fact]
    public void TacticalImpactResolver_DelegatesToTheSameSequence()
    {
        ulong first = 123456789;
        ulong second = 123456789;

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(
                DeterministicRandom.NextUniform(ref first),
                TacticalImpactResolver.NextUniform(ref second));
        }

        Assert.Equal(first, second);
    }

    [Fact]
    public void Hash_IsStatelessAndDependsOnArgumentOrder()
    {
        Assert.Equal(
            DeterministicRandom.Hash(42, 3, 7),
            DeterministicRandom.Hash(42, 3, 7));

        Assert.NotEqual(
            DeterministicRandom.Hash(42, 3, 7),
            DeterministicRandom.Hash(42, 7, 3));

        Assert.NotEqual(
            DeterministicRandom.Hash(42, 3, 7),
            DeterministicRandom.Hash(43, 3, 7));
    }

    [Fact]
    public void NextInt_StaysWithinInclusiveBounds()
    {
        ulong state = 99;
        var seen = new HashSet<int>();

        for (int i = 0; i < 1000; i++)
        {
            int value = DeterministicRandom.NextInt(ref state, 2, 5);

            Assert.InRange(value, 2, 5);
            seen.Add(value);
        }

        Assert.Equal(4, seen.Count);
    }
}
