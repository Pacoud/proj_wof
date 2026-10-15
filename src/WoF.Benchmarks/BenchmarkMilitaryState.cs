
using WoF.Simulation.Core;

namespace WoF.Benchmarks;

public sealed record BenchmarkMilitaryState(
    long SimulatedHour,
    int ActiveBattles,
    int ParticipantsInActiveBattles,
    int CombatCapableParticipants,
    int BrokenDivisions,
    int ArchivedBattles,
    long AvailablePersonnel,
    long KilledInAction,
    long WoundedUnavailable,
    long TotalWoundedInAction,
    long MissingOrCaptured,
    long OperationalTanks,
    long DamagedTanks,
    long DestroyedTanks,
    double TotalOrganization,
    double TotalFuel)
{
    public static BenchmarkMilitaryState Capture(
        SimulationEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var battles =
            engine.Engagements.ActiveEngagements.ToArray();

        int participants = 0;
        int combatCapable = 0;

        foreach (var battle in battles)
        {
            foreach (var division in battle.Participants)
            {
                participants++;

                if (!division.IsBroken && division.IsEngaged)
                    combatCapable++;
            }
        }

        int broken = 0;

        long personnel = 0;
        long killed = 0;
        long woundedUnavailable = 0;
        long woundedTotal = 0;
        long missing = 0;

        long operationalTanks = 0;
        long damagedTanks = 0;
        long destroyedTanks = 0;

        double organization = 0;
        double fuel = 0;

        foreach (var division in engine.Divisions)
        {
            var resources =
                division.GetCurrentResourceTotals();

            if (division.IsBroken)
                broken++;

            personnel += resources.Personnel.Available;
            killed += resources.Personnel.KilledInAction;

            woundedUnavailable +=
                resources.Personnel.WoundedUnavailable;

            woundedTotal +=
                resources.Personnel.TotalWoundedInAction;

            missing +=
                resources.Personnel.MissingOrCaptured;

            operationalTanks += resources.Tanks.Operational;
            damagedTanks += resources.Tanks.Damaged;
            destroyedTanks += resources.Tanks.Destroyed;

            organization += division.Organization;
            fuel += division.Fuel;
        }

        return new BenchmarkMilitaryState(
            SimulatedHour: engine.Clock.CurrentHour,
            ActiveBattles: battles.Length,
            ParticipantsInActiveBattles: participants,
            CombatCapableParticipants: combatCapable,
            BrokenDivisions: broken,
            ArchivedBattles:
                engine.Engagements.History.Reports.Count,
            AvailablePersonnel: personnel,
            KilledInAction: killed,
            WoundedUnavailable: woundedUnavailable,
            TotalWoundedInAction: woundedTotal,
            MissingOrCaptured: missing,
            OperationalTanks: operationalTanks,
            DamagedTanks: damagedTanks,
            DestroyedTanks: destroyedTanks,
            TotalOrganization: organization,
            TotalFuel: fuel
        );
    }
}
