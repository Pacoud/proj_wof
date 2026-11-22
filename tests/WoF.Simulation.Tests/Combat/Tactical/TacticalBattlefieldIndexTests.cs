using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.Tests.World.Terrain;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.Combat.Tactical;

/// <summary>
/// Index des liaisons par secteur : mêmes voisins, mêmes candidats au
/// contact et dans le même ordre que le parcours de la liste plate ;
/// une province entière se construit et se parcourt.
/// </summary>
public sealed class TacticalBattlefieldIndexTests
{
    /// <summary>
    /// Sept secteurs créés dans le désordre de leurs identifiants, liaisons
    /// créées dans le désordre et parfois orientées du plus grand au plus petit.
    /// </summary>
    private static (TacticalBattlefield Field, BattleSector[] Sectors) CreateGraph()
    {
        var field = new TacticalBattlefield();

        var sectors = new[] { 5, 2, 7, 1, 4, 3, 6 }
            .Select(id => new BattleSector(id, $"S{id}", TerrainType.Plains))
            .ToArray();

        foreach (var sector in sectors)
            field.AddSector(sector);

        BattleSector S(int id) => sectors.Single(s => s.Id == id);

        var links = new (int A, int B, double D)[]
        {
            (4, 1, 300), (2, 5, 200), (7, 3, 450), (1, 2, 150),
            (6, 4, 500), (3, 1, 250), (5, 7, 100), (2, 6, 350), (4, 3, 120)
        };

        foreach (var (a, b, d) in links)
            field.Connect(S(a), S(b), d);

        return (field, sectors);
    }

    [Fact]
    public void Neighbors_FollowTheOrderOfTheConnectionList()
    {
        var (field, sectors) = CreateGraph();

        foreach (var sector in sectors)
        {
            // Définition historique : parcours de toute la liste plate.
            var expected = field.Connections
                .Where(c => ReferenceEquals(c.A, sector) || ReferenceEquals(c.B, sector))
                .Select(c => ReferenceEquals(c.A, sector) ? c.B : c.A)
                .ToArray();

            Assert.Equal(expected, field.GetNeighbors(sector));

            foreach (var other in sectors)
            {
                var connection = field.Connections.SingleOrDefault(c => c.Connects(sector, other));

                Assert.Equal(connection is not null, field.AreConnected(sector, other));
                Assert.Equal(connection is not null, field.TryGetConnection(sector, other, out var found));
                Assert.Same(connection, connection is null ? null : found);
            }
        }
    }

    [Fact]
    public void ForeignOrMissingSectors_AreNeverConnected()
    {
        var (field, sectors) = CreateGraph();
        var foreign = new BattleSector(2, "S2 d'une autre bataille", TerrainType.Plains);
        var two = sectors.Single(s => s.Id == 2);
        var five = sectors.Single(s => s.Id == 5);

        Assert.True(field.AreConnected(two, five));
        Assert.False(field.AreConnected(foreign, five));
        Assert.False(field.AreConnected(two, two));
        Assert.False(field.AreConnected(null!, five));

        Assert.Throws<InvalidOperationException>(() => field.GetNeighbors(foreign));
        Assert.Throws<InvalidOperationException>(() => field.GetConnections(foreign));

        // Doublon dans l'autre sens : toujours refusé.
        Assert.Throws<InvalidOperationException>(() => field.Connect(five, two, 200));
        Assert.Equal(9, field.Connections.Count);
    }

    [Fact]
    public void ContactCandidates_KeepTheHistoricalOrder()
    {
        var (field, sectors) = CreateGraph();
        BattleSector S(int id) => sectors.Single(s => s.Id == id);

        var (battle, diplomacy, french, german) = CreateBattle(field);

        // Français et Allemands répartis sur plusieurs secteurs, dont un
        // secteur mixte, un secteur français seul et une réserve.
        var placements = new (Division Division, int Company, int Sector, CompanyDeploymentRole Role)[]
        {
            (french, 0, 1, CompanyDeploymentRole.FrontLine),
            (german, 0, 1, CompanyDeploymentRole.FrontLine),
            (french, 1, 2, CompanyDeploymentRole.FrontLine),
            (german, 1, 4, CompanyDeploymentRole.FrontLine),
            (german, 2, 3, CompanyDeploymentRole.FrontLine),
            (french, 2, 7, CompanyDeploymentRole.FrontLine),
            (german, 3, 5, CompanyDeploymentRole.FrontLine),
            (french, 3, 6, CompanyDeploymentRole.Reserve),
            (french, 4, 1, CompanyDeploymentRole.FrontLine),
        };

        foreach (var (division, company, sector, role) in placements)
            battle.DeployCompany(division.Battalions[0].Companies[company], S(sector), role);

        var actual = TacticalContactDetector.Detect(battle, diplomacy)
            .Select(c => (c.First, c.Second, c.Topology, c.ConnectionDistanceMeters))
            .ToArray();

        var expected = HistoricalCandidates(battle, diplomacy);

        Assert.NotEmpty(expected);
        Assert.Contains(expected, c => c.Item3 == TacticalContactTopology.SameSector);
        Assert.Contains(expected, c => c.Item3 == TacticalContactTopology.ConnectedSectors);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Reproduction du parcours d'avant l'index : tous les secteurs par
    /// identifiant, puis toutes les liaisons par identifiants (min, max).
    /// </summary>
    private static (CompanyDeployment, CompanyDeployment, TacticalContactTopology, double?)[]
        HistoricalCandidates(TacticalBattle battle, DiplomacySystem diplomacy)
    {
        var bySector = battle.Battlefield.Sectors.ToDictionary(
            s => s,
            _ => new List<CompanyDeployment>(),
            (IEqualityComparer<BattleSector>)ReferenceEqualityComparer.Instance);

        foreach (var deployment in battle.Deployments)
        {
            if (!deployment.IsInTransit
                && deployment.Role == CompanyDeploymentRole.FrontLine
                && deployment.Company.Composition!.Manpower.Current > 0)
            {
                bySector[deployment.Sector].Add(deployment);
            }
        }

        bool Enemies(CompanyDeployment a, CompanyDeployment b) =>
            diplomacy.AreAtWar(
                a.Company.ParentBattalion!.ParentDivision!.Country!,
                b.Company.ParentBattalion!.ParentDivision!.Country!);

        var result = new List<(CompanyDeployment, CompanyDeployment, TacticalContactTopology, double?)>();

        foreach (var sector in battle.Battlefield.Sectors.OrderBy(s => s.Id))
        {
            var units = bySector[sector];

            for (int i = 0; i < units.Count; i++)
                for (int j = i + 1; j < units.Count; j++)
                    if (Enemies(units[i], units[j]))
                        result.Add((units[i], units[j], TacticalContactTopology.SameSector, null));
        }

        foreach (var connection in battle.Battlefield.Connections
                     .OrderBy(c => Math.Min(c.A.Id, c.B.Id))
                     .ThenBy(c => Math.Max(c.A.Id, c.B.Id)))
        {
            foreach (var first in bySector[connection.A])
                foreach (var second in bySector[connection.B])
                    if (Enemies(first, second))
                        result.Add((first, second, TacticalContactTopology.ConnectedSectors, connection.DistanceMeters));
        }

        return result.ToArray();
    }

    [Fact]
    public void WholeProvince_IsBuiltAndCrossedByTheShortestRoute()
    {
        // Province de plaine traversée par une rivière (graine 1).
        var grid = ProvinceTerrainGenerator.Generate(
            ProvinceTerrainGeneratorTests.Descriptor(worldSeed: 1));

        var partition = GrandSectorPartition.Build(grid);

        var layout = TacticalBattlefieldBuilder.Build(
            grid, partition, partition.Sectors.Select(s => s.Id));

        int expectedConnections = 0;

        for (int cell = 0; cell < grid.CellCount; cell++)
            for (int d = 0; d < 3; d++)
                if (grid.CanCross(cell, d))
                    expectedConnections++;

        Assert.Equal(grid.CellCount, layout.Battlefield.Sectors.Count);
        Assert.Equal(expectedConnections, layout.Battlefield.Connections.Count);

        // D'un bord à l'autre, au milieu du cadre.
        int row = grid.Rows / 2;
        var origin = layout.GetSector(row * grid.Columns);
        var destination = layout.GetSector(row * grid.Columns + grid.Columns - 1);

        var route = TacticalRouteFinder.Find(layout.Battlefield, origin, destination, _ => true);

        Assert.Same(origin, route.Sectors[0]);
        Assert.Same(destination, route.Sectors[^1]);

        for (int i = 1; i < route.Sectors.Count; i++)
            Assert.True(layout.Battlefield.AreConnected(route.Sectors[i - 1], route.Sectors[i]));

        Assert.Equal(200.0 * (route.Sectors.Count - 1), route.TotalDistanceMeters, 6);

        // Toutes les liaisons font 200 m : le plus court chemin est celui
        // qui franchit le moins de bords, rivières contournées par les ponts.
        Assert.Equal(
            CountCrossings(grid, layout.GetCellIndex(origin), layout.GetCellIndex(destination)),
            route.Sectors.Count - 1);

        // Déterministe : même itinéraire à la seconde recherche.
        var again = TacticalRouteFinder.Find(layout.Battlefield, origin, destination, _ => true);
        Assert.Equal(route.Sectors, again.Sectors);
    }

    private static int CountCrossings(ProvinceHexGrid grid, int origin, int destination)
    {
        var distance = new int[grid.CellCount];
        Array.Fill(distance, -1);
        distance[origin] = 0;

        var queue = new Queue<int>();
        queue.Enqueue(origin);

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();

            for (int d = 0; d < HexCoord.DirectionCount; d++)
            {
                if (!grid.CanCross(cell, d))
                    continue;

                int next = grid.GetNeighborIndex(cell, d);

                if (distance[next] >= 0)
                    continue;

                distance[next] = distance[cell] + 1;
                queue.Enqueue(next);
            }
        }

        return distance[destination];
    }

    private static (TacticalBattle Battle, DiplomacySystem Diplomacy, Division French, Division German)
        CreateBattle(TacticalBattlefield field)
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");
        var province = new Province(1, "Province test", TerrainType.Plains);

        var french = MakeDivision("Division française", france, province);
        var german = MakeDivision("Division allemande", germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);
        engagements.DetectProvinceEngagements(new[] { french, german }, currentHour: 1);

        return (
            new TacticalBattle(Assert.Single(engagements.ActiveEngagements), field),
            diplomacy,
            french,
            german);
    }

    private static Division MakeDivision(string name, Country country, Province province)
    {
        var division = new Division(
            name,
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower: 500,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        var battalion = new Battalion(1, "Bataillon 1", BattalionType.Infantry);

        for (int i = 1; i <= 5; i++)
        {
            battalion.AddCompany(new Company(
                i,
                $"Compagnie {i}",
                CompanyType.Infantry,
                new CompanyComposition(manpower: 100)));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }
}
