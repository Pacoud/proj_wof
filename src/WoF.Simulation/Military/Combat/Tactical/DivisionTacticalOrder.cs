
using WoF.Simulation.Military;

namespace WoF.Simulation.Military.Combat.Tactical;

public enum TacticalPosture
{
    Attack,
    Defend
}

/// <summary>
/// Intention tactique donnée à une division.
/// Aucune compagnie ni aucun bataillon n'est
/// directement commandé par le joueur.
/// </summary>
public sealed class DivisionTacticalOrder
{
    public Division Division { get; }

    public TacticalPosture Posture { get; }

    public BattleSector PrioritySector { get; }

    public IReadOnlyList<BattleSector> SecondarySectors { get; }

    public BattleSector ReserveSector { get; }

    public int ReserveBattalions { get; }

    public DivisionTacticalOrder(
        Division division,
        TacticalPosture posture,
        BattleSector prioritySector,
        IEnumerable<BattleSector> secondarySectors,
        BattleSector reserveSector,
        int reserveBattalions)
    {
        ArgumentNullException.ThrowIfNull(division);
        ArgumentNullException.ThrowIfNull(prioritySector);
        ArgumentNullException.ThrowIfNull(secondarySectors);
        ArgumentNullException.ThrowIfNull(reserveSector);

        if (!Enum.IsDefined(posture))
            throw new ArgumentOutOfRangeException(
                nameof(posture));

        if (reserveBattalions < 0)
            throw new ArgumentOutOfRangeException(
                nameof(reserveBattalions));

        var secondary = secondarySectors.ToArray();

        if (secondary.Any(s => s is null))
            throw new ArgumentException(
                "Secondary sectors cannot contain null.",
                nameof(secondarySectors));

        Division = division;
        Posture = posture;
        PrioritySector = prioritySector;
        SecondarySectors = Array.AsReadOnly(secondary);
        ReserveSector = reserveSector;
        ReserveBattalions = reserveBattalions;
    }
}
