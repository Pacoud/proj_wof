
using WoF.Simulation.Military;

namespace WoF.Simulation.Military.Combat.History;

public sealed record ParticipantBattleSnapshot(
    int Manpower,
    int KilledInAction,
    int TotalWoundedInAction,
    int MissingOrCaptured,
    int OperationalTanks,
    int DamagedTanks,
    int DestroyedTanks,
    double Organization,
    double Fuel)
{
    public static ParticipantBattleSnapshot Capture(
        Division division)
    {
        ArgumentNullException.ThrowIfNull(division);

        var resources = division.GetCurrentResourceTotals();

        var personnel = resources.Personnel;
        var tanks = resources.Tanks;

        return new ParticipantBattleSnapshot(
            Manpower: personnel.Available,
            KilledInAction: personnel.KilledInAction,
            TotalWoundedInAction: personnel.TotalWoundedInAction,
            MissingOrCaptured: personnel.MissingOrCaptured,
            OperationalTanks: tanks.Operational,
            DamagedTanks: tanks.Damaged,
            DestroyedTanks: tanks.Destroyed,
            Organization: division.Organization,
            Fuel: division.Fuel
        );
    }
}
