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

    [Fact]
    public void FractionalTankLossesAccumulateAcrossTicks()
    {
        var tanks =
            new TankPool(
                authorized: 100
            );

        var expectation =
            new TankLossExpectation(
                Damaged: 0.30,
                Destroyed: 0.20
            );

        TankLossResult total =
            TankLossResult.None;

        int damaged = 0;
        int destroyed = 0;

        for (int i = 0; i < 5; i++)
        {
            TankLossResult result =
                tanks.ApplyExpectedLosses(
                    expectation
                );

            damaged +=
                result.Damaged;

            destroyed +=
                result.Destroyed;
        }

        Assert.Equal(
            1,
            destroyed
        );

        Assert.Equal(
            1,
            damaged
        );

        Assert.Equal(
            98,
            tanks.Operational
        );

        Assert.Equal(
            1,
            tanks.Damaged
        );

        Assert.Equal(
            1,
            tanks.Destroyed
        );
    }


    [Fact]
    public void DestroyedAndDamagedTanksAreDistinct()
    {
        var tanks =
            new TankPool(
                authorized: 10
            );

        TankLossResult result =
            tanks.ApplyExpectedLosses(
                new TankLossExpectation(
                    Damaged: 2,
                    Destroyed: 3
                )
            );

        Assert.Equal(
            3,
            result.Destroyed
        );

        Assert.Equal(
            2,
            result.Damaged
        );

        Assert.Equal(
            5,
            tanks.Operational
        );

        Assert.Equal(
            2,
            tanks.Damaged
        );

        Assert.Equal(
            3,
            tanks.Destroyed
        );
    }

    [Fact]
    public void SmallArmsAloneDoNotCauseTankLosses()
    {
        var hostilePower =
            new CombatPowerProfile(
                SmallArms: 100,
                Artillery: 0,
                Armored: 0
            );

        TankLossExpectation losses =
            TankLossCalculator.Calculate(
                hostilePower,
                exposedTanks: 180
            );

        Assert.Equal(
            0,
            losses.Damaged
        );

        Assert.Equal(
            0,
            losses.Destroyed
        );
    }

    [Fact]
    public void DestroyedTankIsMoreDangerousForCrewThanDamagedTank()
    {
        PersonnelCasualtyReport damaged =
            TankCrewCasualtyCalculator.Calculate(
                new TankLossResult(
                    Damaged: 10,
                    Destroyed: 0
                ),
                exposedCrewPositions: 50
            );

        PersonnelCasualtyReport destroyed =
            TankCrewCasualtyCalculator.Calculate(
                new TankLossResult(
                    Damaged: 0,
                    Destroyed: 10
                ),
                exposedCrewPositions: 50
            );

        Assert.True(
            destroyed.Total
            >
            damaged.Total
        );

        Assert.True(
            destroyed.KilledInAction
            >
            damaged.KilledInAction
        );
    }

    [Fact]
    public void DamagedTankStopsContributingToCombatPower()
    {
        var composition =
            new DivisionComposition(
                manpower: 8_000,
                infantryEquipment: 5_000,
                artillery: 36,
                tanks: 180,
                trucks: 600
            );

        CombatPowerProfile before =
            CompositionCombatPowerCalculator
                .Calculate(
                    composition
                );

        composition.Tanks.ApplyExpectedLosses(
            new TankLossExpectation(
                Damaged: 60,
                Destroyed: 0
            )
        );

        CombatPowerProfile after =
            CompositionCombatPowerCalculator
                .Calculate(
                    composition
                );

        Assert.Equal(
            120,
            composition.Tanks.Operational
        );

        Assert.Equal(
            60,
            composition.Tanks.Damaged
        );

        Assert.True(
            after.Armored
            <
            before.Armored
        );
    }
}
