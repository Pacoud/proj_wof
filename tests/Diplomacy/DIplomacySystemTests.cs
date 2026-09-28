using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Diplomacy;

public class DiplomacySystemTests
{
    [Fact]
    public void DeclaredWarIsSymmetric()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var diplomacy =
            new DiplomacySystem();

        diplomacy.DeclareWar(
            france,
            germany
        );

        Assert.True(
            diplomacy.AreAtWar(
                france,
                germany
            )
        );

        Assert.True(
            diplomacy.AreAtWar(
                germany,
                france
            )
        );
    }

    [Fact]
    public void PathfinderCannotEnterNeutralForeignTerritory()
    {
        var france =
            new Country(1, "France");

        var switzerland =
            new Country(2, "Switzerland");

        var a =
            new Province(
                1,
                "A",
                owner: france
            );

        var b =
            new Province(
                2,
                "B",
                owner: switzerland
            );

        a.ConnectTo(b);

        var diplomacy =
            new DiplomacySystem();

        var path =
            MilitaryPathfinder.FindFastestPath(
                a,
                b,
                Array.Empty<InfrastructureLink>(),
                DivisionType.Infantry,
                france,
                diplomacy
            );

        Assert.Null(path);
    }

    [Fact]
    public void PathfinderCanTargetEnemyProvinceDuringWar()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var a =
            new Province(
                1,
                "A",
                owner: france
            );

        var b =
            new Province(
                2,
                "B",
                owner: germany
            );

        a.ConnectTo(b);

        var diplomacy =
            new DiplomacySystem();

        diplomacy.DeclareWar(
            france,
            germany
        );

        var path =
            MilitaryPathfinder.FindFastestPath(
                a,
                b,
                Array.Empty<InfrastructureLink>(),
                DivisionType.Infantry,
                france,
                diplomacy
            );

        Assert.NotNull(path);

        Assert.Equal(
            new[] { a, b },
            path
        );
    }

    [Fact]
    public void PathfinderCannotTraverseEnemyProvince()
    {
        var france =
            new Country(1, "France");

        var germany =
            new Country(2, "Germany");

        var a =
            new Province(
                1,
                "A",
                owner: france
            );

        var b =
            new Province(
                2,
                "B",
                owner: germany
            );

        var c =
            new Province(
                3,
                "C",
                owner: germany
            );

        a.ConnectTo(b);
        b.ConnectTo(c);

        var diplomacy =
            new DiplomacySystem();

        diplomacy.DeclareWar(
            france,
            germany
        );

        var path =
            MilitaryPathfinder.FindFastestPath(
                a,
                c,
                Array.Empty<InfrastructureLink>(),
                DivisionType.Infantry,
                france,
                diplomacy
            );

        Assert.Null(path);
    }
}