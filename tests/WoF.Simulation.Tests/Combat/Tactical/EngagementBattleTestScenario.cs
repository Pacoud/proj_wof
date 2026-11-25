using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;
using WoF.Simulation.World.Terrain;

namespace WoF.Simulation.Tests.Combat.Tactical;

/// <summary>Deux formations physiques et une grille en cache, réutilisables en 3e.1b.</summary>
internal sealed class EngagementBattleTestScenario
{
    public Country Red { get; } = new(1, "Rouge");
    public Country Blue { get; } = new(2, "Bleu");
    public Province Province { get; }
    public Province Border { get; }
    public DiplomacySystem Diplomacy { get; } = new();
    public EngagementSystem System { get; }
    public Division First { get; }
    public Division Second { get; }
    public Engagement Battle { get; }
    public ProvinceBattlefieldGridCache Cache { get; }
    public ProvinceBattlefieldGrid Field { get; }

    public EngagementBattleTestScenario(bool meeting = false, bool legacySecond = false,
        int menPerCompany = 1, int companiesPerBattalion = 4)
    {
        Province = new Province(1, "Champ", TerrainType.Plains, Blue);
        Border = new Province(2, "Voisine", TerrainType.Plains, Red);
        Province.ConnectTo(Border);
        var catalog = new ProvinceTerrainCatalog(20261125);
        catalog.SetBorders(Province, new[] { new ProvinceNeighborBorder(Border.Id, 90, 1) });
        catalog.SetBorders(Border, new[] { new ProvinceNeighborBorder(Province.Id, 270, 1) });
        Cache = new ProvinceBattlefieldGridCache(catalog);

        First = Formation(meeting ? Border : Province, Red, true, menPerCompany, companiesPerBattalion);
        Second = Formation(Province, Blue, !legacySecond, menPerCompany, companiesPerBattalion);
        Diplomacy.DeclareWar(Red, Blue);
        System = new EngagementSystem(Diplomacy);
        if (meeting)
        {
            // Les deux divisions atteignent le milieu de la liaison pendant la même heure.
            var march = new MovementPlan(DurationHours: 2, FuelPerHour: 0);
            Assert.True(First.TryStartMovement(Province, march));
            Assert.True(Second.TryStartMovement(Border, march));
            First.AdvanceOneHour();
            Second.AdvanceOneHour();
            System.DetectTransitEngagements(new[] { First, Second }, currentHour: 1);
        }
        else
        {
            System.DetectProvinceEngagements(new[] { First, Second }, currentHour: 1);
        }

        Battle = Assert.Single(System.ActiveEngagements);
        Field = Cache.GetOrGenerate(Battle);
    }

    public static Division Formation(Province province, Country country, bool hierarchical = true,
        int menPerCompany = 1, int companiesPerBattalion = 4)
    {
        // Les numéros locaux sont volontairement les mêmes dans les deux divisions.
        int total = checked(companiesPerBattalion * menPerCompany);
        var division = new Division(country.Name, province, 100, 100, country: country,
            composition: new DivisionComposition(manpower: total, infantryEquipment: 0, artillery: 0, tanks: 0));
        var battalion = new Battalion(1, "B1", BattalionType.Infantry);
        for (int i = 1; i <= companiesPerBattalion; i++)
            battalion.AddCompany(new Company(i, $"{country.Name}-{i}", CompanyType.Infantry,
                new CompanyComposition(manpower: menPerCompany)));
        division.AddBattalion(battalion);
        if (hierarchical)
            division.EnableHierarchicalResources();
        return division;
    }

    public static void Empty(Company company)
    {
        var pool = company.Composition!.Manpower;
        pool.ApplyCasualties(new PersonnelCasualtyReport(pool.Current, 0, 0));
    }
}