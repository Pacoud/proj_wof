namespace WoF.Simulation.Military;

public sealed record MovementPlan(
    int DurationHours,
    double FuelPerHour
)
{
    public double EstimatedFuelCost  =>
        Math.Round(
            DurationHours * FuelPerHour,
            2
        );
}