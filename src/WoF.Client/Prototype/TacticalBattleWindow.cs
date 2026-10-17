
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WoF.Simulation.Military.Combat.Tactical;

namespace WoF.Client.Prototype;

public sealed class TacticalBattleWindow : Window
{
    private readonly TacticalBattleSnapshot _snapshot;
    private readonly StackPanel _details;
    private readonly ListBox _sectorList;

    public TacticalBattleWindow(
        TacticalBattleSnapshot snapshot)
    {
        _snapshot = snapshot
            ?? throw new ArgumentNullException(nameof(snapshot));

        Title = "WoF - Inspection tactique C2.1d";
        Width = 1040;
        Height = 740;
        MinWidth = 800;
        MinHeight = 550;

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("270,*"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(16)
        };

        var header = new StackPanel
        {
            Spacing = 5,
            Margin = new Thickness(0, 0, 0, 16)
        };

        header.Children.Add(
            Label($"Bataille #{snapshot.EngagementId}", 23, true));

        header.Children.Add(Label(
            "Démonstration tactique indépendante — " +
            "aucun calcul de pertes tactiques actif.",
            13));

        header.Children.Add(Label(
            $"{snapshot.Sectors.Count} secteurs · " +
            $"{snapshot.Companies.Count} compagnies déployées",
            13));

        Grid.SetColumnSpan(header, 2);
        root.Children.Add(header);

        var left = new StackPanel
        {
            Spacing = 9,
            Margin = new Thickness(0, 0, 12, 0)
        };

        left.Children.Add(Label("Secteurs", 18, true));

        _sectorList = new ListBox
        {
            ItemsSource = snapshot.Sectors
                .Select(s =>
                    $"{s.Name} — " +
                    $"{snapshot.Companies.Count(c => c.SectorId == s.Id)} " +
                    "compagnies")
                .ToArray(),
            MinHeight = 225
        };

        left.Children.Add(_sectorList);

        left.Children.Add(new Separator());
        left.Children.Add(Label("Ordres divisionnaires", 16, true));

        foreach (var order in snapshot.Orders)
        {
            left.Children.Add(Label(
                $"{order.DivisionName}\n" +
                $"Posture : {PostureText(order.Posture)}\n" +
                $"Priorité : {SectorName(order.PrioritySectorId)}\n" +
                $"Réserve : {order.ReserveBattalions} bataillon(s)",
                12));
        }

        Grid.SetRow(left, 1);
        root.Children.Add(left);

        _details = new StackPanel
        {
            Spacing = 12
        };

        var rightScroll = new ScrollViewer
        {
            Content = _details,
            Margin = new Thickness(12, 0, 0, 0)
        };

        Grid.SetRow(rightScroll, 1);
        Grid.SetColumn(rightScroll, 1);
        root.Children.Add(rightScroll);

        Content = root;

        _sectorList.SelectionChanged += (_, _) =>
        {
            if (_sectorList.SelectedIndex >= 0)
                ShowSector(_sectorList.SelectedIndex);
        };

        if (_snapshot.Sectors.Count > 0)
            _sectorList.SelectedIndex = 0;
    }

    private void ShowSector(int index)
    {
        _details.Children.Clear();

        var sector = _snapshot.Sectors[index];

        _details.Children.Add(
            Label(sector.Name, 22, true));

        _details.Children.Add(Label(
            $"Terrain : {sector.Terrain}\n" +
            $"Altitude : {sector.ElevationMeters} m\n" +
            $"Caractéristiques : {sector.Features}",
            13));

        var connections = _snapshot.Connections
            .Where(c =>
                c.SectorAId == sector.Id ||
                c.SectorBId == sector.Id)
            .Select(c =>
            {
                int neighborId =
                    c.SectorAId == sector.Id
                        ? c.SectorBId
                        : c.SectorAId;

                return $"{SectorName(neighborId)} " +
                       $"({c.DistanceMeters:0} m)";
            })
            .ToArray();

        _details.Children.Add(Label(
            "Liaisons : " +
            (connections.Length == 0
                ? "Aucune"
                : string.Join(", ", connections)),
            13));

        _details.Children.Add(new Separator());

        var companies = _snapshot.Companies
            .Where(c => c.SectorId == sector.Id)
            .OrderBy(c => c.ParticipantIndex)
            .ThenBy(c => c.BattalionId)
            .ThenBy(c => c.CompanyId)
            .ToArray();

        _details.Children.Add(Label(
            $"Formations présentes : {companies.Length} compagnies",
            16, true));

        // Les identifiants des bataillons sont locaux
        // à chaque division.
        var groups = companies.GroupBy(c =>
            (c.ParticipantIndex, c.BattalionId));

        foreach (var group in groups)
        {
            var first = group.First();

            var block = new StackPanel
            {
                Spacing = 5
            };

            block.Children.Add(Label(
                $"{first.CountryName ?? "Pays inconnu"} — " +
                $"{first.DivisionName}",
                14, true));

            block.Children.Add(
                Label(first.BattalionName, 13, true));

            foreach (var company in group)
            {
                block.Children.Add(Label(
                    $"{company.CompanyName} — " +
                    $"{RoleText(company.Role)}\n" +
                    $"Personnel : {company.PersonnelAvailable:N0} · " +
                    $"Chars opérationnels : {company.OperationalTanks}\n" +
                    $"Affectation : {ReasonText(company.AssignmentReason)}",
                    12));
            }

            _details.Children.Add(new Border
            {
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                Child = block
            });
        }

        if (companies.Length == 0)
            _details.Children.Add(
                Label("Aucune compagnie déployée.", 13));

        _details.Children.Add(new Separator());

        _details.Children.Add(Label(
            "La présence de formations opposées dans un secteur " +
            "ne constitue pas encore un affrontement calculé.",
            12));
    }

    private string SectorName(int sectorId)
    {
        return _snapshot.Sectors
            .FirstOrDefault(s => s.Id == sectorId)
            ?.Name ?? $"Secteur #{sectorId}";
    }

    private static string PostureText(
        TacticalPosture posture) =>
        posture switch
        {
            TacticalPosture.Attack => "Attaque",
            TacticalPosture.Defend => "Défense",
            _ => "Inconnue"
        };

    private static string RoleText(
        CompanyDeploymentRole role) =>
        role switch
        {
            CompanyDeploymentRole.FrontLine => "Première ligne",
            CompanyDeploymentRole.Support => "Soutien",
            CompanyDeploymentRole.Reserve => "Réserve",
            _ => "Inconnu"
        };

    private static string ReasonText(
        TacticalAssignmentReason? reason) =>
        reason switch
        {
            TacticalAssignmentReason.PriorityObjective =>
                "Objectif prioritaire",
            TacticalAssignmentReason.SecondaryObjective =>
                "Objectif secondaire",
            TacticalAssignmentReason.DivisionReserve =>
                "Réserve divisionnaire",
            null => "Origine non documentée",
            _ => "Raison inconnue"
        };

    private static TextBlock Label(
        string text,
        double size = 14,
        bool bold = false)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold
                ? FontWeight.Bold
                : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap
        };
    }
}
