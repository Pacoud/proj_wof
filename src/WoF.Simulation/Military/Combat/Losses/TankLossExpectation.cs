namespace WoF.Simulation.Military.Combat.Losses;

public sealed record TankLossExpectation(
    double Damaged,
    double Destroyed)
{
    public static TankLossExpectation None =>
        new(
            Damaged: 0,
            Destroyed: 0
        );
}