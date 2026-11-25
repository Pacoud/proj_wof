using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class EngagementBattlePreparationTests
{
    // Capturer les valeurs avant l'appel évite de comparer deux vues du même état vivant.
    private static object?[] State(EngagementBattleTestScenario s, Engagement battle)
    {
        var state = new List<object?>
        {
            s.Cache.Count, s.Cache.Contains(s.Province), s.Cache.Contains(s.Border),
            s.System.GetCombatMode(s.Battle), battle.IsActive, battle.EndHour, battle.WinnerCountry,
            battle.BattlefieldProvince, battle.MeetingBorderNeighbor, battle.BattlefieldProvince.Controller,
        };
        foreach (var d in battle.Participants)
        {
            state.Add((d, d.CurrentEngagement, d.CurrentProvince, d.EnteredFrom, d.ResourceMode));
            state.Add((battle.GetRole(d), battle.GetEntryProvince(d), battle.GetJoinHour(d)));
            state.Add((d.Organization, d.Fuel, d.Ammunition, d.GetCurrentResourceTotals()));
            foreach (var b in d.Battalions)
            {
                state.Add((b, b.ParentDivision));
                foreach (var c in b.Companies)
                    state.Add((c, c.ParentBattalion, c.Composition, FormationResourceTotals.Capture(c.Composition!)));
            }
        }
        return state.ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparationKeepsExactPlacementsAndLeavesTheEngagementInLegacy(bool meeting)
    {
        var s = new EngagementBattleTestScenario(meeting);
        Assert.Equal(meeting ? EngagementType.MeetingEngagement : EngagementType.ProvinceBattle, s.Battle.Type);
        var empty = s.Second.Battalions[0].Companies[0];
        EngagementBattleTestScenario.Empty(empty);
        var before = State(s, s.Battle);

        Assert.True(EngagementDeploymentZones.TryCompute(s.Field, s.Battle, out var zones,
            out _, diplomacy: s.Diplomacy));
        var expected = EngagementCompanyPlacement.Compute(EngagementDeploymentPlan.Compute(zones!, s.Battle))
            .Companies.Where(p => p.Company != empty).ToArray();

        Assert.True(EngagementBattleBuilder.TryBuild(s.Field, s.Battle, out var ready,
            out var refusal, s.Diplomacy));
        Assert.Equal(EngagementBattleRefusal.None, refusal);
        Assert.Same(s.Field, ready.Field);
        Assert.Same(s.Battle, ready.Engagement);
        Assert.Equal(zones!.BattlefieldSectorIds, ready.BattlefieldSectorIds);
        Assert.Equal(expected, ready.Companies.ToArray());
        Assert.Equal(7, ready.Companies.Select(p => p.Company).Distinct().Count());
        Assert.DoesNotContain(ready.Companies, p => p.Company == empty);
        Assert.Throws<NotSupportedException>(() => ((IList<CompanyPlacement>)ready.Companies).Clear());

        foreach (var division in s.Battle.Participants)
        {
            var reserve = Assert.Single(ready.Companies, p =>
                p.Battalion.ParentDivision == division && p.Role == CompanyDeploymentRole.Reserve);
            var rear = zones.EnteringDivisions.Contains(division) ? zones.EnteringRearSectorIds : zones.FacingRearSectorIds;
            Assert.Contains(s.Field.Partition.GetSectorOfCell(reserve.Cell).Id, rear);
        }
        Assert.Equal(before, State(s, s.Battle));
        Assert.Same(s.Field, s.Cache.GetOrGenerate(s.Battle));
        Assert.Equal(EngagementCombatMode.LegacyOperational, s.System.GetCombatMode(s.Battle));
    }

    [Theory]
    [InlineData(EngagementBattleRefusal.LegacyResources)]
    [InlineData(EngagementBattleRefusal.NoCompanies)]
    [InlineData(EngagementBattleRefusal.MissingSide)]
    [InlineData(EngagementBattleRefusal.NoEntryBorder)]
    [InlineData(EngagementBattleRefusal.BattlefieldTooShallow)]
    [InlineData(EngagementBattleRefusal.ParticipantWithoutSide)]
    public void EachRefusalLeavesNoPreparationAndPreservesTheInputs(EngagementBattleRefusal expected)
    {
        var s = new EngagementBattleTestScenario(legacySecond: expected == EngagementBattleRefusal.LegacyResources);
        var battle = s.Battle;
        var field = s.Field;

        switch (expected)
        {
            case EngagementBattleRefusal.NoCompanies:
                foreach (var company in s.Second.Battalions[0].Companies)
                    EngagementBattleTestScenario.Empty(company);
                break;
            case EngagementBattleRefusal.MissingSide:
                // Rencontre au bord de la province, mais aucun participant ne vient de la voisine.
                battle = new Engagement(2, 1, s.Border, s.Province, 0.75);
                battle.AddParticipant(EngagementBattleTestScenario.Formation(s.Province, s.Red), 1);
                battle.AddParticipant(EngagementBattleTestScenario.Formation(s.Province, s.Blue), 1);
                break;
            case EngagementBattleRefusal.NoEntryBorder:
                s.Border.ChangeController(s.Blue);
                break;
            case EngagementBattleRefusal.BattlefieldTooShallow:
                // Même province, vrai bord d'entrée, trop peu de bandes pour le second front.
                var small = new ProvinceHexGrid(field.Grid.Descriptor, 8, 8);
                ProvinceBorderLayout.Apply(small);
                field = new ProvinceBattlefieldGrid(field.Province, small, GrandSectorPartition.Build(small));
                break;
            case EngagementBattleRefusal.ParticipantWithoutSide:
                var neutral = EngagementBattleTestScenario.Formation(s.Province, new Country(3, "Neutre"));
                battle.AddParticipant(neutral, 1);
                break;
        }

        var before = State(s, battle);
        Assert.False(EngagementBattleBuilder.TryBuild(field, battle, out var ready, out var refusal, s.Diplomacy));
        Assert.Null(ready);
        Assert.Equal(expected, refusal);
        Assert.Equal(before, State(s, battle));
        Assert.Same(s.Field, s.Cache.GetOrGenerate(s.Battle));
    }

    [Fact]
    public void IndependentScenariosGiveTheSameOrderedDecisions()
    {
        static CompanyPlacement[] Prepare()
        {
            var s = new EngagementBattleTestScenario();
            Assert.True(EngagementBattleBuilder.TryBuild(s.Field, s.Battle, out var ready, out _));
            return ready.Companies.ToArray();
        }
        var first = Prepare();
        var second = Prepare();
        Assert.NotSame(first[0].Company, second[0].Company);
        Assert.Equal(first.Select(Key), second.Select(Key));

        static object Key(CompanyPlacement p) =>
            (p.Company.Name, p.Role, p.GrandSectorId, p.Cell, p.Point, p.FacingDegrees, p.FrontageMeters);
    }

    [Fact]
    public void WrongGridEndedEngagementAndDetachedParticipantAreInvalidCalls()
    {
        var s = new EngagementBattleTestScenario();
        var other = new EngagementBattleTestScenario();
        Assert.Throws<ArgumentException>(() =>
            EngagementBattleBuilder.TryBuild(other.Field, s.Battle, out _, out _));

        s.First.LeaveEngagement(s.Battle);
        Assert.Throws<InvalidOperationException>(() =>
            EngagementBattleBuilder.TryBuild(s.Field, s.Battle, out _, out _));

        other.Battle.End(null, 2);
        Assert.Throws<InvalidOperationException>(() =>
            EngagementBattleBuilder.TryBuild(other.Field, other.Battle, out _, out _));
    }
}