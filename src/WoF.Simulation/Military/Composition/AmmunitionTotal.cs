
namespace WoF.Simulation.Military.Composition;

/// <summary>
/// Releve immuable des munitions disponibles.
/// Ne represente pas un second stock physique.
/// </summary>
public readonly record struct AmmunitionTotals
{
    public long SmallArms { get; }
    public long MachineGun { get; }
    public long Artillery { get; }
    public long Tank { get; }

    public AmmunitionTotals(
        long smallArms = 0,
        long machineGun = 0,
        long artillery = 0,
        long tank = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(smallArms);
        ArgumentOutOfRangeException.ThrowIfNegative(machineGun);
        ArgumentOutOfRangeException.ThrowIfNegative(artillery);
        ArgumentOutOfRangeException.ThrowIfNegative(tank);

        SmallArms = smallArms;
        MachineGun = machineGun;
        Artillery = artillery;
        Tank = tank;
    }

    public static AmmunitionTotals operator +(
        AmmunitionTotals first,
        AmmunitionTotals second)
    {
        return new AmmunitionTotals(
            smallArms: checked(first.SmallArms + second.SmallArms),
            machineGun: checked(first.MachineGun + second.MachineGun),
            artillery: checked(first.Artillery + second.Artillery),
            tank: checked(first.Tank + second.Tank));
    }
}
