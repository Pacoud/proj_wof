namespace WoF.Simulation.Military.Combat.Losses;

public sealed record PersonnelCasualtyReport(
    int KilledInAction,
    int WoundedInAction,
    int MissingOrCaptured)
{
    public int Total =>
        KilledInAction
        + WoundedInAction
        + MissingOrCaptured;

    public static PersonnelCasualtyReport None =>
        new(
            KilledInAction: 0,
            WoundedInAction: 0,
            MissingOrCaptured: 0
        );
}