using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using Xunit.Abstractions;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalObservationTests
{
    private readonly ITestOutputHelper _output;

    public TacticalObservationTests(ITestOutputHelper output) => _output = output;

    private static Division MakeDivision(
        string name, Province province, Country country, int battalions)
    {
        var division = new Division(
            name, province, fuel: 100, ammunition: 100, country: country,
            composition: new DivisionComposition(
                manpower: battalions * 400,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        for (int b = 1; b <= battalions; b++)
        {
            var battalion = new Battalion(b, $"Bataillon {b}",
                BattalionType.Infantry);

            for (int c = 1; c <= 4; c++)
            {
                battalion.AddCompany(new Company(
                    c, $"Compagnie {c}", CompanyType.Infantry,
                    new CompanyComposition(manpower: 100)));
            }

            division.AddBattalion(battalion);
        }

        division.EnableHierarchicalResources();
        return division;
    }

    private static (
        TacticalBattle Battle, Division French, Division German,
        BattleSector Hill, BattleSector Forest, BattleSector Rear)
        CreateScenario()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Champ de bataille");

        var french = MakeDivision("Division française", province, france, 4);
        var german = MakeDivision("Division allemande", province, germany, 1);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);
        var system = new EngagementSystem(diplomacy);
        system.DetectProvinceEngagements(
            new[] { french, german }, currentHour: 1);

        var engagement = Assert.Single(system.ActiveEngagements);
        var field = new TacticalBattlefield();

        var hill = new BattleSector(
            1, "Colline", TerrainType.Hills, elevationMeters: 180,
            features: SectorFeature.Entrenchments);
        var forest = new BattleSector(
            2, "Forêt", TerrainType.Forest,
            features: SectorFeature.Woods);
        var rear = new BattleSector(
            3, "Arrière", TerrainType.Plains,
            features: SectorFeature.Road);

        field.AddSector(hill);
        field.AddSector(forest);
        field.AddSector(rear);
        field.Connect(hill, forest, 600);
        field.Connect(forest, rear, 900);

        return (new TacticalBattle(engagement, field),
            french, german, hill, forest, rear);
    }

    private static TacticalDeploymentPlan BuildFrenchPlan(
        TacticalBattle battle, Division french,
        BattleSector hill, BattleSector forest, BattleSector rear)
    {
        var order = new DivisionTacticalOrder(
            french,
            TacticalPosture.Defend,
            prioritySector: hill,
            secondarySectors: new[] { forest },
            reserveSector: rear,
            reserveBattalions: 1);

        return TacticalCommandSystem.CreatePlan(battle, order);
    }

    [Fact]
    public void CapturedSnapshot_DescribesActualAutomaticDecisions()
    {
        var (battle, french, _, hill, forest, rear) = CreateScenario();
        var plan = BuildFrenchPlan(battle, french, hill, forest, rear);

        // Planning alone must not be represented as an applied decision.
        Assert.Empty(TacticalBattleSnapshotBuilder.Capture(battle).Orders);
        Assert.Empty(TacticalBattleSnapshotBuilder.Capture(battle).Companies);

        TacticalCommandSystem.ApplyPlan(plan);
        var snapshot = TacticalBattleSnapshotBuilder.Capture(battle);

        Assert.Equal(battle.Engagement.Id, snapshot.EngagementId);
        Assert.Equal(3, snapshot.Sectors.Count);
        Assert.Equal(2, snapshot.Connections.Count);
        Assert.Equal(16, snapshot.Companies.Count);
        Assert.Single(snapshot.Orders);

        var order = snapshot.Orders[0];
        Assert.Equal(TacticalPosture.Defend, order.Posture);
        Assert.Equal(hill.Id, order.PrioritySectorId);
        Assert.Equal(new[] { forest.Id }, order.SecondarySectorIds);
        Assert.Equal(rear.Id, order.ReserveSectorId);
        Assert.Equal(1, order.ReserveBattalions);

        Assert.Equal(8, snapshot.Companies.Count(c =>
            c.AssignmentReason == TacticalAssignmentReason.PriorityObjective));
        Assert.Equal(4, snapshot.Companies.Count(c =>
            c.AssignmentReason == TacticalAssignmentReason.SecondaryObjective));
        Assert.Equal(4, snapshot.Companies.Count(c =>
            c.AssignmentReason == TacticalAssignmentReason.DivisionReserve));

        Assert.Equal(3, snapshot.Companies.Count(c =>
            c.Role == CompanyDeploymentRole.FrontLine));
        Assert.All(snapshot.Companies, c => Assert.Equal(100, c.PersonnelAvailable));

        string report = TacticalBattleReportFormatter.Format(snapshot);
        Assert.Contains("Colline", report);
        Assert.Contains("objectif divisionnaire prioritaire", report);
        Assert.Contains("reserve divisionnaire demandee", report);
        _output.WriteLine(report);
    }

    [Fact]
    public void Snapshot_IsDetachedFromLaterPersonnelChanges()
    {
        var (battle, french, _, hill, forest, rear) = CreateScenario();
        TacticalCommandSystem.ApplyPlan(
            BuildFrenchPlan(battle, french, hill, forest, rear));

        var original = TacticalBattleSnapshotBuilder.Capture(battle);
        var company = french.Battalions[0].Companies[0];
        company.Composition!.Manpower.ApplyCasualties(
            new PersonnelCasualtyReport(10, 0, 0));

        var updated = TacticalBattleSnapshotBuilder.Capture(battle);
        Assert.Equal(100, original.Companies[0].PersonnelAvailable);
        Assert.Equal(90, updated.Companies[0].PersonnelAvailable);
        Assert.Equal(100, original.Companies[0].PersonnelAvailable);
        Assert.Equal(100, updated.Companies[1].PersonnelAvailable);
    }

    [Fact]
    public void ManualDeployment_HasNoInventedCommandReason()
    {
        var (battle, french, german, hill, forest, rear) = CreateScenario();
        TacticalCommandSystem.ApplyPlan(
            BuildFrenchPlan(battle, french, hill, forest, rear));

        var germanCompany = german.Battalions[0].Companies[0];
        battle.DeployCompany(
            germanCompany, hill, CompanyDeploymentRole.FrontLine);

        var snapshot = TacticalBattleSnapshotBuilder.Capture(battle);
        Assert.Equal(17, snapshot.Companies.Count);

        var manual = Assert.Single(snapshot.Companies,
            c => c.DivisionName == german.Name);

        Assert.Null(manual.AssignmentReason);
        Assert.NotEqual(snapshot.Companies[0].ParticipantIndex,
            manual.ParticipantIndex);
        Assert.Equal(1, manual.CompanyId);
        Assert.Equal(1, snapshot.Companies[0].CompanyId);

        Assert.Contains("origine non documentee",
            TacticalBattleReportFormatter.Format(snapshot));
    }
}
