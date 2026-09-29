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
    }
}