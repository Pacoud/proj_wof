using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Benchmarks;

internal sealed record DensityField(Province Province, ProvinceBattlefieldLayout Layout);
internal sealed record DensityBattle(DensityField Field, TacticalBattle Battle,
    TacticalHourlyCombatOrchestrator Orchestrator, TacticalSectorOccupationControl Control,
    TacticalPerceptionState Perception, TacticalPhaseTimingRecorder Timer, int Lanes);

internal static class TacticalDensityScenarioFactory
{
    internal const ulong Seed = 20261125;
    private static readonly Country West = new(1, "Ouest");
    private static readonly Country East = new(2, "Est");

    internal static DensityField[] Fields(int count)
    {
        var provinces = Enumerable.Range(1, count)
            .Select(i => new Province(i, $"Province {i}", TerrainType.Plains, West)).ToArray();
        for (int i = 1; i < count; i++)
            provinces[i - 1].ConnectTo(provinces[i]);

        return provinces.Select(p =>
        {
            var borders = p.Neighbours.Select(n => new ProvinceNeighborBorder(n.Id,
                n.Id < p.Id ? 270 : 90, 1));
            var grid = ProvinceTerrainGenerator.Generate(ProvinceTerrainDescriptor.FromProvince(p, Seed, borders));
            var partition = GrandSectorPartition.Build(grid);
            return new DensityField(p, TacticalBattlefieldBuilder.Build(grid, partition,
                partition.Sectors.Select(s => s.Id)));
        }).ToArray();
    }

    internal static DensityBattle Create(DensityField field, int total)
    {
        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(West, East);
        var engagement = new Engagement(field.Province.Id, 0, field.Province);
        var battle = new TacticalBattle(engagement, field.Layout.Battlefield);
        var map = new TacticalGeometryMap(battle);
        field.Layout.DefineGeometry(map);
        var signatures = new TacticalSignatureFrame(battle);
        var grid = field.Layout.Grid;
        int perSide = total / 2;
        int lanes = Math.Min(70, (perSide + 9) / 10);

        void Deploy(Country country, bool west)
        {
            for (int start = 0; start < perSide; start += 48)
            {
                int size = Math.Min(48, perSide - start);
                var division = new Division($"{country.Name}-{start}", field.Province, 100, 100,
                    country: country, composition: new DivisionComposition(size * 100, size * 100, 0, 0));
                for (int group = 0; group < size; group += 4)
                {
                    var battalion = new Battalion(group / 4 + 1, $"B{group}", BattalionType.Infantry);
                    for (int j = 0; j < Math.Min(4, size - group); j++)
                        battalion.AddCompany(new Company(j + 1, $"C{j}", CompanyType.Infantry,
                            new CompanyComposition(manpower: 100, infantryEquipment: 100,
                                ammunition: new AmmunitionTotals(smallArms: 1000))));
                    division.AddBattalion(battalion);
                }
                division.EnableHierarchicalResources();
                engagement.AddParticipant(division, 0);
                int i = start;
                foreach (var battalion in division.Battalions)
                foreach (var company in battalion.Companies)
                {
                    int depth = i / lanes;
                    int col = grid.Columns / 2 + (west ? -1 - depth : depth);
                    int row = (grid.Rows - (2 * lanes - 1)) / 2 + 2 * (i % lanes);
                    var sector = field.Layout.GetSector(row * grid.Columns + col);
                    var role = i < perSide / 2 ? CompanyDeploymentRole.FrontLine :
                        i < 3 * perSide / 4 ? CompanyDeploymentRole.Support : CompanyDeploymentRole.Reserve;
                    var deployment = battle.DeployCompany(company, sector, role);
                    map.PlaceCompany(deployment, field.Layout.GetCellCenter(sector), west ? 90 : 270, 300);
                    signatures.Set(deployment, new(TacticalActivity.Stationary, TacticalConcealment.None));
                    battle.PersonnelExposure.Set(deployment, new(25, 0, 75, 300));
                    i++;
                }
            }
        }

        Deploy(West, true);
        Deploy(East, false);
        var timer = new TacticalPhaseTimingRecorder();
        var perception = new TacticalPerceptionState(battle);
        var settings = new TacticalHourlyCombatSettings(
            new TacticalDetectionProfile(500, 0, 160, 50), new TacticalDirectFireProfile(500, 160),
            new TacticalHitProfile(2, 1, 0.5), new TacticalDefenseProfile(1, 0.75, 0.5, 0, 0, 0),
            new TacticalPersonnelEffectProfile(0.2, 0.3), 10, 3, Seed + (ulong)field.Province.Id,
            suppression: new TacticalSuppressionProfile(10, 2, 10, 0.8),
            cohesion: new TacticalCohesionProfile(1.5, 40, 0.1, 3, 20, 0.5),
            battalionAssessment: new TacticalBattalionAssessmentProfile(3, 2, 1, 2, 60, 30),
            battalionReactions: new TacticalBattalionReactionProfile(2, 2, 10));
        var orchestrator = new TacticalHourlyCombatOrchestrator(battle, map, perception, signatures,
            diplomacy, settings, timer);
        return new DensityBattle(field, battle, orchestrator, new(map, diplomacy), perception, timer, lanes);
    }
}