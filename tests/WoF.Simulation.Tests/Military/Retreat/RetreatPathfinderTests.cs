using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Retreat;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Military.Retreat;

public class RetreatPathfinderTests
{



    [Fact]
    public void RetreatPrefersProvinceNotAdjacentToEnemy()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var battlefield =
            new Province(
                1,
                "Battlefield",
                owner: france,
                controller: france
            );

        var nearRear =
            new Province(
                2,
                "Near Rear",
                owner: germany,
                controller: germany
            );

        var safeRear =
            new Province(
                3,
                "Safe Rear",
                owner: germany,
                controller: germany
            );

        battlefield.ConnectTo(
            nearRear
        );

        nearRear.ConnectTo(
            safeRear
        );

        var frenchDivision =
            new Division(
                "French Division",
                battlefield,
                100,
                100,
                country: france
            );

        var diplomacy =
            new DiplomacySystem();

        diplomacy.DeclareWar(
            france,
            germany
        );

        var path =
            RetreatPathfinder.FindRetreatPath(
                battlefield,
                germany,
                new[] { frenchDivision },
                diplomacy
            );

        Assert.NotNull(path);

        Assert.Equal(
            new[]
            {
                nearRear,
                safeRear
            },
            path
        );
    }
}
