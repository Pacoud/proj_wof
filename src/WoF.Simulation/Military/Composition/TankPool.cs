using WoF.Simulation.Military.Combat.Losses;

namespace WoF.Simulation.Military.Composition;

public sealed class TankPool
{
    public int Authorized { get; }

    public int Operational { get; private set; }

    public int Damaged { get; private set; }

    // Cumul historique.
    public int Destroyed { get; private set; }

    public int Current =>
        Operational + Damaged;

    public int Missing =>
        Math.Max(
            0,
            Authorized - Current
        );

    public double AvailabilityRatio =>
        Authorized == 0
            ? 1.0
            : (double)Operational
                / Authorized;

    private double _pendingDamaged;

    private double _pendingDestroyed;

    public TankPool(
        int authorized,
        int? operational = null)
    {
        if (authorized < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(authorized)
            );
        }

        int initialOperational =
            operational ?? authorized;

        if (initialOperational < 0
            || initialOperational > authorized)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operational)
            );
        }

        Authorized =
            authorized;

        Operational =
            initialOperational;
    }

    public TankLossResult ApplyExpectedLosses(
        TankLossExpectation losses)
    {
        if (losses.Damaged < 0
            || losses.Destroyed < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(losses)
            );
        }

        if (Operational <= 0)
        {
            _pendingDamaged = 0;
            _pendingDestroyed = 0;

            return TankLossResult.None;
        }

        // Les destructions sont appliquées en premier.
        _pendingDestroyed +=
            losses.Destroyed;

        int destroyed =
            Math.Min(
                (int)Math.Floor(
                    _pendingDestroyed
                ),
                Operational
            );

        _pendingDestroyed -=
            destroyed;

        Operational -=
            destroyed;

        Destroyed +=
            destroyed;


        // Puis les immobilisations / dégâts
        // parmi les chars encore opérationnels.
        _pendingDamaged +=
            losses.Damaged;

        int damaged =
            Math.Min(
                (int)Math.Floor(
                    _pendingDamaged
                ),
                Operational
            );

        _pendingDamaged -=
            damaged;

        Operational -=
            damaged;

        Damaged +=
            damaged;


        // Si plus aucun char n'est disponible,
        // on ne conserve pas des dégâts fantômes
        // qui toucheraient des renforts futurs.
        if (Operational == 0)
        {
            _pendingDamaged = 0;
            _pendingDestroyed = 0;
        }

        return new TankLossResult(
            Damaged: damaged,
            Destroyed: destroyed
        );
    }

    public int Repair(
        int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount)
            );
        }

        int repaired =
            Math.Min(
                amount,
                Damaged
            );

        Damaged -=
            repaired;

        Operational +=
            repaired;

        return repaired;
    }

    public int Reinforce(
        int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount)
            );
        }

        int received =
            Math.Min(
                amount,
                Missing
            );

        Operational +=
            received;

        return received;
    }
}