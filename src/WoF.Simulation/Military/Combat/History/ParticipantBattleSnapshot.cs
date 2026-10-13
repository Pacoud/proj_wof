
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
        var personnel = division.Composition.Manpower;
        var tanks = division.Composition.Tanks;

        return new ParticipantBattleSnapshot(
            Manpower: personnel.Current,
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
