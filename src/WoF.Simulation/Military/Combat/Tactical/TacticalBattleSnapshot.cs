using WoF.Simulation.Military.Organization;
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat.Tactical;

// Read model: no live Division, Battalion, Company, or BattleSector references.
public sealed record TacticalSectorSnapshot(
    int Id,
    string Name,
    TerrainType Terrain,
    int ElevationMeters,
    SectorFeature Features);

public sealed record TacticalConnectionSnapshot(
    int SectorAId,
    int SectorBId,
    double DistanceMeters);

public sealed record TacticalOrderSnapshot(
    int ParticipantIndex,
    string DivisionName,
    TacticalPosture Posture,
    int PrioritySectorId,
    IReadOnlyList<int> SecondarySectorIds,
    int ReserveSectorId,
    int ReserveBattalions);

public sealed record TacticalCompanySnapshot(
    int ParticipantIndex,
    string DivisionName,
    string? CountryName,
    int BattalionId,
    string BattalionName,
    int CompanyId,
    string CompanyName,
    CompanyDeploymentRole Role,
    int SectorId,
    TacticalAssignmentReason? AssignmentReason,
    int PersonnelAvailable,
    int OperationalTanks,
    int DamagedTanks);

public sealed record TacticalBattleSnapshot(
    int EngagementId,
    bool IsActive,
    IReadOnlyList<TacticalSectorSnapshot> Sectors,
    IReadOnlyList<TacticalConnectionSnapshot> Connections,
    IReadOnlyList<TacticalOrderSnapshot> Orders,
    IReadOnlyList<TacticalCompanySnapshot> Companies);

public static class TacticalBattleSnapshotBuilder
{
    public static TacticalBattleSnapshot Capture(TacticalBattle battle)
    {
        ArgumentNullException.ThrowIfNull(battle);

        // ParticipantIndex is a battle-local identity. Battalion and company IDs
        // are unique only inside their parent, and divisions have no numeric ID.
        var participantIndexes = new Dictionary<Division, int>(
            ReferenceEqualityComparer.Instance);

        for (int i = 0; i < battle.Engagement.Participants.Count; i++)
        {
            participantIndexes.Add(battle.Engagement.Participants[i], i);
        }

        var reasons = new Dictionary<Company, TacticalAssignmentReason>(
            ReferenceEqualityComparer.Instance);

        var orders = new List<TacticalOrderSnapshot>();

        foreach (var plan in battle.AppliedPlans)
        {
            int participantIndex = participantIndexes[plan.Division];
            var order = plan.Order;

            orders.Add(new TacticalOrderSnapshot(
                participantIndex,
                plan.Division.Name,
                order.Posture,
                order.PrioritySector.Id,
                Array.AsReadOnly(order.SecondarySectors
                    .Select(sector => sector.Id).ToArray()),
                order.ReserveSector.Id,
                order.ReserveBattalions));

            foreach (var assignment in plan.Assignments)
            {
                if (!reasons.TryAdd(assignment.Company, assignment.Reason))
                    throw new InvalidOperationException(
                        "A company has multiple recorded command decisions.");
            }
        }

        var companies = new List<TacticalCompanySnapshot>();

        foreach (var deployment in battle.Deployments)
        {
            var company = deployment.Company;
            var battalion = company.ParentBattalion
                ?? throw new InvalidOperationException("Unattached company.");
            var division = battalion.ParentDivision
                ?? throw new InvalidOperationException("Unattached battalion.");
            var composition = company.Composition
                ?? throw new InvalidOperationException("Missing composition.");

            companies.Add(new TacticalCompanySnapshot(
                ParticipantIndex: participantIndexes[division],
                DivisionName: division.Name,
                CountryName: division.Country?.Name,
                BattalionId: battalion.Id,
                BattalionName: battalion.Name,
                CompanyId: company.Id,
                CompanyName: company.Name,
                Role: deployment.Role,
                SectorId: deployment.Sector.Id,
                AssignmentReason: reasons.TryGetValue(company, out var reason)
                    ? reason : null,
                PersonnelAvailable: composition.Manpower.Current,
                OperationalTanks: composition.Tanks.Operational,
                DamagedTanks: composition.Tanks.Damaged));
        }

        var sectors = battle.Battlefield.Sectors
            .Select(s => new TacticalSectorSnapshot(
                s.Id, s.Name, s.DominantTerrain,
                s.ElevationMeters, s.Features))
            .ToList().AsReadOnly();

        var connections = battle.Battlefield.Connections
            .Select(c => new TacticalConnectionSnapshot(
                c.A.Id, c.B.Id, c.DistanceMeters))
            .ToList().AsReadOnly();

        return new TacticalBattleSnapshot(
            battle.Engagement.Id,
            battle.Engagement.IsActive,
            sectors,
            connections,
            orders.OrderBy(o => o.ParticipantIndex).ToList().AsReadOnly(),
            companies.AsReadOnly());
    }
}
