using WoF.Simulation.World;
using WoF.Simulation.World.Infrastructure;

namespace WoF.Simulation.Military;

public static class MovementSystem
{
    private const int BaseMovementDurationHours = 6;

    public static int CalculateMovementDuration(
        Province origin,
        Province destination,
        IEnumerable<InfrastructureLink> links)
    {
        if (!origin.IsNeighbourOf(destination))
        {
            throw new ArgumentException(
                "Movement is only possible between neighbouring provinces."
            );
        }

        var bestRoad = links
            .Where(link =>
                link.Type == InfrastructureType.Road
                &&
                link.Connects(origin, destination))
            .OrderByDescending(link => link.Level)
            .FirstOrDefault();

        // Aucune route :
        // déplacement possible mais lent.
        if (bestRoad == null)
            return BaseMovementDurationHours;

        return bestRoad.Level switch
        {
            1 => 5,
            2 => 4,
            3 => 3,

            _ => BaseMovementDurationHours
        };
    }
}