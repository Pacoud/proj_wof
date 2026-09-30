using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Recovery;

public class OrganizationRecoveryTests {


    [Fact]
    public void StationaryDivisionRecoversOrganizationInSafeFriendlyProvince()
    {
        var france =
            new Country(1, "France");

        var province =
            new Province(
                1,
                "Rear",
                owner: france
            );

        var division =
            new Division(
                "French Division",
                province,
                100,
                100,
                country: france
            );

        division.LoseOrganization(50);

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(
            division
        );

        simulation.Tick();

        Assert.Equal(
            56,
            division.Organization
        );
    }

    [Fact]
    public void LowOrganizationDoesNotAutomaticallyMeanBroken()
    {
        var france =
            new Country(1, "France");

        var province =
            new Province(
                1,
                "Rear",
                owner: france
            );

        var division =
            new Division(
                "French Division",
                province,
                100,
                100,
                country: france
            );

        division.LoseOrganization(90);

        Assert.Equal(
            10,
            division.Organization
        );

        Assert.False(
            division.IsBroken
        );
    }


    [Fact]
    public void ZeroOrganizationMakesDivisionBroken()
    {
        var province =
            new Province(1, "A");

        var division =
            new Division(
                "Division",
                province,
                100,
                100
            );

        division.LoseOrganization(100);

        Assert.Equal(
            0,
            division.Organization
        );

        Assert.True(
            division.IsBroken
        );
    }
}