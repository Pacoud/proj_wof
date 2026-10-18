
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Delais de transmission en heures de simulation.
/// Valeurs configurables, non calibrees historiquement.
/// </summary>
public sealed class TacticalCommunicationDelays
{
    public long CompanyToBattalionHours { get; }
    public long BattalionToDivisionHours { get; }

    public TacticalCommunicationDelays(
        long companyToBattalionHours = 1,
        long battalionToDivisionHours = 2)
    {
        if (companyToBattalionHours < 1)
            throw new ArgumentOutOfRangeException(
                nameof(companyToBattalionHours));

        if (battalionToDivisionHours < 1)
            throw new ArgumentOutOfRangeException(
                nameof(battalionToDivisionHours));

        CompanyToBattalionHours = companyToBattalionHours;
        BattalionToDivisionHours = battalionToDivisionHours;
    }
}
