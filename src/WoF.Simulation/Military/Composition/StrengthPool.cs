namespace WoF.Simulation.Military.Composition;

public sealed class StrengthPool
{
    public int Authorized { get; }

    public int Current { get; private set; }

    public int Missing =>
        Authorized - Current;

    public double AvailabilityRatio =>
        Authorized == 0
            ? 1.0
            : (double)Current / Authorized;

    public StrengthPool(
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

    public int ApplyLoss(
        int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount)
            );
        }

        int actualLoss =
            Math.Min(
                amount,
                Current
            );

        Current -=
            actualLoss;

        return actualLoss;
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

        int actualReinforcement =
            Math.Min(
                amount,
                Missing
            );

        Current +=
            actualReinforcement;

        return actualReinforcement;
    }
}