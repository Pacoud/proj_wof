using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;

using WoF.Client.Prototype;

using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Client;

public partial class MainWindow : Window
{
    private PrototypeScenario _scenario;

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
        DivisionSelector.ItemsSource =
            _scenario.PlayerDivisions;

        if (_scenario.PlayerDivisions.Count > 0)
        {
            DivisionSelector.SelectedIndex =
                0;

            _selectedDivision =
                _scenario.PlayerDivisions[0];
        }

        UpdateInterface();
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


            if (ReferenceEquals(
                    division.Country,
                    _scenario.PlayerCountry))
            {
                marker.Click +=
                    (_, _) =>
                    {
                        _selectedDivision =
                            division;

                        DivisionSelector.SelectedItem =
                            division;

                        UpdateInterface();
                    };
            }


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


        SelectedProvinceText.Text =
            $"{_selectedProvince.Name}\n" +
            $"Terrain : {_selectedProvince.Terrain}\n" +
            $"Contrôle : {controller}";
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

            $"Personnel apte : " +
            $"{personnel.Current:N0}\n" +

            $"KIA : " +
            $"{personnel.KilledInAction:N0}\n" +

            $"WIA indisponibles : " +
            $"{personnel.WoundedUnavailable:N0}";
    }
}