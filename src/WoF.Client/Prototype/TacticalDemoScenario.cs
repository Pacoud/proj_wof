
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Client.Prototype;

public static class TacticalDemoScenario
{
    public static TacticalBattleSnapshot CreateSnapshot()
    {
        var france = new Country(1, "France");
        var germany = new Country(2, "Allemagne");

        var province = new Province(
            100, "Province de démonstration",
            TerrainType.Hills);

        var french = CreateDivision(
            "21e Division", france, province);

        var german = CreateDivision(
            "15. Infanterie", germany, province);

        var diplomacy = new DiplomacySystem();
        diplomacy.DeclareWar(france, germany);

        var engagements = new EngagementSystem(diplomacy);

        engagements.DetectProvinceEngagements(
            new[] { french, german },
            currentHour: 1);

        var engagement =
            engagements.ActiveEngagements.Single();

        var battlefield = new TacticalBattlefield();

        var hill = new BattleSector(
            1, "Colline", TerrainType.Hills,
            elevationMeters: 150,
            features: SectorFeature.Entrenchments);

        var forest = new BattleSector(
            2, "Forêt", TerrainType.Forest,
            features: SectorFeature.Woods);

        var village = new BattleSector(
            3, "Village", TerrainType.Urban,
            features: SectorFeature.Buildings);

        var rear = new BattleSector(
            4, "Arrière", TerrainType.Plains,
            features: SectorFeature.Road);

        battlefield.AddSector(hill);
        battlefield.AddSector(forest);
        battlefield.AddSector(village);
        battlefield.AddSector(rear);

        battlefield.Connect(hill, forest, 800);
        battlefield.Connect(hill, village, 1200);
        battlefield.Connect(forest, rear, 600);
        battlefield.Connect(village, rear, 900);

        var battle = new TacticalBattle(
            engagement, battlefield);

        var frenchOrder = new DivisionTacticalOrder(
            french,
            TacticalPosture.Defend,
            hill,
            new[] { forest, village },
            rear,
            reserveBattalions: 1);

        var germanOrder = new DivisionTacticalOrder(
            german,
            TacticalPosture.Attack,
            village,
            new[] { forest, hill },
            rear,
            reserveBattalions: 1);

        TacticalCommandSystem.ApplyPlan(
            TacticalCommandSystem.CreatePlan(
                battle, frenchOrder));

        TacticalCommandSystem.ApplyPlan(
            TacticalCommandSystem.CreatePlan(
                battle, germanOrder));

        return TacticalBattleSnapshotBuilder.Capture(battle);
    }

    private static Division CreateDivision(
        string name,
        Country country,
        Province province)
    {
        const int battalionCount = 4;
        const int companiesPerBattalion = 4;
        const int personnelPerCompany = 100;

        var division = new Division(
            name,
            province,
            fuel: 100,
            ammunition: 100,
            country: country,
            composition: new DivisionComposition(
                manpower:
                    battalionCount *
                    companiesPerBattalion *
                    personnelPerCompany,
                infantryEquipment: 0,
                artillery: 0,
                tanks: 0));

        for (int b = 1; b <= battalionCount; b++)
        {
            var battalion = new Battalion(
                b,
                $"Bataillon {b}",
                BattalionType.Infantry);

            for (int c = 1; c <= companiesPerBattalion; c++)
            {
                battalion.AddCompany(new Company(
                    c,
                    $"Compagnie {c}",
                    CompanyType.Infantry,
                    new CompanyComposition(
                        manpower: personnelPerCompany)));
            }

            division.AddBattalion(battalion);
        }

        division.EnableHierarchicalResources();

        return division;
    }
}
