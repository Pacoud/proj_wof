using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Combat;
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Retreat;

public sealed class RetreatSystem
{
    private readonly DiplomacySystem _diplomacy;

    public RetreatSystem(
        DiplomacySystem diplomacy)
    {
        _diplomacy = diplomacy;
    }
/*
    public bool TryPlanRetreat(
        Division division,
        Engagement engagement,
        IEnumerable<Division> allDivisions)
    {
        if (!division.IsBroken)
            return false;

        if (division.Country == null)
            return false;

        Province? retreatStart =
            division.CurrentProvince
            ?? division.Transit?.Origin;

        if (retreatStart == null)
            return false;

        var path =
            RetreatPathfinder.FindRetreatPath(
                retreatStart,
                division.Country,
                allDivisions,
                _diplomacy
            );

        if (path == null
            || path.Count == 0)
        {
            return false;
        }

        division.BeginRetreat(path);

        return true;
    } */

        public bool BeginRetreatAfterEngagement(
        Division division,
        Engagement engagement,
        IEnumerable<Division> allDivisions)
    {
        if (!division.IsBroken)
            return false;

        if (division.Country == null)
            return false;

        // Combat rencontré au milieu d'une liaison :
        // retour obligatoire vers la province d'origine.
        if (engagement.Type
                == EngagementType.MeetingEngagement
            && division.Transit != null)
        {
            return division.BeginReturnToOriginRetreat();
        }

        // Combat dans une province :
        // on peut chercher immédiatement l'arrière.
        return TryPlanSafetyRoute(
            division,
            allDivisions
        );
    }

        public bool ContinueRetreatToSafety(
        Division division,
        IEnumerable<Division> allDivisions)
    {
        if (division.RetreatPhase
            != RetreatPhase.ReturningToOrigin)
        {
            return false;
        }

        if (division.IsInTransit)
            return false;

        if (division.CurrentProvince == null)
            return false;

        return TryPlanSafetyRoute(
            division,
            allDivisions
        );
    }

    private bool TryPlanSafetyRoute(
    Division division,
    IEnumerable<Division> allDivisions)
    {
        if (division.CurrentProvince == null)
            return false;

        if (division.Country == null)
            return false;

        var path =
            RetreatPathfinder.FindRetreatPath(
                division.CurrentProvince,
                division.Country,
                allDivisions,
                _diplomacy
            );

        if (path == null
            || path.Count == 0)
        {
            return false;
        }

        division.BeginRetreat(path);

        return true;
    }
}