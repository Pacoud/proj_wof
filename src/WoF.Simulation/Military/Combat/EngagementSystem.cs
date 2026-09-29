using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat;

public sealed class EngagementSystem
{
    private readonly DiplomacySystem _diplomacy;

    private readonly List<Engagement> _engagements = new();

    private int _nextEngagementId = 1;

    public IReadOnlyList<Engagement> Engagements =>
        _engagements;

    public IEnumerable<Engagement> ActiveEngagements =>
        _engagements.Where(
            engagement => engagement.IsActive
        );

    public EngagementSystem(
        DiplomacySystem diplomacy)
    {
        _diplomacy = diplomacy;
    }

    private bool AreHostile(
        Division first,
        Division second)
    {
        if (first.Country == null)
            return false;

        if (second.Country == null)
            return false;

        return _diplomacy.AreAtWar(
            first.Country,
            second.Country
        );
    }

    public void DetectProvinceEngagements(
    IEnumerable<Division> divisions,
    long currentHour)
    {
        var stationedDivisions =
            divisions
                .Where(
                    division =>
                        division.CurrentProvince != null 
                        && !division.IsBroken
                )
                .ToList();

        foreach (var group in stationedDivisions
                    .GroupBy(
                        division =>
                            division.CurrentProvince!
                    ))
        {
            Province province = group.Key;

            var divisionsHere =
                group.ToList();

            // Existe-t-il déjà une bataille ici ?
            Engagement? existing =
                _engagements.FirstOrDefault(
                    engagement =>
                        engagement.IsActive
                        &&
                        engagement.LocationType
                            == EngagementLocationType.Province
                        &&
                        ReferenceEquals(
                            engagement.Province,
                            province
                        )
                );

            if (existing != null)
            {
                AddReinforcementsToProvinceEngagement(
                    existing,
                    divisionsHere
                );

                continue;
            }

            var involved =
                divisionsHere
                    .Where(
                        division =>
                            !division.IsEngaged
                            &&
                            divisionsHere.Any(
                                other =>
                                    !ReferenceEquals(
                                        division,
                                        other
                                    )
                                    &&
                                    AreHostile(
                                        division,
                                        other
                                    )
                            )
                    )
                    .ToList();

            if (involved.Count < 2)
                continue;

            var engagement =
                new Engagement(
                    _nextEngagementId++,
                    currentHour,
                    province
                );

            foreach (var division in involved)
            {
                engagement.AddParticipant(
                    division
                );
            }

            _engagements.Add(
                engagement
            );
        }
    }


    private void AddReinforcementsToProvinceEngagement(
    Engagement engagement,
    IEnumerable<Division> divisions)
    {
        foreach (var candidate in divisions)
        {
            if (candidate.IsEngaged)
                continue;
            
            if (candidate.IsBroken)
                continue;

            bool hostileToParticipant =
                engagement.Participants.Any(
                    participant =>
                        AreHostile(
                            candidate,
                            participant
                        )
                );

            if (hostileToParticipant)
            {
                engagement.AddParticipant(
                    candidate
                );
            }
        }
    }

    public void DetectTransitEngagements(
    IEnumerable<Division> divisions,
    long currentHour)
    {
        var moving =
            divisions
                .Where(
                    division =>
                        division.Transit != null
                        &&
                        !division.IsEngaged
                        && 
                        !division.IsBroken
                )
                .ToList();

        for (int i = 0; i < moving.Count; i++)
        {
            for (int j = i + 1;
                j < moving.Count;
                j++)
            {
                Division first =
                    moving[i];

                Division second =
                    moving[j];

                if (first.IsEngaged
                    || second.IsEngaged)
                {
                    continue;
                }

                if (!AreHostile(
                        first,
                        second))
                {
                    continue;
                }

                if (!AreTravellingOppositeDirections(
                        first,
                        second))
                {
                    continue;
                }

                if (!DidCrossDuringTick(
                        first,
                        second))
                {
                    continue;
                }

                var engagement =
                    new Engagement(
                        _nextEngagementId++,
                        currentHour,
                        first.Transit!.Origin,
                        first.Transit.Destination
                    );

                engagement.AddParticipant(
                    first
                );

                engagement.AddParticipant(
                    second
                );

                _engagements.Add(
                    engagement
                );
            }
        }
    }


    private static bool AreTravellingOppositeDirections(
    Division first,
    Division second)
    {
        if (first.Transit == null
            || second.Transit == null)
        {
            return false;
        }

        return
            ReferenceEquals(
                first.Transit.Origin,
                second.Transit.Destination
            )
            &&
            ReferenceEquals(
                first.Transit.Destination,
                second.Transit.Origin
            );
    }

    private static bool DidCrossDuringTick(
        Division first,
        Division second)
    {
        TransitState firstTransit =
            first.Transit!;

        TransitState secondTransit =
            second.Transit!;

        // Référentiel : Origin de first = 0,
        // Destination de first = 1.

        double firstPrevious =
            firstTransit.PreviousProgress;

        double firstCurrent =
            firstTransit.Progress;

        // second se déplace dans le sens inverse.
        double secondPrevious =
            1.0
            - secondTransit.PreviousProgress;

        double secondCurrent =
            1.0
            - secondTransit.Progress;

        return
            firstPrevious <= secondPrevious
            &&
            firstCurrent >= secondCurrent;
    }


    public void ProcessEngagements()
    {
        foreach (var engagement in
                ActiveEngagements.ToList())
        {
            ProcessEngagementTick(
                engagement
            );
        }
    }

    private static double GetCombatFuelPerHour(
    DivisionType type)
    {
        return type switch
        {
            DivisionType.Infantry => 0.8,
            DivisionType.Motorized => 3.0,
            DivisionType.Armored => 5.0,

            _ => 0.8
        };
    }

    private static double GetBaseCombatPressure(
    DivisionType type)
    {
        return type switch
        {
            DivisionType.Infantry => 6.0,
            DivisionType.Motorized => 7.5,
            DivisionType.Armored => 10.0,

            _ => 6.0
        };
    }

    private static double GetMinimumFuelEffectiveness(
    DivisionType type)
    {
        return type switch
        {
            DivisionType.Infantry => 0.85,
            DivisionType.Motorized => 0.50,
            DivisionType.Armored => 0.25,

            _ => 0.85
        };
    }


    private static double CalculateFuelEffectiveness(
    Division division,
    double requestedFuel,
    double consumedFuel)
    {
        if (requestedFuel <= 0)
            return 1.0;

        double fuelSatisfaction =
            Math.Clamp(
                consumedFuel / requestedFuel,
                0.0,
                1.0
            );

        double minimum =
            GetMinimumFuelEffectiveness(
                division.Type
            );

        return minimum
            + (1.0 - minimum)
            * fuelSatisfaction;
    }

    private double ProcessDivisionCombatFuel(
    Division division)
    {
        double requestedFuel =
            GetCombatFuelPerHour(
                division.Type
            );

        double consumedFuel =
            division.ConsumeFuel(
                requestedFuel
            );

        double fuelEffectiveness =
            CalculateFuelEffectiveness(
                division,
                requestedFuel,
                consumedFuel
            );

        return Math.Round(
            GetBaseCombatPressure(
                division.Type
            )
            * fuelEffectiveness,
            2
        );
    }

    private void ProcessEngagementTick(
    Engagement engagement)
    {
        var participants =
            engagement.Participants
                .Where(
                    division =>
                        ReferenceEquals(
                            division.CurrentEngagement,
                            engagement
                        )
                )
                .ToList();

        if (participants.Count < 2)
        {
            ResolveEngagementIfPossible(
                engagement,
                participants
            );

            return;
        }

        var pressures =
            new Dictionary<Division, double>();

        // Première phase :
        // calcul simultané de la puissance disponible.
        foreach (var division in participants)
        {
            if (division.IsBroken)
            {
                pressures[division] = 0;
                continue;
            }

            pressures[division] =
                ProcessDivisionCombatFuel(
                    division
                );
        }

        var organizationLosses =
            new Dictionary<Division, double>();

        // Deuxième phase :
        // calcul des pertes d'organisation.
        foreach (var division in participants)
        {
            if (division.IsBroken
                || division.Country == null)
            {
                organizationLosses[division] = 0;
                continue;
            }

            double hostilePressure =
                participants
                    .Where(
                        other =>
                            !other.IsBroken
                            &&
                            AreHostile(
                                division,
                                other
                            )
                    )
                    .Sum(
                        other =>
                            pressures[other]
                    );

            int friendlyTargets =
                participants.Count(
                    other =>
                        !other.IsBroken
                        &&
                        other.Country != null
                        &&
                        other.Country.Id
                            == division.Country.Id
                );

            if (friendlyTargets <= 0)
            {
                organizationLosses[division] = 0;
                continue;
            }

            organizationLosses[division] =
                Math.Round(
                    hostilePressure
                    / friendlyTargets,
                    2
                );
        }

        // Troisième phase :
        // application simultanée.
        foreach (var pair in organizationLosses)
        {
            pair.Key.LoseOrganization(
                pair.Value
            );
        }

        ResolveEngagementIfPossible(
            engagement,
            participants
        );
    }


    private void ResolveEngagementIfPossible(
    Engagement engagement,
    IReadOnlyCollection<Division> participants)
    {
        var combatCapableCountries =
            participants
                .Where(
                    division =>
                        !division.IsBroken
                        &&
                        division.Country != null
                )
                .Select(
                    division =>
                        division.Country!
                )
                .GroupBy(
                    country =>
                        country.Id
                )
                .Select(
                    group =>
                        group.First()
                )
                .ToList();

        if (combatCapableCountries.Count > 1)
            return;

        Country? winner =
            combatCapableCountries.Count == 1
                ? combatCapableCountries[0]
                : null;

        engagement.End(
            winner
        );

        foreach (var division
                in engagement.Participants)
        {
            division.LeaveEngagement(
                engagement
            );

            // Pour l'instant, aucun participant ne
            // poursuit automatiquement son ancien ordre.
            division.ClearPlannedRoute();
        }
    }



}