using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Power;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Tests.Combat.Losses;

public class PersonnelCasualtyCalculatorTests
{
    [Fact]
    public void CombatProducesKilledAndWoundedButNoCaptured()
    {
        var hostilePower =
            new CombatPowerProfile(
                SmallArms: 20,
                Artillery: 20,
                Armored: 10
            );

        PersonnelCasualtyReport losses =
            PersonnelCasualtyCalculator
                .Calculate(
                    hostilePower,
                    exposedManpower: 10_000
                );

        Assert.True(
            losses.KilledInAction > 0
        );

        Assert.True(
            losses.WoundedInAction > 0
        );

        Assert.Equal(
            0,
            losses.MissingOrCaptured
        );
    }


    [Fact]
    public void ArtilleryPowerProducesMoreCasualtiesThanEqualSmallArmsPower()
    {
        var smallArms =
            new CombatPowerProfile(
                SmallArms: 10,
                Artillery: 0,
                Armored: 0
            );

        var artillery =
            new CombatPowerProfile(
                SmallArms: 0,
                Artillery: 10,
                Armored: 0
            );

        PersonnelCasualtyReport smallArmsLosses =
            PersonnelCasualtyCalculator
                .Calculate(
                    smallArms,
                    10_000
                );

        PersonnelCasualtyReport artilleryLosses =
            PersonnelCasualtyCalculator
                .Calculate(
                    artillery,
                    10_000
                );

        Assert.True(
            artilleryLosses.Total
            >
            smallArmsLosses.Total
        );
    }


    [Fact]
    public void PersonnelPoolTracksCasualtyCategories()
    {
        var personnel =
            new PersonnelPool(
                authorized: 10_000
            );

        personnel.ApplyCasualties(
            new PersonnelCasualtyReport(
                KilledInAction: 10,
                WoundedInAction: 40,
                MissingOrCaptured: 0
            )
        );

        Assert.Equal(
            9_950,
            personnel.Current
        );

        Assert.Equal(
            10,
            personnel.KilledInAction
        );

        Assert.Equal(
            40,
            personnel.WoundedUnavailable
        );

        Assert.Equal(
            40,
            personnel.TotalWoundedInAction
        );
    }


    [Fact]
    public void WoundedPersonnelCanReturnToDuty()
    {
        var personnel =
            new PersonnelPool(
                authorized: 10_000
            );

        personnel.ApplyCasualties(
            new PersonnelCasualtyReport(
                10,
                40,
                0
            )
        );

        int returned =
            personnel.ReturnWoundedToDuty(
                15
            );

        Assert.Equal(
            15,
            returned
        );

        Assert.Equal(
            9_965,
            personnel.Current
        );

        Assert.Equal(
            25,
            personnel.WoundedUnavailable
        );

        Assert.Equal(
            40,
            personnel.TotalWoundedInAction
        );
    }
}
