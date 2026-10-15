using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;
using WoF.Simulation.Military.Combat.Power;
using WoF.Simulation.Military.Combat.Fuel;
using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.History;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat;

public sealed class EngagementSystem
{
    private readonly DiplomacySystem _diplomacy;

    
    private readonly record struct CountryCombatSummary(
        int FriendlyTargets,
        int HostileDivisionCount,
        double HostilePressure,
        double HostileSmallArms,
        double HostileArtillery,
        double HostileArmored
    );


    private readonly List<Engagement> _engagements = new();

    private readonly List<Engagement> _endedThisTick = new();

    public IReadOnlyList<Engagement> EndedThisTick =>
            _endedThisTick;

    private int _nextEngagementId = 1;

    public BattleHistory History { get; } = new();


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
                    arrivalSet,
                    currentHour
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
                    currentHour,
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
    IReadOnlySet<Division> arrivals,
    long currentHour)
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
                    currentHour,
                    role
                );
            }
        }
    }

    private void AddReinforcementsToTransitEngagements(
    IEnumerable<Division> candidates,
    long currentHour)
    {
        var meetings = ActiveEngagements
            .Where(engagement =>
                engagement.Type == EngagementType.MeetingEngagement)
            .OrderBy(engagement => engagement.Id)
            .ToList();

        foreach (Division candidate in candidates)
        {
            if (candidate.Transit == null
                || candidate.IsEngaged
                || candidate.IsBroken)
            {
                continue;
            }

            Engagement? firstEncounter = null;
            double firstContactTime = double.PositiveInfinity;

            foreach (Engagement engagement in meetings)
            {
                bool facesEnemy = engagement.Participants.Any(
                    participant =>
                        !participant.IsBroken
                        && AreHostile(candidate, participant)
                );

                if (!facesEnemy)
                    continue;

                if (!TryGetTransitContactTime(
                        candidate.Transit,
                        engagement,
                        out double contactTime))
                {
                    continue;
                }

                // Si plusieurs combats sont sur la liaison,
                // rejoindre le premier rencontré pendant le tick.
                if (contactTime < firstContactTime)
                {
                    firstEncounter = engagement;
                    firstContactTime = contactTime;
                }
            }

            if (firstEncounter == null)
                continue;

            SynchronizeDivisionToMeetingPoint(
                candidate,
                firstEncounter.ConnectionA!,
                firstEncounter.ConnectionProgress!.Value
            );

            firstEncounter.AddParticipant(candidate, currentHour);
        }
    }

    public void DetectTransitEngagements(
    IEnumerable<Division> divisions,
    long currentHour)
    {
        var candidates = divisions
            .Where(division =>
                division.Transit != null
                && !division.IsEngaged
                && !division.IsBroken)
            .ToList();

        // Priorité aux combats qui existaient déjà.
        AddReinforcementsToTransitEngagements(candidates, currentHour);

        // Créer les nouvelles rencontres.
        DetectNewTransitEngagements(candidates, currentHour);

        // Une troisième division peut atteindre un combat
        // créé pendant ce même tick.
        AddReinforcementsToTransitEngagements(candidates, currentHour);
    }

    public void DetectNewTransitEngagements(
    IReadOnlyList<Division> moving,
    long currentHour)
    {

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
                    first,
                    currentHour
                );

                engagement.AddParticipant(
                    second,
                    currentHour
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

    public void ProcessEngagements(long currentHour)
    {
        _endedThisTick.Clear();
        foreach (var engagement in
                ActiveEngagements.ToList())
        {
            ProcessEngagementTick(
                engagement,
                currentHour
            );
        }
    }


    private CombatPowerProfile CalculateDivisionEffectiveCombatPower(
    Division division,
    Engagement engagement)
    {
        FormationResourceTotals resources =
            division.GetCurrentResourceTotals();

        CombatPowerProfile physicalPower = 
        CompositionCombatPowerCalculator.Calculate(
            resources
        );

        CombatPowerProfile terrainAdjustedPower =
            CombatTerrainModifier.Apply(
                physicalPower,
                engagement.BattleTerrain
        );

        CombatFuelDemandProfile fuelDemand = 
        CompositionCombatFuelCalculator.Calculate(
            resources
        );

        double requestedFuel =
            fuelDemand.Total;

        double consumedFuel =
            division.ConsumeFuel(
                requestedFuel
            );

        double fuelSatisfaction =
            requestedFuel <= 0
            ? 1.0
            : Math.Clamp(
                consumedFuel
                / requestedFuel,
                0.0,
                1.0
            );

        CombatPowerProfile fuelAdjustedPower = 
            CombatFuelModifier.Apply(
                terrainAdjustedPower,
                fuelSatisfaction
            );


     
            return fuelAdjustedPower;
    }

    private void ProcessEngagementTick(
    Engagement engagement,
    long currentHour)
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
                participants,
                currentHour
            );

            return;
        }

        // =========================================================
        // PHASE 1
        // Calcul de la puissance effective de chaque division.
        //
        // Rien n'est encore appliqué aux participants.
        // =========================================================

        var combatPowers =
            new Dictionary<Division, CombatPowerProfile>();

        // Première phase :
        // calcul simultané de la puissance disponible.
        foreach (var division in participants)
        {
            if (division.IsBroken)
            {
                combatPowers[division] = new CombatPowerProfile(
                    SmallArms : 0,
                    Artillery : 0,
                    Armored : 0
                );

                continue;
            }

            combatPowers[division] =
                CalculateDivisionEffectiveCombatPower(
                    division,
                    engagement
                );
        }

                // Toutes les puissances ont été calculées.
        // Aucun stock humain ou matériel n'a encore
        // subi les pertes de ce tick.
        var countrySummaries =
            BuildCountryCombatSummaries(
                participants,
                combatPowers
            );
  

        // =========================================================
        // PHASE 2
        // Calcul de toutes les conséquences du combat.
        //
        // Toujours aucune modification réelle des divisions.
        // =========================================================

        var organizationLosses =
            new Dictionary<Division, double>();

        var personnelLosses = 
            new Dictionary<Division,PersonnelCasualtyReport>();

        var tankLossExpectations =
            new Dictionary<  Division, TankLossExpectation>();

        var tankCrewPositions =
            new Dictionary< Division,int>();

        // Deuxième phase :
        // calcul des pertes d'organisation.
        foreach (var division in participants)
        {
            tankLossExpectations[division] = TankLossExpectation.None;
            tankCrewPositions[division] = 0;

            if (division.IsBroken
                || division.Country == null)
            {
                organizationLosses[division] = 0;

                personnelLosses[division] = PersonnelCasualtyReport.None;

                continue;
            }

    
    // Toutes les divisions actives d'un même pays
    // partagent la même pression ennemie de départ.
    CountryCombatSummary summary =
        countrySummaries[division.Country.Id];

    // Aucun pays hostile actif :
    if (summary.HostileDivisionCount == 0)
    {
        organizationLosses[division] = 0;

        personnelLosses[division] =
            PersonnelCasualtyReport.None;

        continue;
    }

    int friendlyTargets = summary.FriendlyTargets;

    if (friendlyTargets <= 0)
    {
        organizationLosses[division] = 0;

        personnelLosses[division] =
            PersonnelCasualtyReport.None;

        continue;
    }

    // Même formule qu'avant, mais pression
    // ennemie calculée une seule fois par pays.
    organizationLosses[division] = Math.Round(
        summary.HostilePressure / friendlyTargets,
        2
    );

    // Conserver séparément les composantes :
    // elles interviennent différemment dans
    // les pertes humaines et matérielles.
    var hostilePowerPerTarget =
        new CombatPowerProfile(
            SmallArms:
                summary.HostileSmallArms / friendlyTargets,

            Artillery:
                summary.HostileArtillery / friendlyTargets,

            Armored:
                summary.HostileArmored / friendlyTargets
        );


        FormationResourceTotals resources = division.GetCurrentResourceTotals();


        int crewedTanks =
            Math.Min(
                resources
                    .Tanks
                    .Operational,

                resources.Personnel.Available
                / TankCrewCasualtyCalculator
                    .CrewSize
            );

        int crewPositions =
            crewedTanks
            * TankCrewCasualtyCalculator
                .CrewSize;

        tankCrewPositions[division] = crewPositions;

        int generalExposedManpower =
            Math.Max(
                0,
                resources
                    .Personnel
                    .Available
                - crewPositions
            );


        // -----------------------------------------------------
        // Calcul des pertes humaines.
        //
        // IMPORTANT :
        // elles ne sont toujours PAS appliquées ici.
        // -----------------------------------------------------

        personnelLosses[division] = PersonnelCasualtyCalculator.Calculate(
                hostilePowerPerTarget,generalExposedManpower
            );

        tankLossExpectations[division] = 
            TankLossCalculator.Calculate(
                hostilePowerPerTarget,
                crewedTanks
            );

        }



    // =========================================================
    // PHASE 3
    // Application simultanée des résultats.
    //
    // Tout a été calculé à partir de l'état du début du tick.
    // =========================================================

        foreach (var pair in organizationLosses)
        {
            pair.Key.LoseOrganization(
                pair.Value
            );
        }

        var tankLossResults = new Dictionary<Division, TankLossResult>();

        foreach (var pair in tankLossExpectations)
        {
            tankLossResults[pair.Key] = pair.Key.ApplyExpectedTankLosses(
                pair.Value
            );
        }

        foreach (var division in participants)
        {
            PersonnelCasualtyReport generalLosses = 
                personnelLosses[
                    division
                ];

            TankLossResult tankLosses = 
                tankLossResults[
                    division
                ];

            PersonnelCasualtyReport crewLosses =
            TankCrewCasualtyCalculator
                .Calculate(
                    tankLosses,
                    tankCrewPositions[
                        division
                    ]
                );

            var combinedLosses =
            new PersonnelCasualtyReport(
                KilledInAction:
                    generalLosses.KilledInAction
                    + crewLosses.KilledInAction,

                WoundedInAction:
                    generalLosses.WoundedInAction
                    + crewLosses.WoundedInAction,

                MissingOrCaptured:
                    generalLosses.MissingOrCaptured
                    + crewLosses.MissingOrCaptured
            );

            division.ApplyPersonnelCasualties(
                combinedLosses
                );
        }

    // =========================================================
    // PHASE 4
    // Vérification de la fin éventuelle de l'engagement.
    // =========================================================

        ResolveEngagementIfPossible(
            engagement,
            participants,
            currentHour
        );
    }




    private void ResolveEngagementIfPossible(
    Engagement engagement,
    IReadOnlyCollection<Division> participants,
    long currentHour)
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

        if (!engagement.End(winner, currentHour))
            return;

        History.Add(
            BuildBattleReport(engagement)
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



    private static bool IsOnSameConnection(
    TransitState transit,
    Province a,
    Province b)
    {
        return
            (ReferenceEquals(transit.Origin, a)
            && ReferenceEquals(transit.Destination, b))
            ||
            (ReferenceEquals(transit.Origin, b)
            && ReferenceEquals(transit.Destination, a));
    }

    private static bool TryGetTransitContactTime(
        TransitState transit,
        Engagement engagement,
        out double contactTime)
    {
        const double tolerance = 0.000001;

        contactTime = 0;

        if (engagement.ConnectionA == null
            || engagement.ConnectionB == null
            || engagement.ConnectionProgress == null)
        {
            return false;
        }

        if (!IsOnSameConnection(
                transit,
                engagement.ConnectionA,
                engagement.ConnectionB))
        {
            return false;
        }

        double previous = GetProgressInReference(
            transit,
            engagement.ConnectionA,
            previous: true
        );

        double current = GetProgressInReference(
            transit,
            engagement.ConnectionA,
            previous: false
        );

        double battlePosition =
            engagement.ConnectionProgress.Value;

        double movement = current - previous;

        // Une division déjà exactement au contact
        // peut rejoindre le combat même sans déplacement.
        if (Math.Abs(movement) <= tolerance)
        {
            return Math.Abs(previous - battlePosition)
                <= tolerance;
        }

        double fraction =
            (battlePosition - previous) / movement;

        if (fraction < -tolerance
            || fraction > 1 + tolerance)
        {
            return false;
        }

        contactTime = Math.Clamp(fraction, 0, 1);

        return true;
    }

    private static BattleReport BuildBattleReport(
        Engagement engagement)
    {
        var participantReports =
            engagement.Participants
                .Select(division =>
                    new ParticipantBattleReport(
                        DivisionName: division.Name,
                        CountryId: division.Country?.Id,
                        CountryName:
                            division.Country?.Name ?? "Unknown",
                        Role: engagement.GetRole(division),
                        JoinedAtHour:
                            engagement.GetJoinHour(division),
                        Initial:
                            engagement.GetInitialSnapshot(division),
                        Final:
                            ParticipantBattleSnapshot.Capture(division)
                    )
                )
                .ToArray();

        string location =
            engagement.Type == EngagementType.ProvinceBattle
                ? engagement.Province?.Name ?? "Unknown"
                : $"{engagement.ConnectionA?.Name}" +
                $" ↔ {engagement.ConnectionB?.Name}";

        return new BattleReport(
            EngagementId: engagement.Id,
            Type: engagement.Type,
            Location: location,
            Terrain: engagement.BattleTerrain,
            StartHour: engagement.StartHour,
            EndHour: engagement.EndHour
                ?? throw new InvalidOperationException("Cannot report an active engagement."),
            WinnerCountryId: engagement.WinnerCountry?.Id,
            WinnerCountryName: engagement.WinnerCountry?.Name,
            Participants: participantReports
        );
    }


        
    private Dictionary<int, CountryCombatSummary> BuildCountryCombatSummaries(
            IReadOnlyList<Division> participants,
            IReadOnlyDictionary<Division, CombatPowerProfile>
                combatPowers)
    {
        // Une division représentative par pays
        // et le nombre de divisions non rompues.
        var representatives = new Dictionary<int, Division>();
        var friendlyCounts = new Dictionary<int, int>();

        foreach (var division in participants)
        {
            if (division.IsBroken || division.Country == null)
                continue;

            int countryId = division.Country.Id;

            if (friendlyCounts.TryGetValue(
                countryId, out int count))
            {
                friendlyCounts[countryId] = count + 1;
            }
            else
            {
                friendlyCounts.Add(countryId, 1);
                representatives.Add(countryId, division);
            }
        }

        var summaries =
            new Dictionary<int, CountryCombatSummary>(
                friendlyCounts.Count);

        // Ce calcul est effectué une fois par pays,
        // et non une fois par division.
        foreach (var pair in representatives)
        {
            int countryId = pair.Key;
            Division representative = pair.Value;

            // Conserve l'ordre original des participants
            // et les règles diplomatiques existantes.
            var hostileDivisions = participants
                .Where(other =>
                    !other.IsBroken &&
                    AreHostile(representative, other))
                .ToList();

            // Important : conserver les mêmes opérations
            // Sum que dans le code d'origine, notamment
            // pour les effets des arrondis.
            var summary = new CountryCombatSummary(
                FriendlyTargets: friendlyCounts[countryId],

                HostileDivisionCount: hostileDivisions.Count,

                HostilePressure: hostileDivisions.Sum(
                    other => combatPowers[other].Total),

                HostileSmallArms: hostileDivisions.Sum(
                    other => combatPowers[other].SmallArms),

                HostileArtillery: hostileDivisions.Sum(
                    other => combatPowers[other].Artillery),

                HostileArmored: hostileDivisions.Sum(
                    other => combatPowers[other].Armored)
            );

            summaries.Add(countryId, summary);
        }

        return summaries;
    }




}

