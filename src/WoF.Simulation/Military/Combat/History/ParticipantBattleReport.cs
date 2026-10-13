
namespace WoF.Simulation.Military.Combat.History;

public sealed record ParticipantBattleReport(
    string DivisionName,
    int? CountryId,
    string CountryName,
    EngagementRole Role,
    long JoinedAtHour,
    ParticipantBattleSnapshot Initial,
    ParticipantBattleSnapshot Final)
{
    public int Killed =>
        Final.KilledInAction - Initial.KilledInAction;

    public int Wounded =>
        Final.TotalWoundedInAction
        - Initial.TotalWoundedInAction;

    public int MissingOrCaptured =>
        Final.MissingOrCaptured
        - Initial.MissingOrCaptured;

    public int TotalCasualties =>
        Killed + Wounded + MissingOrCaptured;

    public int TanksDestroyed =>
        Final.DestroyedTanks - Initial.DestroyedTanks;

    public int OperationalTanksChange =>
        Final.OperationalTanks - Initial.OperationalTanks;

    // Net stock change, not the number of tanks damaged during this battle.
    // Repairs can reduce this value without undoing the damage suffered.
    public int DamagedTanksChange =>
        Final.DamagedTanks - Initial.DamagedTanks;

    public double OrganizationChange =>
        Final.Organization - Initial.Organization;
}
