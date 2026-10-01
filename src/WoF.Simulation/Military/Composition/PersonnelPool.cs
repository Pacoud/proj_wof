using WoF.Simulation.Military.Combat.Losses;


namespace WoF.Simulation.Military.Composition;

public sealed class PersonnelPool
{
    public int Authorized { get; }

    public int Current { get; private set; }

    public int WoundedUnavailable { get; private set; }

    public int KilledInAction { get; private set; }

    public int TotalWoundedInAction { get; private set; }

    public int MissingOrCaptured { get; private set; }

    public double AvailabilityRatio =>
        Authorized == 0
            ? 0.0
            : (double)Current / Authorized;

    public int VacantPositions =>
        Math.Max(
            0,
            Authorized
            - Current
            - WoundedUnavailable
        );

    public PersonnelPool(
        int authorized,
        int? current = null)
    {
        if (authorized < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(authorized)
            );
        }

        int initialCurrent =
            current ?? authorized;

        if (initialCurrent < 0
            || initialCurrent > authorized)
        {
            throw new ArgumentOutOfRangeException(
                nameof(current)
            );
        }

        Authorized =
            authorized;

        Current =
            initialCurrent;
    }

    public void ApplyCasualties(
        PersonnelCasualtyReport casualties)
    {
        if (casualties.Total > Current)
        {
            throw new InvalidOperationException(
                "Casualties cannot exceed available manpower."
            );
        }

        Current -=
            casualties.Total;

        KilledInAction +=
            casualties.KilledInAction;

        WoundedUnavailable +=
            casualties.WoundedInAction;

        TotalWoundedInAction +=
            casualties.WoundedInAction;

        MissingOrCaptured +=
            casualties.MissingOrCaptured;
    }

    public int ReturnWoundedToDuty(
        int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount)
            );
        }

        int returned =
            Math.Min(
                amount,
                WoundedUnavailable
            );

        returned =
            Math.Min(
                returned,
                Authorized - Current
            );

        WoundedUnavailable -=
            returned;

        Current +=
            returned;

        return returned;
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
                VacantPositions
            );

        Current +=
            received;

        return received;
    }
}