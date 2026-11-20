
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Acces limite a un seul enregistrement de QG.
///
/// La politique peut lire son propre resultat,
/// mais ne recoit ni l'orchestrateur, ni la
/// phase globale contenant les autres bataillons.
/// </summary>
internal sealed class TacticalCommandSituationReader
{
    private readonly Func<
        long,
        TacticalCommandSituationReadResult?> _read;

    internal TacticalCommandSituationReader(
        Func<long, TacticalCommandSituationReadResult?> read)
    {
        _read = read
            ?? throw new ArgumentNullException(nameof(read));
    }

    internal TacticalCommandSituationReadResult?
        Read(long expectedHour)
    {
        if (expectedHour < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedHour));
        }

        return _read(expectedHour);
    }
}
