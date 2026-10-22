
namespace WoF.Simulation.Military.Composition;

/// <summary>
/// Inventaire physique des munitions d'une compagnie.
/// Usage synchrone, sans acces concurrent pour l'instant.
/// </summary>
public sealed class CompanyAmmunitionInventory
{
    private long _smallArms;
    private long _machineGun;
    private long _artillery;
    private long _tank;

    internal CompanyAmmunitionInventory(AmmunitionTotals initial)
    {
        _smallArms = initial.SmallArms;
        _machineGun = initial.MachineGun;
        _artillery = initial.Artillery;
        _tank = initial.Tank;
    }

    public AmmunitionTotals Capture()
    {
        return new AmmunitionTotals(
            _smallArms,
            _machineGun,
            _artillery,
            _tank);
    }

    public long GetAvailable(AmmunitionType type)
    {
        return type switch
        {
            AmmunitionType.SmallArms => _smallArms,
            AmmunitionType.MachineGun => _machineGun,
            AmmunitionType.Artillery => _artillery,
            AmmunitionType.Tank => _tank,

            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    public bool TryConsume(AmmunitionType type, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        long available = GetAvailable(type);

        if (amount > available)
            return false;

        SetAvailable(type, available - amount);
        return true;
    }

    public void Resupply(AmmunitionType type, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        long available = GetAvailable(type);
        long updated = checked(available + amount);

        SetAvailable(type, updated);
    }

    private void SetAvailable(AmmunitionType type, long value)
    {
        switch (type)
        {
            case AmmunitionType.SmallArms:
                _smallArms = value;
                break;

            case AmmunitionType.MachineGun:
                _machineGun = value;
                break;

            case AmmunitionType.Artillery:
                _artillery = value;
                break;

            case AmmunitionType.Tank:
                _tank = value;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }
}
