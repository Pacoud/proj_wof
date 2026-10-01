namespace WoF.Simulation.Military.Combat.Fuel;

public sealed record CombatFuelDemandProfile(
    double Trucks,
    double Artillery,
    double Tanks)
{
    public double Total =>
        Math.Round(
            Trucks
            + Artillery
            + Tanks,
            4
        );
}