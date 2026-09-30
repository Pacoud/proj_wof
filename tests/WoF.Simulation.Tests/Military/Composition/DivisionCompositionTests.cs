using WoF.Simulation.Military;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Composition;

public class DivisionCompositionTests
{
    [Fact]
    public void StrengthPoolStartsAtAuthorizedStrength()
    {
        var pool =
            new StrengthPool(
                authorized: 10_000
            );

        Assert.Equal(
            10_000,
            pool.Authorized
        );

        Assert.Equal(
            10_000,
            pool.Current
        );

        Assert.Equal(
            0,
            pool.Missing
        );

        Assert.Equal(
            1.0,
            pool.AvailabilityRatio,
            6
        );
    }

    [Fact]
    public void LossCannotReducePoolBelowZero()
    {
        var pool =
            new StrengthPool(
                authorized: 100
            );

        int lost =
            pool.ApplyLoss(
                150
            );

        Assert.Equal(
            100,
            lost
        );

        Assert.Equal(
            0,
            pool.Current
        );
    }

    [Fact]
    public void ReinforcementCannotExceedAuthorizedStrength()
    {
        var pool =
            new StrengthPool(
                authorized: 100,
                current: 60
            );

        int reinforced =
            pool.Reinforce(
                80
            );

        Assert.Equal(
            40,
            reinforced
        );

        Assert.Equal(
            100,
            pool.Current
        );
    }

    [Fact]
    public void StrengthPoolRejectsInvalidInitialCurrentValue()
    {
        Assert.Throws<
            ArgumentOutOfRangeException>(
            () =>
                new StrengthPool(
                    authorized: 100,
                    current: 120
                )
        );
    }

    [Fact]
    public void DivisionCanUseExplicitComposition()
    {
        var province =
            new Province(
                1,
                "A"
            );

        var composition =
            new DivisionComposition(
                manpower: 12_000,
                infantryEquipment: 10_000,
                artillery: 72,
                tanks: 30
            );

        var division =
            new Division(
                "Test Division",
                province,
                fuel: 100,
                ammunition: 100,
                composition: composition
            );

        Assert.Same(
            composition,
            division.Composition
        );

        Assert.Equal(
            12_000,
            division.Composition
                .Manpower
                .Current
        );

        Assert.Equal(
            30,
            division.Composition
                .Tanks
                .Current
        );
    }

    [Fact]
    public void ExistingArmoredDivisionReceivesPrototypeComposition()
    {
        var province =
            new Province(
                1,
                "A"
            );

        var division =
            new Division(
                "Armored Division",
                province,
                fuel: 100,
                ammunition: 100,
                type: DivisionType.Armored
            );

        Assert.True(
            division.Composition
                .Tanks
                .Current > 0
        );

        Assert.True(
            division.Composition
                .Manpower
                .Current > 0
        );
    }
}