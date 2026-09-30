using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Retreat;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Tests.Military;

public class TransitStateTests
{

    [Fact]
    public void ReversePreservesPhysicalPosition()
    {
        var a =
            new Province(1, "A");

        var b =
            new Province(2, "B");

        var plan =
            new MovementPlan(
                DurationHours: 5,
                FuelPerHour: 1
            );

        var transit =
            new TransitState(
                a,
                b,
                plan
            );

        transit.AdvanceOneHour();
        transit.AdvanceOneHour();

        Assert.Equal(
            0.40,
            transit.Progress,
            2
        );

        transit.Pause();

        transit.Reverse();

        Assert.Same(
            b,
            transit.Origin
        );

        Assert.Same(
            a,
            transit.Destination
        );

        Assert.Equal(
            2,
            transit.RemainingHours
        );

        Assert.Equal(
            0.60,
            transit.Progress,
            2
        );

        Assert.Equal(
            transit.Progress,
            transit.PreviousProgress,
            6
        );

        Assert.False(
            transit.IsPaused
        );
    }
    [Fact]
    public void BrokenDivisionOnConnectionReversesTowardOrigin()
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

        var road =
            new InfrastructureLink(
                a,
                b,
                InfrastructureType.Road,
                2
            );

        var french =
            new Division(
                name: "French Division",
                position: a,
                fuel: 100,
                ammunition: 100,
                country: france
            );

        var german =
            new Division(
                name: "German Division",
                position: b,
                fuel: 100,
                ammunition: 100,
                country: germany,
                maxOrganization: 5
            );

        var simulation =
            new SimulationEngine();

        simulation.AddDivision(french);
        simulation.AddDivision(german);

        simulation.AddInfrastructureLink(
            road
        );

        simulation.DeclareWar(
            france,
            germany
        );

        simulation.TryOrderMoveTo(
            french,
            b
        );

        simulation.TryOrderMoveTo(
            german,
            a
        );

        // H1
        simulation.Tick();

        // H2 : rencontre sur la liaison.
        simulation.Tick();

        Assert.True(
            french.IsEngaged
        );

        Assert.True(
            german.IsEngaged
        );

        // H3 : combat.
        // L'organisation allemande tombe à zéro.
        simulation.Tick();

        Assert.True(
            german.IsBroken
        );

        Assert.True(
            german.IsRetreating
        );

        Assert.Equal(
            RetreatPhase.ReturningToOrigin,
            german.RetreatPhase
        );

        // L'Allemand venait de B.
        // Après Reverse(), il retourne donc vers B.
        Assert.NotNull(
            german.Transit
        );

        Assert.Same(
            b,
            german.Transit!.Destination
        );

        // Le vainqueur français reprend A -> B.
        Assert.False(
            french.IsEngaged
        );

        Assert.NotNull(
            french.Transit
        );

        Assert.False(
            french.Transit!.IsPaused
        );

        Assert.Same(
            b,
            french.Transit.Destination
        );
    }

    [Fact]
    public void ConnectionRetreatContinuesTowardSafeRearAfterReturningToOrigin()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        var a = new Province(
            1,
            "A",
            owner: france
        );

        var b = new Province(
            2,
            "B",
            owner: germany
        );

        var c = new Province(
            3,
            "C",
            owner: germany
        );

        var d = new Province(
            4,
            "D",
            owner: germany
        );

        a.ConnectTo(b);
        b.ConnectTo(c);
        c.ConnectTo(d);

        var roadAB = new InfrastructureLink(
            a,
            b,
            InfrastructureType.Road,
            2
        );

        var roadBC = new InfrastructureLink(
            b,
            c,
            InfrastructureType.Road,
            2
        );

        var roadCD = new InfrastructureLink(
            c,
            d,
            InfrastructureType.Road,
            2
        );

        var french = new Division(
            name: "French Division",
            position: a,
            fuel: 100,
            ammunition: 100,
            country: france
        );

        var german = new Division(
            name: "German Division",
            position: b,
            fuel: 100,
            ammunition: 100,
            country: germany,
            maxOrganization: 5
        );

        var simulation = new SimulationEngine();

        simulation.AddDivision(french);
        simulation.AddDivision(german);

        simulation.AddInfrastructureLink(roadAB);
        simulation.AddInfrastructureLink(roadBC);
        simulation.AddInfrastructureLink(roadCD);

        simulation.DeclareWar(
            france,
            germany
        );

        simulation.TryOrderMoveTo(
            french,
            b
        );

        simulation.TryOrderMoveTo(
            german,
            a
        );

        // Rencontre sur la liaison.
        simulation.Tick();
        simulation.Tick();

        // Résolution du combat :
        // l'Allemand devient Broken.
        simulation.Tick();

        Assert.Equal(
            RetreatPhase.ReturningToOrigin,
            german.RetreatPhase
        );

        int safety = 30;

        while (german.IsRetreating
            && safety-- > 0)
        {
            simulation.Tick();
        }

        Assert.True(
            safety > 0,
            "Retreat did not complete."
        );

        Assert.False(
            german.IsRetreating
        );

        Assert.True(
            german.IsBroken
        );

        Assert.Same(
            d,
            german.CurrentProvince
        );
    }

}
