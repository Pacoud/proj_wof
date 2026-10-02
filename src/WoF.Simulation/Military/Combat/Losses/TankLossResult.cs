namespace WoF.Simulation.Military.Combat.Losses;

public sealed record TankLossResult(
    int Damaged,
    int Destroyed)
{
    public int Total =>
        Damaged + Destroyed;

    public static TankLossResult None =>
        new(
            Damaged: 0,
            Destroyed: 0
        );
}