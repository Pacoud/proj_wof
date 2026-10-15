
using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Composition;
using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Benchmarks;

public static class BenchmarkScenarioFactory
{
    private const int BattalionsPerDivision = 12;
    private const int CompaniesPerBattalion = 4;

    private const int CompaniesPerDivision =
        BattalionsPerDivision * CompaniesPerBattalion;

    public static SimulationEngine Create(
        int divisionCount,
        bool combat,
        int divisionsPerBattle = 2)
    {
        if (divisionCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(divisionCount));

        if (combat && (divisionsPerBattle < 2 ||
                divisionsPerBattle % 2 != 0 ||
                divisionCount % divisionsPerBattle != 0))
                {
                     throw new ArgumentException(
                        "Combat requires even groups that divide the division count.");
                }
           
        var engine = new SimulationEngine();

        var france = new Country(1, "France");
        var germany = new Country(2, "Germany");

        if (combat)
            engine.DeclareWar(france, germany);

        Province? sharedProvince = null;

        for (int i = 0; i < divisionCount; i++)
        {
            Country country =
                i % 2 == 0 ? france : germany;

            Province province;

            if (combat)
            {
                int battleIndex = i / divisionsPerBattle;
                
                if (i % divisionsPerBattle == 0)
                {
                    sharedProvince = new Province(
                        battleIndex + 1,
                        $"Battle Sector { battleIndex + 1}",
                        owner: france);
                }

                province = sharedProvince!;
            }
            else
            {
                // Une province par division :
                // pas de combats artificiels.
                province = new Province(
                    i + 1,
                    $"Sector {i + 1}",
                    owner: country);
            }

            var division = new Division(
                name: $"Division {i + 1}",
                position: province,
                fuel: 100,
                ammunition: 100,
                country: country,
                type: DivisionType.Infantry,

                // Valeur de stress-test :
                // empêche la fin rapide des engagements.
                maxOrganization: 10_000,

                composition: new DivisionComposition(
                    manpower: 10_000,
                    infantryEquipment: 9_000,
                    artillery: 48,
                    tanks: 0,
                    trucks: 300)
            );

            for (int b = 1; b <= BattalionsPerDivision; b++)
            {
                var battalion = new Battalion(
                    b,
                    $"Battalion {b}",
                    BattalionType.Infantry);

                for (int c = 1; c <= CompaniesPerBattalion; c++)
                {
                    int index =
                        (b - 1) * CompaniesPerBattalion
                        + c - 1;

                    var composition = new CompanyComposition(
                        manpower: Split(10_000, index),
                        infantryEquipment: Split(9_000, index),
                        artillery: Split(48, index),
                        tanks: 0,
                        trucks: Split(300, index)
                    );

                    battalion.AddCompany(new Company(
                        c,
                        $"Company {c}",
                        CompanyType.Infantry,
                        composition
                    ));
                }

                division.AddBattalion(battalion);
            }

            // Valide l'égalité des ressources
            // puis active le nouveau modèle.
            division.EnableHierarchicalResources();

            engine.AddDivision(division);
        }

        return engine;
    }

    private static int Split(int total, int index)
    {
        int baseAmount = total / CompaniesPerDivision;
        int remainder = total % CompaniesPerDivision;

        return baseAmount + (index < remainder ? 1 : 0);
    }
}
