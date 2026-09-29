using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Retreat;

public static class RetreatPathfinder
{
    public static IReadOnlyList<Province>? FindRetreatPath(
        Province start,
        Country country,
        IEnumerable<Division> divisions,
        DiplomacySystem diplomacy)
    {
        var allDivisions =
            divisions.ToList();

        var visited =
            new HashSet<Province>
            {
                start
            };

        var queue =
            new Queue<(
                Province Province,
                List<Province> Path
            )>();

        queue.Enqueue(
            (
                start,
                new List<Province>()
            )
        );

        List<Province>? fallbackPath =
            null;

        while (queue.Count > 0)
        {
            var current =
                queue.Dequeue();

            foreach (var neighbour
                     in current.Province.Neighbours)
            {
                if (!visited.Add(neighbour))
                    continue;

                if (!IsFriendlyControlled(
                        neighbour,
                        country))
                {
                    continue;
                }

                if (ContainsHostileDivision(
                        neighbour,
                        country,
                        allDivisions,
                        diplomacy))
                {
                    continue;
                }

                var newPath =
                    new List<Province>(
                        current.Path
                    )
                    {
                        neighbour
                    };

                // Premier territoire ami valide :
                // solution de secours.
                fallbackPath ??=
                    newPath;

                if (IsPreferredSafeProvince(
                        neighbour,
                        country,
                        allDivisions,
                        diplomacy))
                {
                    return newPath;
                }

                queue.Enqueue(
                    (
                        neighbour,
                        newPath
                    )
                );
            }
        }

        return fallbackPath;
    }

    private static bool IsFriendlyControlled(
        Province province,
        Country country)
    {
        return province.Controller != null
            && province.Controller.Id
                == country.Id;
    }

    private static bool ContainsHostileDivision(
        Province province,
        Country country,
        IEnumerable<Division> divisions,
        DiplomacySystem diplomacy)
    {
        return divisions.Any(
            division =>
                !division.IsBroken
                &&
                division.CurrentProvince != null
                &&
                ReferenceEquals(
                    division.CurrentProvince,
                    province
                )
                &&
                division.Country != null
                &&
                diplomacy.AreAtWar(
                    country,
                    division.Country
                )
        );
    }

    private static bool IsPreferredSafeProvince(
        Province province,
        Country country,
        IEnumerable<Division> divisions,
        DiplomacySystem diplomacy)
    {
        foreach (var neighbour
                 in province.Neighbours)
        {
            if (ContainsHostileDivision(
                    neighbour,
                    country,
                    divisions,
                    diplomacy))
            {
                return false;
            }
        }

        return true;
    }
}