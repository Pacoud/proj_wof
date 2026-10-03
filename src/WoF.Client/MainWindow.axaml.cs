using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using WoF.Simulation.Logistics;

using WoF.Client.Prototype;

using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Client;

public partial class MainWindow : Window
{
    private PrototypeScenario _scenario;

    private Country? _selectedCountry;

    private Province? _selectedProvince;

    private Division? _selectedDivision;


    public MainWindow()
    {
        InitializeComponent();

        _scenario =
            PrototypeScenario.Create();

        LoadScenario();
    }


    private void LoadScenario()
    {
        CountrySelector.ItemsSource = 
        _scenario.CommandableCountries;

        _selectedCountry = _scenario.PlayerCountry;

        CountrySelector.SelectedItem = _selectedCountry;

        RefreshDivisionSelector();

        UpdateInterface(); 

    }

    private void RefreshDivisionSelector()
    {
        if (_selectedCountry == null)
        {
            DivisionSelector.ItemsSource =
                null;

            _selectedDivision =
                null;

            return;
        }

        var divisions =
            _scenario.GetDivisionsFor(
                _selectedCountry
            );

        DivisionSelector.ItemsSource =
            divisions;

        if (divisions.Count > 0)
        {
            _selectedDivision =
                divisions[0];

            DivisionSelector.SelectedItem =
                _selectedDivision;
        }
        else
        {
            _selectedDivision =
                null;

            DivisionSelector.SelectedItem =
                null;
        }
    }


    private void OnCountrySelectionChanged(
    object? sender,
    SelectionChangedEventArgs e)
    {
        _selectedCountry =
            CountrySelector.SelectedItem
                as Country;

        RefreshDivisionSelector();

        UpdateDivisionInfo();
    }



    private void UpdateInterface()
    {
        HourText.Text =
            $"Heure : {_scenario.Simulation.Clock.CurrentHour}";

        RenderMap();

        UpdateProvinceInfo();

        UpdateDivisionInfo();
    }


    private void RenderMap()
    {
        MapCanvas.Children.Clear();

        DrawConnections();

        DrawProvinces();

        DrawDivisions();

        DrawSupplyDepots();

        DrawBattleMarkers();

        DrawDivisions();
    }


    private void DrawSupplyDepots()
    {
        foreach (var depot in _scenario.SupplyDepots)
        {
            MapProvince? province =
                FindMapProvince(
                    depot.Position
                );

            if (province == null)
                continue;

            var depotMarker =
                new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = Brushes.DarkSlateBlue,
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(2),
                    Child =
                        new TextBlock
                        {
                            Text = "D",
                            Foreground = Brushes.White,
                            HorizontalAlignment =
                                Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment =
                                Avalonia.Layout.VerticalAlignment.Center,
                            TextAlignment =
                                TextAlignment.Center
                        }
                };

            ToolTip.SetTip(
                depotMarker,
                $"{depot.Name}\n" +
                $"Stock : {depot.FuelStock:F0}\n" +
                $"Débit : {depot.FuelTransferPerHour:F0}/h"
            );

            Canvas.SetLeft(
                depotMarker,
                province.X + 40
            );

            Canvas.SetTop(
                depotMarker,
                province.Y - 42
            );

            MapCanvas.Children.Add(
                depotMarker
            );
        }
    }


    private void DrawBattleMarkers()
    {
        var engagementGroups =
            _scenario.Simulation.Divisions
                .Where(
                    division =>
                        division.CurrentEngagement != null
                )
                .GroupBy(
                    division =>
                        division.CurrentEngagement!
                )
                .ToList();

        foreach (var group in engagementGroups)
        {
            var participants =
                group.ToList();

            bool provinceBattle =
                participants.All(
                    division =>
                        division.CurrentProvince != null
                );

            if (provinceBattle)
            {
                Province province =
                    participants[0]
                        .CurrentProvince!;

                MapProvince? mapProvince =
                    FindMapProvince(
                        province
                    );

                if (mapProvince == null)
                    continue;

                DrawBattleMarker(
                    x: mapProvince.X,
                    y: mapProvince.Y - 52,
                    label: "⚔",
                    tooltip:
                        $"Bataille en province : {province.Name}\n" +
                        $"Participants : {participants.Count}"
                );

                continue;
            }

            var positions =
                participants
                    .Select(
                        division =>
                            GetDivisionPosition(
                                division
                            )
                    )
                    .Where(
                        position => position != null
                    )
                    .Select(
                        position => position!.Value
                    )
                    .ToList();

            if (positions.Count == 0)
                continue;

            double averageX =
                positions.Average(
                    position => position.X
                );

            double averageY =
                positions.Average(
                    position => position.Y
                );

            DrawBattleMarker(
                x: averageX,
                y: averageY - 18,
                label: "⚔",
                tooltip:
                    $"Engagement sur liaison\n" +
                    $"Participants : {participants.Count}"
            );
        }
    }

        // *** HELPER *** //
    private void DrawBattleMarker(
    double x,
    double y,
    string label,
    string tooltip)
    {
        var marker =
            new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = Brushes.DarkRed,
                BorderBrush = Brushes.Gold,
                BorderThickness = new Thickness(2),
                Child =
                    new TextBlock
                    {
                        Text = label,
                        Foreground = Brushes.White,
                        FontSize = 18,
                        HorizontalAlignment =
                            Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment =
                            Avalonia.Layout.VerticalAlignment.Center,
                        TextAlignment =
                            TextAlignment.Center
                    }
            };

        ToolTip.SetTip(
            marker,
            tooltip
        );

        Canvas.SetLeft(
            marker,
            x - 17
        );

        Canvas.SetTop(
            marker,
            y - 17
        );

        MapCanvas.Children.Add(
            marker
        );
    }


    private void DrawConnections()
    {
        foreach (var link in _scenario.Links)
        {
            MapProvince? first =
                FindMapProvince(
                    link.ProvinceA
                );

            MapProvince? second =
                FindMapProvince(
                    link.ProvinceB
                );

            if (first == null
                || second == null)
            {
                continue;
            }

            var line =
                new Line
                {
                    StartPoint =
                        new Point(
                            first.X,
                            first.Y
                        ),

                    EndPoint =
                        new Point(
                            second.X,
                            second.Y
                        ),

                    Stroke =
                        Brushes.Gray,

                    StrokeThickness =
                        3
                };

            MapCanvas.Children.Add(
                line
            );
        }
    }


    private void DrawProvinces()
    {
        foreach (var mapProvince
                 in _scenario.Provinces)
        {
            Province province =
                mapProvince.Province;

            var button =
                new Button
                {
                    Width = 120,
                    Height = 58,

                    Content =
                        $"{province.Name}\n{province.Terrain}",

                    HorizontalContentAlignment =
                        Avalonia.Layout
                            .HorizontalAlignment
                            .Center,

                    VerticalContentAlignment =
                        Avalonia.Layout
                            .VerticalAlignment
                            .Center,

                    Background =
                        GetProvinceBrush(
                            province
                        ),

                    BorderBrush =
                        ReferenceEquals(
                            province,
                            _selectedProvince
                        )
                            ? Brushes.Gold
                            : Brushes.Black,

                    BorderThickness =
                        ReferenceEquals(
                            province,
                            _selectedProvince
                        )
                            ? new Thickness(4)
                            : new Thickness(1)
                };


            button.Click +=
                (_, _) =>
                {
                    _selectedProvince =
                        province;

                    UpdateInterface();
                };


            Canvas.SetLeft(
                button,
                mapProvince.X - 60
            );

            Canvas.SetTop(
                button,
                mapProvince.Y - 29
            );


            MapCanvas.Children.Add(
                button
            );
        }
    }


    private void DrawDivisions()
    {
        var positions =
            new Dictionary<
                (int X, int Y),
                int
            >();


        foreach (Division division
                 in _scenario.Simulation.Divisions)
        {
            (double X, double Y)? position =
                GetDivisionPosition(
                    division
                );

            if (position == null)
                continue;


            int roundedX =
                (int)Math.Round(
                    position.Value.X
                );

            int roundedY =
                (int)Math.Round(
                    position.Value.Y
                );


            var key =
                (
                    roundedX,
                    roundedY
                );


            positions.TryGetValue(
                key,
                out int stackIndex
            );

            positions[key] =
                stackIndex + 1;


            var marker =
                new Button
                {
                    Width = 110,
                    Height = 28,

                    FontSize = 11,

                    Content =
                        division.Name,

                    Background =
                        ReferenceEquals(
                            division.Country,
                            _scenario.PlayerCountry
                        )
                            ? Brushes.LightBlue
                            : Brushes.LightCoral
                };


            marker.Click +=
                (_, _) =>
                {
                    _selectedDivision =
                        division;

                    _selectedCountry =
                        division.Country;

                    CountrySelector.SelectedItem =
                        _selectedCountry;

                    RefreshDivisionSelector();

                    DivisionSelector.SelectedItem =
                        division;

                    UpdateInterface();
                };
            


            Canvas.SetLeft(
                marker,
                position.Value.X - 55
            );

            Canvas.SetTop(
                marker,
                position.Value.Y
                + 35
                + stackIndex * 30
            );


            MapCanvas.Children.Add(
                marker
            );
        }
    }
    


    private (double X, double Y)?
        GetDivisionPosition(
            Division division)
    {
        if (division.CurrentProvince != null)
        {
            MapProvince? province =
                FindMapProvince(
                    division.CurrentProvince
                );

            if (province == null)
                return null;

            return (
                province.X,
                province.Y
            );
        }


        if (division.Transit != null)
        {
            MapProvince? origin =
                FindMapProvince(
                    division.Transit.Origin
                );

            MapProvince? destination =
                FindMapProvince(
                    division.Transit.Destination
                );


            if (origin == null
                || destination == null)
            {
                return null;
            }


            double progress =
                division.Transit.Progress;


            double x =
                origin.X
                + (
                    destination.X
                    - origin.X
                )
                * progress;


            double y =
                origin.Y
                + (
                    destination.Y
                    - origin.Y
                )
                * progress;


            return (
                x,
                y
            );
        }


        return null;
    }


    private MapProvince? FindMapProvince(
        Province province)
    {
        return _scenario.Provinces
            .FirstOrDefault(
                mapProvince =>
                    ReferenceEquals(
                        mapProvince.Province,
                        province
                    )
            );
    }


    private IBrush GetProvinceBrush(
        Province province)
    {
        if (ReferenceEquals(
                province.Controller,
                _scenario.PlayerCountry))
        {
            return Brushes.LightBlue;
        }

        if (ReferenceEquals(
                province.Controller,
                _scenario.EnemyCountry))
        {
            return Brushes.LightCoral;
        }

        return Brushes.LightGray;
    }


    private void OnTickClick(
        object? sender,
        RoutedEventArgs e)
    {
        _scenario.Simulation.Tick();

        OrderResultText.Text =
            string.Empty;

        UpdateInterface();
    }


    private void OnResetClick(
        object? sender,
        RoutedEventArgs e)
    {
        _scenario =
            PrototypeScenario.Create();

        _selectedProvince =
            null;

        _selectedDivision =
            null;

        LoadScenario();
    }


    private void OnDivisionSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        _selectedDivision =
            DivisionSelector.SelectedItem
                as Division;

        UpdateDivisionInfo();
    }


    private void OnMoveClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_selectedDivision == null)
        {
            OrderResultText.Text =
                "Sélectionne une division.";

            return;
        }

        if (_selectedProvince == null)
        {
            OrderResultText.Text =
                "Sélectionne une province.";

            return;
        }


        bool accepted =
            _scenario.Simulation
                .TryOrderMoveTo(
                    _selectedDivision,
                    _selectedProvince
                );


        OrderResultText.Text =
            accepted
                ? "Ordre accepté."
                : "Ordre impossible.";


        UpdateInterface();
    }


    private void UpdateProvinceInfo()
    {
        if (_selectedProvince == null)
        {
            SelectedProvinceText.Text =
                "Aucune";

            return;
        }


        string controller =
            _selectedProvince.Controller
                ?.Name
            ?? "Aucun";

        SupplyDepot? depot = 
            _scenario.SupplyDepots.FirstOrDefault(
                candidate =>
                    ReferenceEquals(
                        candidate.Position,
                        _selectedProvince
                    )
            );

        string depotText = 
            depot == null 
                ?"Aucun Dépot"
                : $"Dépot : {depot.Name}" +
                $"({depot.FuelStock:F0})";

        SelectedProvinceText.Text =
            $"{_selectedProvince.Name}\n" +
            $"Terrain : {_selectedProvince.Terrain}\n" +
            $"Contrôle : {controller}\n" +
            depotText;
    }


    private void UpdateDivisionInfo()
    {
        if (_selectedDivision == null)
        {
            DivisionInfoText.Text =
                "Aucune division sélectionnée.";

            return;
        }


        Division division =
            _selectedDivision;


        string position =
            division.CurrentProvince != null
                ? division.CurrentProvince.Name
                : division.Transit != null
                    ? $"{division.Transit.Origin.Name}" +
                      $" → " +
                      $"{division.Transit.Destination.Name}" +
                      $" ({division.Transit.Progress:P0})"
                    : "Inconnue";


        var personnel =
            division.Composition.Manpower;

        var tanks = division.Composition.Tanks;


        DivisionInfoText.Text =
            $"{division.Name}\n\n" +

            $"État : {division.OperationalState}\n" +

            $"Position : {position}\n\n" +

            $"Organisation : " +
            $"{division.Organization:F1}" +
            $" / {division.MaxOrganization:F1}\n" +

            $"Carburant : " +
            $"{division.Fuel:F1}" +
            $" / {division.FuelCapacity:F1}\n\n" +


            $"Chars opérationnels : " +
            $"{tanks.Operational:N0}\n" +

            $"Chars endommagés : " +
            $"{tanks.Damaged:N0}\n" +

            $"Chars détruits : " +
            $"{tanks.Destroyed:N0}\n"+

            $"Personnel apte : " +

            $"{personnel.Current:N0}\n" +

            $"KIA : " +
            $"{personnel.KilledInAction:N0}\n" +

            $"WIA indisponibles : " +
            $"{personnel.WoundedUnavailable:N0}";


    }
}