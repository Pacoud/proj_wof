using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Combat;

public sealed class BattlefieldProvinceTests
{
    [Fact]
    public void ProvincialEngagementKeepsItsProvinceAndHasNoMeetingBorder()
    {
        var province = new Province(1, "P", TerrainType.Mountain);
        var battle = new Engagement(1, 0, province);

        Assert.Same(province, battle.Province);
        Assert.Same(province, battle.BattlefieldProvince);
        Assert.Null(battle.MeetingBorderNeighbor);
        Assert.Equal(province.Terrain, battle.BattleTerrain);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.4, false)]
    [InlineData(0.5, true)]
    [InlineData(0.6, true)]
    [InlineData(1, true)]
    public void MeetingUsesTheExactProvinceEvenWithIdenticalTerrain(double progress, bool inB)
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");
        var battle = new Engagement(1, 0, a, b, progress);

        Assert.Same(inB ? b : a, battle.BattlefieldProvince);
        Assert.Same(inB ? a : b, battle.MeetingBorderNeighbor);
        Assert.Null(battle.Province); // Contrat existant des rencontres sur liaison.
    }

    [Fact]
    public void DisplayRoundingDoesNotMoveTheBattleAcrossTheMidpoint()
    {
        var a = new Province(1, "A", TerrainType.Mountain);
        var b = new Province(2, "B", TerrainType.Plains);
        var battle = new Engagement(1, 0, a, b, 0.4999999);

        Assert.Equal(0.5, battle.ConnectionProgress);
        Assert.Same(a, battle.BattlefieldProvince);
        Assert.Same(b, battle.MeetingBorderNeighbor);
        Assert.Equal(a.Terrain, battle.BattleTerrain);
        Assert.Equal(a.Terrain, BattleTerrainResolver.ResolveMeetingTerrain(a, b, 0.4999999));
    }

    [Fact]
    public void BattlefieldHasNoSetterAndStaysFixedAfterBothDivisionsMove()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Allemagne");
        var a = new Province(1, "A", TerrainType.Mountain);
        var b = new Province(2, "B", TerrainType.Plains);
        a.ConnectTo(b);
        var french = new Division("F", a, 100, 100, country: france);
        var german = new Division("G", b, 100, 100, country: germany);
        var plan = new MovementPlan(DurationHours: 2, FuelPerHour: 0);
        Assert.True(french.TryStartMovement(b, plan));
        Assert.True(german.TryStartMovement(a, plan));
        french.AdvanceOneHour();
        german.AdvanceOneHour();

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);
        var system = new EngagementSystem(diplomacy);
        system.DetectTransitEngagements(new[] { french, german }, currentHour: 1);
        var battle = Assert.Single(system.ActiveEngagements);
        Assert.Same(b, battle.BattlefieldProvince);
        Assert.Contains(french, battle.Participants);
        Assert.Contains(german, battle.Participants);

        // Après la fin de la rencontre, les deux participants se déplacent
        // réellement et arrivent tous deux dans A, hors du champ initial B.
        french.LoseOrganization(french.Organization);
        system.ProcessEngagements(currentHour: 2);
        Assert.False(battle.IsActive);
        french.Transit!.Reverse();
        german.Transit!.Resume();
        foreach (var division in new[] { french, german })
        {
            division.AdvanceOneHour();
            Assert.True(division.FinalizeArrival());
            Assert.Same(a, division.CurrentProvince);
        }

        Assert.Same(b, battle.BattlefieldProvince);
        Assert.Same(a, battle.MeetingBorderNeighbor);
        Assert.Equal(b.Terrain, battle.BattleTerrain);
        var property = typeof(Engagement).GetProperty(nameof(Engagement.BattlefieldProvince))!;
        Assert.Null(property.GetSetMethod(nonPublic: true)); // Même un setter privé serait détecté.
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void InvalidProgressCannotDesignateAProvince(double progress)
    {
        var a = new Province(1, "A");
        var b = new Province(2, "B");
        Assert.Throws<ArgumentOutOfRangeException>(() => new Engagement(1, 0, a, b, progress));
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleTerrainResolver.ResolveMeetingProvince(a, b, progress));
    }
}