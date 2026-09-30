namespace WoF.Simulation.Military.Combat.Power;

public sealed record CombatPowerProfile(
    double SmallArms,
    double Artillery,
    double Armored)
{
    public double Total =>
        Math.Round(
            SmallArms
            + Artillery
            + Armored,
            2
        );
}