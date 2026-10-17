using System.Text;

namespace WoF.Simulation.Military.Combat.Tactical;

// Diagnostic formatter. The eventual Avalonia UI consumes the same snapshot.
public static class TacticalBattleReportFormatter
{
    public static string Format(TacticalBattleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sb = new StringBuilder();
        sb.AppendLine($"Bataille #{snapshot.EngagementId} " +
            (snapshot.IsActive ? "(active)" : "(terminee)"));

        foreach (var order in snapshot.Orders)
        {
            sb.AppendLine($"Ordre divisionnaire : {order.DivisionName} " +
                $"[{(order.Posture == TacticalPosture.Attack ? "attaque" : "defense")}] " +
                $"| priorite: {SectorName(order.PrioritySectorId)} " +
                $"| reserve: {order.ReserveBattalions} bataillon(s) " +
                $"a {SectorName(order.ReserveSectorId)}");
        }

        foreach (var connection in snapshot.Connections)
        {
            sb.AppendLine($"Liaison : {SectorName(connection.SectorAId)} " +
                $"<-> {SectorName(connection.SectorBId)} " +
                $"({connection.DistanceMeters:0} m)");
        }

        foreach (var sector in snapshot.Sectors.OrderBy(s => s.Id))
        {
            sb.AppendLine();
            sb.AppendLine($"Secteur {sector.Id} : {sector.Name} " +
                $"| {sector.Terrain} | altitude {sector.ElevationMeters} m " +
                $"| {sector.Features}");

            var sectorCompanies = snapshot.Companies
                .Where(c => c.SectorId == sector.Id)
                .OrderBy(c => c.ParticipantIndex)
                .ThenBy(c => c.BattalionId)
                .ThenBy(c => c.CompanyId)
                .ToArray();

            if (sectorCompanies.Length == 0)
            {
                sb.AppendLine("  Aucune compagnie deployee");
                continue;
            }

            foreach (var group in sectorCompanies.GroupBy(c =>
                (c.ParticipantIndex, c.BattalionId)))
            {
                var first = group.First();
                sb.AppendLine($"  {first.DivisionName} / {first.BattalionName}");

                foreach (var company in group)
                {
                    sb.AppendLine($"    {company.CompanyName} " +
                        $"[{RoleText(company.Role)}] " +
                        $"hommes={company.PersonnelAvailable} " +
                        $"chars={company.OperationalTanks} " +
                        $"| raison: {ReasonText(company.AssignmentReason)}");
                }
            }
        }

        return sb.ToString();

        string SectorName(int id) =>
            snapshot.Sectors.FirstOrDefault(s => s.Id == id)?.Name
            ?? $"secteur #{id}";
    }

    private static string RoleText(CompanyDeploymentRole role) => role switch
    {
        CompanyDeploymentRole.FrontLine => "premiere ligne",
        CompanyDeploymentRole.Support => "soutien",
        CompanyDeploymentRole.Reserve => "reserve",
        _ => "role inconnu"
    };

    private static string ReasonText(TacticalAssignmentReason? reason) =>
        reason switch
        {
            TacticalAssignmentReason.PriorityObjective =>
                "objectif divisionnaire prioritaire",
            TacticalAssignmentReason.SecondaryObjective =>
                "objectif divisionnaire secondaire",
            TacticalAssignmentReason.DivisionReserve =>
                "reserve divisionnaire demandee",
            null => "origine non documentee",
            _ => "raison inconnue"
        };
}
