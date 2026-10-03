using WoF.Simulation.Core;
using WoF.Simulation.Military;
using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Client.Prototype;

public sealed class PrototypeScenario
{
    public SimulationEngine Simulation { get; }

    public Country PlayerCountry { get; }

    public Country EnemyCountry { get; }

    public IReadOnlyList<MapProvince> Provinces { get; }

    public IReadOnlyList<InfrastructureLink> Links { get; }

    public IReadOnlyList<Division> PlayerDivisions =>
        Simulation.Divisions
            .Where(
                division =>
                    ReferenceEquals(
                        division.Country,
                        PlayerCountry
                    )
            )
            .ToList();

    private PrototypeScenario(
        SimulationEngine simulation,
        Country playerCountry,
        Country enemyCountry,
        IReadOnlyList<MapProvince> provinces,
        IReadOnlyList<InfrastructureLink> links)
    {
        Simulation = simulation;
        PlayerCountry = playerCountry;
        EnemyCountry = enemyCountry;
        Provinces = provinces;
        Links = links;
    }

    public static PrototypeScenario Create()
    {
        var france =
            new Country(
                1,
                "France"
            );

        var germany =
            new Country(
                2,
                "Germany"
            );

        var west =
            new Province(
                1,
                "West",
                TerrainType.Plains,
                owner: france
            );

        var north =
            new Province(
                2,
                "North",
                TerrainType.Forest,
                owner: france
            );

        var highlands =
            new Province(
                3,
                "Highlands",
                TerrainType.Mountain,
                owner: france
            );

        var south =
            new Province(
                4,
                "South",
                TerrainType.Plains,
                owner: france
            );

        var central =
            new Province(
                5,
                "Central",
                TerrainType.Urban,
                owner: france
            );

        var east =
            new Province(
                6,
                "East",
                TerrainType.Hills,
                owner: france
            );

        var borderWest =
            new Province(
                7,
                "Border West",
                TerrainType.Forest,
                owner: germany
            );

        var borderCentral =
            new Province(
                8,
                "Border Central",
                TerrainType.Plains,
                owner: germany
            );

        var borderEast =
            new Province(
                9,
                "Border East",
                TerrainType.Hills,
                owner: germany
            );


        var simulation =
            new SimulationEngine();

        var links =
            new List<InfrastructureLink>();


        void Connect(
            Province first,
            Province second,
            int level = 2)
        {
            first.ConnectTo(
                second
            );

            var link =
                new InfrastructureLink(
                    first,
                    second,
                    InfrastructureType.Road,
                    level
                );

            links.Add(
                link
            );

            simulation.AddInfrastructureLink(
                link
            );
        }


        Connect(west, north);
        Connect(west, south);

        Connect(north, highlands);
        Connect(north, central);

        Connect(south, central);
        Connect(south, borderWest);

        Connect(central, east);
        Connect(central, borderCentral);

        Connect(highlands, east);

        Connect(east, borderEast);

        Connect(borderWest, borderCentral);
        Connect(borderCentral, borderEast);


        simulation.DeclareWar(
            france,
            germany
        );


        simulation.AddDivision(
            new Division(
                "1re Division",
                west,
                fuel: 100,
                ammunition: 100,
                type: DivisionType.Infantry,
                country: france
            )
        );

        simulation.AddDivision(
            new Division(
                "2e Division",
                north,
                fuel: 100,
                ammunition: 100,
                type: DivisionType.Infantry,
                country: france
            )
        );

        simulation.AddDivision(
            new Division(
                "1re Division blindée",
                central,
                fuel: 100,
                ammunition: 100,
                type: DivisionType.Armored,
                country: france
            )
        );


        simulation.AddDivision(
            new Division(
                "15. Infanterie",
                borderCentral,
                fuel: 100,
                ammunition: 100,
                type: DivisionType.Infantry,
                country: germany
            )
        );

        simulation.AddDivision(
            new Division(
                "3. Panzer",
                borderEast,
                fuel: 100,
                ammunition: 100,
                type: DivisionType.Armored,
                country: germany
            )
        );


        var provinces =
            new List<MapProvince>
            {
                new(west,          100, 140),
                new(north,         300, 100),
                new(highlands,     530, 130),

                new(south,         130, 330),
                new(central,       350, 300),
                new(east,          580, 310),

                new(borderWest,    170, 510),
                new(borderCentral, 400, 500),
                new(borderEast,    650, 500)
            };


        return new PrototypeScenario(
            simulation,
            france,
            germany,
            provinces,
            links
        );
    }
}