
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using WoF.Simulation.Military.Combat.Tactical;
using System.Diagnostics;

namespace WoF.Client.Prototype;

public sealed class TacticalIntelligenceDemoWindow : Window
{

    private double _previousUiRefreshMs;

    private TacticalIntelligenceDemoScenario _scenario = new();

    private readonly Canvas _map = new()
    {
        Width = 740,
        Height = 440,
        Background = Brushes.DarkSlateGray
    };

    private readonly StackPanel _details = new()
    {
        Spacing = 12
    };

    private readonly TextBlock _clock = new()
    {
        FontSize = 16,
        FontWeight = FontWeight.Bold
    };

    private readonly TextBlock _eventInfo = new()
    {
        TextWrapping = TextWrapping.Wrap
    };

    private readonly ComboBox _viewSelector = new()
    {
        Width = 230,
        ItemsSource = new[]
        {
            "Développeur — réalité",
            "France — compagnie",
            "France — bataillon",
            "France — division",
            "Allemagne — compagnie"
        },
        SelectedIndex = 0
    };

    private readonly Button _nextButton = new()
    {
        Content = "Étape suivante"
    };

    public TacticalIntelligenceDemoWindow()
    {
        Title = "WoF — Laboratoire tactique C2.3a";
        Width = 1180;
        Height = 700;
        MinWidth = 950;
        MinHeight = 580;

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,350"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(14)
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 14),
            VerticalAlignment = VerticalAlignment.Center
        };

        toolbar.Children.Add(new TextBlock
        {
            Text = "Renseignement tactique",
            FontSize = 19,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        toolbar.Children.Add(_clock);
        toolbar.Children.Add(_viewSelector);
        toolbar.Children.Add(_nextButton);

        var resetButton = new Button
        {
            Content = "Recommencer"
        };

        toolbar.Children.Add(resetButton);

        Grid.SetColumnSpan(toolbar, 2);
        root.Children.Add(toolbar);

        var left = new StackPanel
        {
            Spacing = 10
        };

        left.Children.Add(_eventInfo);

        left.Children.Add(new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives
                        .ScrollBarVisibility.Auto,
                Content = _map
            }
        });

        left.Children.Add(Text(
            "Carte expérimentale : les points ennemis sont " +
            "filtrés selon le renseignement connu.",
            12));

        Grid.SetRow(left, 1);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var right = new ScrollViewer
        {
            Margin = new Thickness(14, 0, 0, 0),
            Content = _details
        };

        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        Content = root;

        _viewSelector.SelectionChanged +=
            (_, _) => Refresh();

        _nextButton.Click += (_, _) =>
        {
            _scenario.Advance();
            Refresh();
        };

        resetButton.Click += (_, _) =>
        {
            _scenario.Dispose();
            _scenario = new TacticalIntelligenceDemoScenario();
            Refresh();
        };

        Closed += (_, _) => _scenario.Dispose();

        Refresh();
    }

    private sealed record DisplayReport(
        TacticalEnemyContactReport Contact,
        long? ReceivedMinute);

    private IReadOnlyList<DisplayReport> VisibleReports()
    {
        switch (_viewSelector.SelectedIndex)
        {
            case 1:
                return _scenario.Perception
                    .GetReports(_scenario.FrenchDeployment)
                    .Select(r => new DisplayReport(r, null))
                    .ToArray();

            case 2:
                return _scenario.Relay
                    .GetBattalionReports(_scenario.FrenchBattalion)
                    .Select(r => new DisplayReport(
                        r.Contact, r.ReceivedMinute))
                    .ToArray();

            case 3:
                return _scenario.Relay
                    .GetDivisionReports(_scenario.FrenchDivision)
                    .Select(r => new DisplayReport(
                        r.Contact, r.ReceivedMinute))
                    .ToArray();

            case 4:
                return _scenario.Perception
                    .GetReports(_scenario.GermanDeployment)
                    .Select(r => new DisplayReport(r, null))
                    .ToArray();

            default:
                return Array.Empty<DisplayReport>();
        }
    }

    private void Refresh()
    {
        long minute = _scenario.CurrentMinute;

        _clock.Text =
            $"H{minute / 60:00}:{minute % 60:00}";

        bool developer = _viewSelector.SelectedIndex == 0;

        _eventInfo.Text = developer
            ? $"Événement : {_scenario.DeveloperEvent}"
            : "Vue limitée aux renseignements de la formation " +
              "ou du QG sélectionné.";

        _nextButton.IsEnabled = _scenario.CanAdvance;

        var reports = VisibleReports();

        long uiStart = Stopwatch.GetTimestamp();
        RenderMap(developer, reports);
        RenderDetails(developer, reports);

        _previousUiRefreshMs =
        Stopwatch.GetElapsedTime(uiStart).TotalMilliseconds;
    }

    private static double MapX(TacticalPoint point) =>
        120 + point.XMeters * 2;

    private static double MapY(TacticalPoint point) =>
        220 - point.YMeters * 2;

    private void RenderMap(
        bool developer,
        IReadOnlyList<DisplayReport> reports)
    {
        _map.Children.Clear();

        PutText(
            "PLAINE D'ESSAI — schéma géométrique",
            20, 16, Brushes.White, 15);

        var front = new Line
        {
            StartPoint = new Point(290, 70),
            EndPoint = new Point(290, 370),
            Stroke = Brushes.Gray,
            StrokeThickness = 1
        };

        _map.Children.Add(front);

        int view = _viewSelector.SelectedIndex;

        bool showFrenchTruth = developer || view is 1 or 2 or 3;
        bool showGermanTruth = developer || view == 4;

        if (showFrenchTruth)
        {
            DrawCompany(
                _scenario.FrenchDeployment,
                Brushes.LightSkyBlue,
                "Compagnie française");
        }

        if (showGermanTruth)
        {
            DrawCompany(
                _scenario.GermanDeployment,
                Brushes.IndianRed,
                "Compagnie allemande");
        }

        if (!developer)
        {
            foreach (var report in reports)
                DrawContact(report.Contact);
        }

        PutText(
            developer
                ? "Mode développeur : positions physiques réelles"
                : "Positions amies et renseignements disponibles",
            20, 398, Brushes.White, 12);
    }

    private void DrawCompany(
        CompanyDeployment company,
        IBrush brush,
        string label)
    {
        var position =
            _scenario.Geometry.GetPosition(company);

        if (position is null)
            return;

        double x = MapX(position.Point);
        double y = MapY(position.Point);

        var marker = new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = brush,
            Stroke = Brushes.White,
            StrokeThickness = 2
        };

        Canvas.SetLeft(marker, x - 9);
        Canvas.SetTop(marker, y - 9);

        _map.Children.Add(marker);

        PutText(
            label,
            x - 65,
            y + 17,
            Brushes.White,
            12);
    }

    private void DrawContact(
        TacticalEnemyContactReport report)
    {
        if (report.LastKnownPosition is not TacticalPoint position)
        {
            // Suspicion : aucune coordonnée réelle
            // ne doit être exploitée ici.
            PutText(
                $"? Présence suspectée — secteur " +
                $"{report.LastKnownSectorId}",
                300, 75,
                Brushes.Gold,
                13);

            return;
        }

        double x = MapX(position);
        double y = MapY(position);

        double diameter = Math.Clamp(
            (report.PositionUncertaintyMeters ?? 50) * 1.6,
            24,
            120);

        var uncertainty = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Stroke = Brushes.Gold,
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        };

        Canvas.SetLeft(uncertainty, x - diameter / 2);
        Canvas.SetTop(uncertainty, y - diameter / 2);
        _map.Children.Add(uncertainty);

        PutText(
            $"? Piste {report.TrackId}",
            x - 35,
            y + diameter / 2 + 4,
            Brushes.Gold,
            12);
    }

    private void RenderDetails(
        bool developer,
        IReadOnlyList<DisplayReport> reports)
    {
        _details.Children.Clear();

        _details.Children.Add(
            Text("Renseignements disponibles", 19, true));

        if (developer)
        {
            _details.Children.Add(Text(
                "Cette vue montre la réalité du moteur. " +
                "Les vues des camps ne doivent pas y accéder.",
                12));

            _details.Children.Add(new Separator());

            _details.Children.Add(Text(
                $"Reconstruction UI précédente : " +
                $"{_previousUiRefreshMs:F3} ms\n" +
                $"Radios compagnie Signals : " +
                $"{_scenario.SignalsCompany.Composition!.Radios.Current}\n" +
                $"Compagnie → Bataillon : " +
                RadioStatus(_scenario.CompanyRadio) + "\n" +
                $"Bataillon → Division : " +
                RadioStatus(_scenario.BattalionRadio),
                13));

            _details.Children.Add(new Separator());

            _details.Children.Add(
                Text("Profilage et événements", 16, true));

            _details.Children.Add(Text(
                $"Dernière détection : " +
                $"{_scenario.LastDetectionMs:F3} ms\n" +
                $"Dernier traitement réseau : " +
                $"{_scenario.LastCommunicationMs:F3} ms\n" +
                $"Recherches de routes : " +
                $"{_scenario.Router.SearchCount}\n" +
                $"Réutilisations du cache : " +
                $"{_scenario.Router.CacheHitCount}\n" +
                $"Messages en attente : " +
                $"{_scenario.Relay.PendingMessageCount}\n" +
                $"Segments livrés : " +
                $"{_scenario.Relay.DeliveredHopCount}\n" +
                $"Segments interrompus : " +
                $"{_scenario.Relay.DroppedHopCount}\n" +
                $"Échecs de routage : " +
                $"{_scenario.Relay.RoutingFailureCount}",
                13));

            _details.Children.Add(new Separator());
            _details.Children.Add(Text(
                "Sélectionne la vue d'une formation pour " +
                "consulter ses renseignements sans omniscience.",
                13));

            return;
        }

        if (reports.Count == 0)
        {
            _details.Children.Add(Text(
                "Aucun contact ennemi connu à cet échelon.",
                13));

            return;
        }

        foreach (var item in reports)
        {
            var report = item.Contact;

            bool fresh =
                item.ReceivedMinute is null
                && report.IsCurrentlyObserved
                && report.LastObservedHour
                    == _scenario.CurrentMinute / 60;

            string positionText =
                report.LastKnownPosition is TacticalPoint p
                    ? $"Position estimée : " +
                      $"({p.XMeters:0}, {p.YMeters:0}) m\n" +
                      $"Incertitude : " +
                      $"{report.PositionUncertaintyMeters:0} m"
                    : "Position précise inconnue";

            string receivedText =
                item.ReceivedMinute is long received
                    ? $"\nRapport reçu à H" +
                      $"{received / 60:00}:{received % 60:00}"
                    : "";

            var card = new Border
            {
                Padding = new Thickness(12),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = Text(
                    $"Piste #{report.TrackId}\n" +
                    $"Meilleur renseignement : " +
                    $"{report.BestKnownLevel}\n" +
                    $"Dernière observation : " +
                    $"H{report.LastObservedHour}\n" +
                    $"Contact observé maintenant : " +
                    $"{(fresh ? "Oui" : "Non / non confirmé")}\n" +
                    $"Secteur connu : " +
                    $"{report.LastKnownSectorId}\n" +
                    positionText +
                    receivedText,
                    13)
            };

            _details.Children.Add(card);
        }
    }

    private string RadioStatus(TacticalCommunicationLink link)
    {
        return _scenario.Network.CanUse(link)
            ? "opérationnelle"
            : "indisponible";
    }

    private void PutText(
        string value,
        double x,
        double y,
        IBrush brush,
        double size)
    {
        var label = Text(value, size);
        label.Foreground = brush;

        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        _map.Children.Add(label);
    }

    private static TextBlock Text(
        string value,
        double size,
        bool bold = false)
    {
        return new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = bold
                ? FontWeight.Bold
                : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap
        };
    }
}
