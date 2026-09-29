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


    public void ProcessCombatFuel()
    {
        foreach (var engagement
                in ActiveEngagements)
        {
            foreach (var division
                    in engagement.Participants)
            {
                double requestedFuel =
                    GetCombatFuelPerHour(
                        division.Type
                    );

                division.ConsumeFuel(
                    requestedFuel
                );
            }
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



}