using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat;

public sealed class EngagementSystem
{
    private readonly DiplomacySystem _diplomacy;

    private readonly List<Engagement> _engagements = new();

    private readonly List<Engagement> _endedThisTick = new();

    public IReadOnlyList<Engagement> EndedThisTick =>
            _endedThisTick;

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
    long currentHour,
    IReadOnlyCollection<Division>? arrivals = null)
    {

        var arrivalSet =
        arrivals != null
        ? arrivals.ToHashSet()
        : new HashSet<Division>();

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
                        engagement.Type
                            == EngagementType.ProvinceBattle
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
                    divisionsHere,
                    arrivalSet
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

            var roles = DetermineProvinceBattleRoles(
                involved,
                arrivalSet
            );

            var engagement =
                new Engagement(
                    _nextEngagementId++,
                    currentHour,
                    province
                );

            foreach (var division in involved)
            {
                engagement.AddParticipant(
                    division,
                    roles[division]
                );
            }

            _engagements.Add(
                engagement
            );
        }
    }

    private EngagementRole DetermineReinforcementRole(
    Engagement engagement,
    Division candidate,
    IReadOnlySet<Division> arrivals)
    {
        if (candidate.Country == null)
            return EngagementRole.None;

        // Même pays qu'un attaquant existant.
        if (engagement.Attackers.Any(
                attacker =>
                    attacker.Country != null
                    &&
                    attacker.Country.Id
                        == candidate.Country.Id))
        {
            return EngagementRole.Attacker;
        }

        // Même pays qu'un défenseur existant.
        if (engagement.Defenders.Any(
                defender =>
                    defender.Country != null
                    &&
                    defender.Country.Id
                        == candidate.Country.Id))
        {
            return EngagementRole.Defender;
        }

        // Fallback simple pour les cas encore non classifiés.

        if (arrivals.Contains(candidate)
            &&
            engagement.Defenders.Any(
                defender =>
                    AreHostile(
                        candidate,
                        defender
                    )))
        {
            return EngagementRole.Attacker;
        }

        if (!arrivals.Contains(candidate)
            &&
            engagement.Attackers.Any(
                attacker =>
                    AreHostile(
                        candidate,
                        attacker
                    )))
        {
            return EngagementRole.Defender;
        }

        return EngagementRole.None;
    }


    private void AddReinforcementsToProvinceEngagement(
    Engagement engagement,
    IEnumerable<Division> divisions,
    IReadOnlySet<Division> arrivals)
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
                EngagementRole role = DetermineReinforcementRole(
                    engagement,
                    candidate,
                    arrivals
                );
                engagement.AddParticipant(
                    candidate,
                    role
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

                if (!TryCalculateMeetingPoint(
                        first,
                        second,
                        out Province connectionA,
                        out Province connectionB,
                        out  double contactProgress))
                {
                    continue;
                }

                 SynchronizeDivisionToMeetingPoint(
                    first,
                    connectionA,
                    contactProgress
                );

                SynchronizeDivisionToMeetingPoint(
                    second,
                    connectionA,
                    contactProgress
                );

                var engagement =
                    new Engagement(
                        _nextEngagementId++,
                        currentHour,
                        connectionA,
                        connectionB,
                        contactProgress
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

    public void ProcessEngagements()
    {
        _endedThisTick.Clear();
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
    Division division,
    Engagement engagement)
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

        double terrainModifier =
            CombatTerrainModifier.GetPressureModifier(
                division.Type,
                engagement.BattleTerrain
        );

        return Math.Round(
            GetBaseCombatPressure(
                division.Type
            )
            * fuelEffectiveness
            * terrainModifier,
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
                    division,
                    engagement
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
        
        _endedThisTick.Add(
            engagement
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


    private static bool TryCalculateMeetingPoint(
    Division first,
    Division second,
    out Province connectionA,
    out Province connectionB,
    out double progressFromA)
    {
        connectionA = null!;
        connectionB = null!;
        progressFromA = 0;

        if (first.Transit == null
            || second.Transit == null)
        {
            return false;
        }

        TransitState firstTransit =
            first.Transit;

        TransitState secondTransit =
            second.Transit;

        if (!AreTravellingOppositeDirections(
                first,
                second))
        {
            return false;
        }

        // Référentiel déterministe :
        // la province ayant l'ID le plus petit = A.
        if (firstTransit.Origin.Id
            < firstTransit.Destination.Id)
        {
            connectionA =
                firstTransit.Origin;

            connectionB =
                firstTransit.Destination;
        }
        else
        {
            connectionA =
                firstTransit.Destination;

            connectionB =
                firstTransit.Origin;
        }

        double firstPrevious =
            GetProgressInReference(
                firstTransit,
                connectionA,
                previous: true
            );

        double firstCurrent =
            GetProgressInReference(
                firstTransit,
                connectionA,
                previous: false
            );

        double secondPrevious =
            GetProgressInReference(
                secondTransit,
                connectionA,
                previous: true
            );

        double secondCurrent =
            GetProgressInReference(
                secondTransit,
                connectionA,
                previous: false
            );

        double firstDelta =
            firstCurrent - firstPrevious;

        double secondDelta =
            secondCurrent - secondPrevious;

        double relativeMovement =
            firstDelta - secondDelta;

        if (Math.Abs(relativeMovement)
            < 0.000001)
        {
            return false;
        }

        double tickFraction =
            (secondPrevious - firstPrevious)
            / relativeMovement;

        if (tickFraction < 0
            || tickFraction > 1)
        {
            return false;
        }

        double contactPosition =
            firstPrevious
            + firstDelta * tickFraction;

        if (contactPosition < 0
            || contactPosition > 1)
        {
            return false;
        }

        progressFromA =
            Math.Clamp(
                contactPosition,
                0,
                1
            );

        return true;
    }


    private static double GetProgressInReference(
    TransitState transit,
    Province referenceOrigin,
    bool previous)
    {
        double progress =
            previous
                ? transit.PreviousProgress
                : transit.Progress;

        if (ReferenceEquals(
                transit.Origin,
                referenceOrigin))
        {
            return progress;
        }

        return 1.0 - progress;
    }

    private static void SynchronizeDivisionToMeetingPoint(
    Division division,
    Province connectionA,
    double progressFromA)
    {
        if (division.Transit == null)
            return;

        TransitState transit =
            division.Transit;

        double progressInOwnDirection =
            ReferenceEquals(
                transit.Origin,
                connectionA
            )
                ? progressFromA
                : 1.0 - progressFromA;

        transit.SynchronizeProgress(
            progressInOwnDirection
        );
    }

    private Dictionary<Division, EngagementRole> DetermineProvinceBattleRoles(
        IReadOnlyCollection<Division> participants,
        IReadOnlySet<Division> arrivals)
    {
        var roles =
            participants.ToDictionary(
                division => division,
                _ => EngagementRole.None
            );

        var arriving =
            participants
                .Where(arrivals.Contains)
                .ToList();

        var stationary =
            participants
                .Where(
                    division =>
                        !arrivals.Contains(division)
                )
                .ToList();

        Country? attackerCountry = null;
        Country? defenderCountry = null;

        foreach (var arrivingDivision in arriving)
        {
            foreach (var stationaryDivision in stationary)
            {
                if (!AreHostile(
                        arrivingDivision,
                        stationaryDivision))
                {
                    continue;
                }

                attackerCountry =
                    arrivingDivision.Country;

                defenderCountry =
                    stationaryDivision.Country;

                break;
            }

            if (attackerCountry != null
                && defenderCountry != null)
            {
                break;
            }
        }

        // Aucun affrontement arrivée vs unité stationnaire :
        // on n'invente pas de rôles.
        if (attackerCountry == null
            || defenderCountry == null)
        {
            return roles;
        }

        foreach (var division in participants)
        {
            if (division.Country == null)
                continue;

            if (division.Country.Id
                == attackerCountry.Id)
            {
                roles[division] =
                    EngagementRole.Attacker;

                continue;
            }

            if (division.Country.Id
                == defenderCountry.Id)
            {
                roles[division] =
                    EngagementRole.Defender;
            }
        }

        return roles;
    }



}
