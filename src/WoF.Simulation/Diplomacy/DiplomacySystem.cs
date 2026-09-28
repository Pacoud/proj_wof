using WoF.Simulation.World;

namespace WoF.Simulation.Diplomacy;

public sealed class DiplomacySystem
{
    private readonly HashSet<(int, int)> _wars = new();

    public bool DeclareWar(
        Country first,
        Country second)
    {
        if (first.Id == second.Id)
            return false;

        return _wars.Add(
            CreatePair(first, second)
        );
    }

    public bool MakePeace(
        Country first,
        Country second)
    {
        return _wars.Remove(
            CreatePair(first, second)
        );
    }

    public bool AreAtWar(
        Country first,
        Country second)
    {
        if (first.Id == second.Id)
            return false;

        return _wars.Contains(
            CreatePair(first, second)
        );
    }

    private static (int, int) CreatePair(
        Country first,
        Country second)
    {
        return first.Id < second.Id
            ? (first.Id, second.Id)
            : (second.Id, first.Id);
    }
}