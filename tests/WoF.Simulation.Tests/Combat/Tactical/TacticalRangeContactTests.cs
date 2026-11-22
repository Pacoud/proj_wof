using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using static WoF.Simulation.Tests.Combat.Tactical.TacticalGridTestFactory;

namespace WoF.Simulation.Tests.Combat.Tactical;

/// <summary>
/// Étape 2b : paires candidates au contact choisies selon la portée de
/// détection, au-delà des secteurs voisins, dans un ordre déterministe.
/// Sur la grille, deux cases voisines ne sont distantes que de 200 m.
/// </summary>
public sealed class TacticalRangeContactTests
{
    /// <summary>Grille plate de 30 colonnes sur 9 rangées, entièrement incluse.</summary>
    private static GridBattle FlatBattle() =>
        CreateBattle(CreateGrid(30, 9, (_, _) => 0));

    private static TacticalDetectionProfile Profile(
        double visual = 500,
        double acoustic = 0) =>
        new(visual, acoustic, fieldOfViewDegrees: 360, localizationCellMeters: 1);

    private static IReadOnlyList<TacticalContactCandidate> Candidates(
        GridBattle b,
        TacticalDetectionProfile profile) =>
        TacticalContactDetector.Detect(
            b.Battle, b.Diplomacy, b.Map, profile.MaxDetectionRangeMeters);

    [Fact]
    public void NonAdjacentCellsWithinRange_FormCandidates_AfterTheTopologicalPairs()
    {
        // Compagnies f0, f1, g0, g1, g2, déployées dans cet ordre ou dans
        // l'ordre inverse.
        static (GridBattle Battle, CompanyDeployment[] Companies) Build(bool reversed = false)
        {
            var b = FlatBattle();

            var steps = new Func<CompanyDeployment>[]
            {
                () => Place(b, b.French, 0, column: 10, row: 4),
                () => Place(b, b.French, 1, column: 12, row: 4),
                () => Place(b, b.German, 0, column: 10, row: 4, dx: 20, facingDegrees: 270),
                () => Place(b, b.German, 1, column: 11, row: 4, facingDegrees: 270),
                () => Place(b, b.German, 2, column: 16, row: 4, facingDegrees: 270)
            };

            var companies = new CompanyDeployment[steps.Length];

            for (int k = 0; k < steps.Length; k++)
            {
                int i = reversed ? steps.Length - 1 - k : k;
                companies[i] = steps[i]();
            }

            return (b, companies);
        }

        var (battle, c) = Build();
        var (f0, f1, g0, g1, g2) = (c[0], c[1], c[2], c[3], c[4]);

        var candidates = Candidates(battle, Profile());

        // Les passages topologiques restent en tête, inchangés : ce sont
        // exactement les paires de la détection sans géométrie.
        var topological = TacticalContactDetector.Detect(battle.Battle, battle.Diplomacy);

        Assert.Equal(
            topological.Select(Key),
            candidates.Take(topological.Count).Select(Key));

        Assert.Equal(
            new[]
            {
                TacticalContactTopology.SameSector,
                TacticalContactTopology.ConnectedSectors,
                TacticalContactTopology.ConnectedSectors
            },
            topological.Select(x => x.Topology));

        // Puis les paires à portée, triées par identifiant de secteur ;
        // la première compagnie est celle du secteur le plus petit.
        // f0-g2 : 1 200 m ; f1-g0 : 380 m ; f1-g2 : 800 m. Portée de
        // sélection : 2,5 × 500 m + 10 m + 10 m.
        Assert.Collection(
            candidates.Skip(topological.Count),
            x => AssertRange(x, g0, f1),
            x => AssertRange(x, f0, g2),
            x => AssertRange(x, f1, g2));

        // Même suite d'une construction à l'autre.
        var (again, _) = Build();

        Assert.Equal(
            candidates.Select(Describe),
            Candidates(again, Profile()).Select(Describe));

        // Déployées dans l'ordre inverse, l'index visite f1-g2 avant f0-g2 :
        // l'ordre des paires à portée vient du tri par secteur.
        var (reversed, r) = Build(reversed: true);

        Assert.Collection(
            Candidates(reversed, Profile())
                .Where(x => x.Topology == TacticalContactTopology.WithinDetectionRange),
            x => AssertRange(x, r[2], r[1]),
            x => AssertRange(x, r[0], r[4]),
            x => AssertRange(x, r[1], r[4]));
    }

    [Fact]
    public void CompanyWhoseFrontEntersTheRange_IsACandidate_EvenWithItsCenterOutside()
    {
        // Portée visuelle de 100 m : sélection jusqu'à 250 m, plus les
        // demi-fronts (10 m pour l'observateur, 200 m pour la cible).
        static IReadOnlyList<TacticalContactCandidate> Run(int targetColumn, double frontage)
        {
            var b = FlatBattle();

            Place(b, b.French, 0, column: 10, row: 4);
            Place(b, b.German, 0, column: targetColumn, row: 4,
                facingDegrees: 0, frontageMeters: frontage);

            return Candidates(b, Profile(visual: 100));
        }

        // Centres à 400 m : hors de 250 + 10 + 10, dans 250 + 10 + 200.
        var withFront = Assert.Single(Run(targetColumn: 12, frontage: 400));
        Assert.Equal(TacticalContactTopology.WithinDetectionRange, withFront.Topology);

        Assert.Empty(Run(targetColumn: 12, frontage: 20));

        // Centres à 600 m : au-delà de 460 m.
        Assert.Empty(Run(targetColumn: 13, frontage: 400));
    }

    [Fact]
    public void ReserveCompanyWithinRange_IsNotACandidate()
    {
        // Même règle que les passages topologiques : seules les compagnies
        // de première ligne, hors transit et avec du personnel, comptent.
        var b = FlatBattle();

        var observer = Place(b, b.French, 0, column: 10, row: 4);
        var front = Place(b, b.German, 0, column: 14, row: 4, facingDegrees: 270);

        var sector = b.Layout.GetSector(Cell(b.Grid, column: 13, row: 4));
        var reserve = b.Battle.DeployCompany(
            b.German.Battalions[0].Companies[1], sector, CompanyDeploymentRole.Reserve);
        b.Map.PlaceCompany(reserve, b.Layout.GetCellCenter(sector), 270, 20);

        var candidate = Assert.Single(Candidates(b, Profile()));

        Assert.Same(observer, candidate.First);
        Assert.Same(front, candidate.Second);
    }

    [Fact]
    public void SelectionRange_IncludesTheVisualRangeOfAFiringCompany()
    {
        // Compagnies à 1 200 m, portée visuelle de 500 m : une compagnie
        // qui tire est vue jusqu'à 1 250 m. Ni voisines ni reliées, elles
        // ne sont examinées que grâce au rayon de 2,5 fois la portée.
        static bool Located(TacticalActivity activity)
        {
            var b = FlatBattle();

            var observer = Place(b, b.French, 0, column: 10, row: 4);
            var target = Place(b, b.German, 0, column: 16, row: 4, facingDegrees: 270);

            var signatures = new TacticalSignatureFrame(b.Battle);
            signatures.Set(target, new TacticalUnitSignature(activity, TacticalConcealment.None));

            var perception = new TacticalPerceptionState(b.Battle);

            TacticalDetectionSystem.Update(
                b.Battle, b.Map, b.Diplomacy, perception, signatures,
                currentHour: 2, Profile());

            return perception.GetReports(observer).Any(r =>
                r.CurrentObservationLevel == TacticalKnowledgeLevel.Located);
        }

        Assert.Equal(1250, Profile().MaxDetectionRangeMeters);
        Assert.Equal(600, Profile(visual: 200, acoustic: 600).MaxDetectionRangeMeters);

        Assert.True(Located(TacticalActivity.Firing));
        Assert.False(Located(TacticalActivity.Stationary));
    }

    [Fact]
    public void HandBuiltBattlefield_AlsoGetsPairsWithinRange()
    {
        // Deux secteurs non reliés, à 300 m : aucune paire topologique,
        // une paire à portée dès que la géométrie est fournie.
        var s = HandBuilt(secondSectorX: 300);

        Assert.Empty(TacticalContactDetector.Detect(s.Battle, s.Diplomacy));

        var candidate = Assert.Single(
            TacticalContactDetector.Detect(s.Battle, s.Diplomacy, s.Geometry, 500));

        Assert.Equal(TacticalContactTopology.WithinDetectionRange, candidate.Topology);
        Assert.Null(candidate.ConnectionDistanceMeters);
        Assert.Same(s.French, candidate.First);

        var far = HandBuilt(secondSectorX: 600);

        Assert.Empty(TacticalContactDetector.Detect(far.Battle, far.Diplomacy, far.Geometry, 500));
    }

    [Fact]
    public void RangeSelection_RejectsForeignGeometryAndInvalidRange()
    {
        var s = HandBuilt(secondSectorX: 300);
        var other = HandBuilt(secondSectorX: 300);

        Assert.Throws<InvalidOperationException>(() =>
            TacticalContactDetector.Detect(s.Battle, s.Diplomacy, other.Geometry, 500));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TacticalContactDetector.Detect(s.Battle, s.Diplomacy, s.Geometry, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TacticalContactDetector.Detect(s.Battle, s.Diplomacy, s.Geometry, double.NaN));
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(250.0)]
    [InlineData(301.0)]
    [InlineData(1000.0)]
    public void SpatialIndexPairs_MatchABruteForceSearch(double cellSize)
    {
        // 60 compagnies à des points pseudo-aléatoires fixes : pour toute
        // taille de cellule, les paires proches proposées par l'index
        // contiennent toutes les paires à 300 m ou moins, une seule fois,
        // la première compagnie précédant la seconde dans les déploiements.
        const double Radius = 300;
        var s = Scattered(60);

        var index = new TacticalCompanySpatialIndex(s.Battle, s.Geometry, cellSize);

        var visited = new List<(CompanyDeployment, CompanyDeployment)>();
        index.VisitNearbyPairs(Radius, (a, b) => visited.Add((a, b)));

        Assert.Equal(visited.Count, visited.Distinct().Count());

        // Déploiements distincts par référence : leur rang suffit.
        var order = new Dictionary<CompanyDeployment, int>();

        for (int i = 0; i < s.Battle.Deployments.Count; i++)
            order.Add(s.Battle.Deployments[i], i);

        Assert.All(visited, p => Assert.True(order[p.Item1] < order[p.Item2]));

        var found = visited
            .Where(p => Distance(s.Geometry, p.Item1, p.Item2) <= Radius)
            .Select(p => (order[p.Item1], order[p.Item2]))
            .OrderBy(p => p)
            .ToArray();

        var expected = new List<(int, int)>();

        for (int i = 0; i < s.Battle.Deployments.Count; i++)
        {
            for (int j = i + 1; j < s.Battle.Deployments.Count; j++)
            {
                if (Distance(s.Geometry, s.Battle.Deployments[i], s.Battle.Deployments[j]) <= Radius)
                    expected.Add((i, j));
            }
        }

        Assert.NotEmpty(expected);
        Assert.Equal(expected, found);
    }

    [Fact]
    public void SpatialIndexPairs_RejectAnOversizedRadius()
    {
        var s = Scattered(4);
        var index = new TacticalCompanySpatialIndex(s.Battle, s.Geometry, 10);

        Assert.Throws<InvalidOperationException>(() =>
            index.VisitNearbyPairs(500, (_, _) => { }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            index.VisitNearbyPairs(-1, (_, _) => { }));
    }

    private static (CompanyDeployment, CompanyDeployment, TacticalContactTopology, double?) Key(
        TacticalContactCandidate c) =>
        (c.First, c.Second, c.Topology, c.ConnectionDistanceMeters);

    private static string Describe(TacticalContactCandidate c) =>
        $"{CountryName(c.First)}{c.First.Company.Id}@{c.First.Sector.Id}-" +
        $"{CountryName(c.Second)}{c.Second.Company.Id}@{c.Second.Sector.Id}:{c.Topology}";

    private static string CountryName(CompanyDeployment d) =>
        d.Company.ParentBattalion!.ParentDivision!.Country!.Name;

    private static void AssertRange(
        TacticalContactCandidate candidate,
        CompanyDeployment first,
        CompanyDeployment second)
    {
        Assert.Same(first, candidate.First);
        Assert.Same(second, candidate.Second);
        Assert.Equal(TacticalContactTopology.WithinDetectionRange, candidate.Topology);
        Assert.Null(candidate.ConnectionDistanceMeters);
    }

    private static double Distance(
        TacticalGeometryMap geometry,
        CompanyDeployment a,
        CompanyDeployment b) =>
        geometry.GetPosition(a)!.Point.HorizontalDistanceTo(geometry.GetPosition(b)!.Point);

    private sealed record HandBuiltScenario(
        TacticalBattle Battle,
        TacticalGeometryMap Geometry,
        DiplomacySystem Diplomacy,
        CompanyDeployment French,
        CompanyDeployment German);

    /// <summary>
    /// Deux secteurs non reliés, une compagnie française au centre du
    /// premier, une compagnie allemande au centre du second.
    /// </summary>
    private static HandBuiltScenario HandBuilt(double secondSectorX)
    {
        var west = new BattleSector(1, "Ouest", TerrainType.Plains);
        var east = new BattleSector(2, "Est", TerrainType.Plains);

        var (battle, diplomacy, french, german) = CreateHandBattle(1, 1, west, east);

        var frenchDeployment = battle.DeployCompany(
            french.Battalions[0].Companies[0], west, CompanyDeploymentRole.FrontLine);

        var germanDeployment = battle.DeployCompany(
            german.Battalions[0].Companies[0], east, CompanyDeploymentRole.FrontLine);

        var geometry = new TacticalGeometryMap(battle);
        geometry.DefineSector(west, 0, 0, 100);
        geometry.DefineSector(east, secondSectorX, 0, 100);

        geometry.PlaceCompany(frenchDeployment, new TacticalPoint(0, 0, 0), 90, 20);
        geometry.PlaceCompany(germanDeployment, new TacticalPoint(secondSectorX, 0, 0), 270, 20);

        return new HandBuiltScenario(battle, geometry, diplomacy, frenchDeployment, germanDeployment);
    }

    private sealed record ScatteredScenario(TacticalBattle Battle, TacticalGeometryMap Geometry);

    /// <summary>
    /// Compagnies des deux camps réparties dans un grand secteur, à des
    /// points tirés d'une suite fixe (SplitMix64).
    /// </summary>
    private static ScatteredScenario Scattered(int companiesPerSide)
    {
        var sector = new BattleSector(1, "Plaine", TerrainType.Plains);

        var (battle, _, french, german) = CreateHandBattle(companiesPerSide, companiesPerSide, sector);

        var geometry = new TacticalGeometryMap(battle);
        geometry.DefineSector(sector, 0, 0, 5000);

        ulong state = 0x2B992DDFA23249D6UL;

        foreach (var company in french.Battalions[0].Companies.Concat(german.Battalions[0].Companies))
        {
            var deployment = battle.DeployCompany(company, sector, CompanyDeploymentRole.FrontLine);

            double x = (Next(ref state) - 0.5) * 2000;
            double y = (Next(ref state) - 0.5) * 2000;

            geometry.PlaceCompany(deployment, new TacticalPoint(x, y, 0), 90, 20);
        }

        return new ScatteredScenario(battle, geometry);
    }

    private static double Next(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;

        return (z >> 11) * (1.0 / (1UL << 53));
    }

    private static (TacticalBattle, DiplomacySystem, Division, Division) CreateHandBattle(
        int frenchCompanies,
        int germanCompanies,
        params BattleSector[] sectors)
    {
        var france = new Country(1, "F");
        var germany = new Country(2, "G");
        var province = new Province(1, "Province", TerrainType.Plains);

        var french = MakeDivision(france, province, frenchCompanies);
        var german = MakeDivision(germany, province, germanCompanies);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);
        engagements.DetectProvinceEngagements(new[] { french, german }, currentHour: 1);

        var field = new TacticalBattlefield();

        foreach (var sector in sectors)
            field.AddSector(sector);

        var battle = new TacticalBattle(Assert.Single(engagements.ActiveEngagements), field);

        return (battle, diplomacy, french, german);
    }

    private static Division MakeDivision(Country country, Province province, int companies)
    {
        var division = new Division(
            country.Name, province, fuel: 100, ammunition: 100, country: country,
            composition: new DivisionComposition(
                manpower: companies * 100, infantryEquipment: 0, artillery: 0, tanks: 0));

        var battalion = new Battalion(1, "Bataillon", BattalionType.Infantry);

        for (int i = 1; i <= companies; i++)
        {
            battalion.AddCompany(new Company(
                i, $"Compagnie {i}", CompanyType.Infantry, new CompanyComposition(manpower: 100)));
        }

        division.AddBattalion(battalion);
        division.EnableHierarchicalResources();

        return division;
    }
}
