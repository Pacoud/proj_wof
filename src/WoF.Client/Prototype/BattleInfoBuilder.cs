using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using WoF.Simulation.Military;
using WoF.Simulation.Military.Combat;

namespace WoF.Client.Prototype;

public static class BattleInfoBuilder
{
    public static Control Build(
        Engagement engagement,
        long currentHour)
    {
        var root =
            new StackPanel
            {
                Spacing = 8,
                Width = 280
            };


        // -------------------------------------------------
        // EN-TÊTE
        // -------------------------------------------------

        string title =
            engagement.Type
                == EngagementType.ProvinceBattle
                    ? "⚔ Bataille en province"
                    : "⚔ Engagement de rencontre";


        AddText(root, title, bold: true, fontSize: 17);

        AddText(root, BuildLocationText(engagement));

        AddText(root, $"Terrain : {engagement.BattleTerrain}");

        long duration = Math.Max(
            0,
            currentHour - engagement.StartHour
        );

        AddText(root, $"Durée : {duration} h");

        root.Children.Add(new Separator());


        // -------------------------------------------------
        // PARTICIPANTS
        // -------------------------------------------------

        if (engagement.Type
            == EngagementType.ProvinceBattle)
        {
            AddGroup(
                root,
                "ATTAQUANTS",
                engagement.Attackers
            );

            AddGroup(
                root,
                "DÉFENSEURS",
                engagement.Defenders
            );


            // Certains scénarios/tests peuvent encore
            // avoir des participants sans rôle.
            var neutral =
                engagement.Participants
                    .Where(
                        division =>
                            engagement.GetRole(
                                division
                            )
                            == EngagementRole.None
                    )
                    .ToList();

            if (neutral.Count > 0)
            {
                AddGroup(
                    root,
                    "AUTRES PARTICIPANTS",
                    neutral
                );
            }
        }
        else
        {
            foreach (var countryGroup
                     in engagement.Participants
                         .Where(
                             division =>
                                 division.Country != null
                         )
                         .GroupBy(
                             division =>
                                 division.Country!
                         ))
            {
                AddGroup(
                    root,
                    countryGroup.Key.Name
                        .ToUpperInvariant(),
                    countryGroup
                );
            }
        }


        return new Border
        {
            Padding =
                new Thickness(12),

            CornerRadius =
                new CornerRadius(6),

            BorderThickness =
                new Thickness(1),

            BorderBrush =
                Brushes.Gray,

            Background =
                Brushes.Black,

            Child =
                root
        };
    }


    private static string BuildLocationText(
        Engagement engagement)
    {
        if (engagement.Type
                == EngagementType.ProvinceBattle
            &&
            engagement.Province != null)
        {
            return engagement.Province.Name;
        }


        if (engagement.ConnectionA != null
            &&
            engagement.ConnectionB != null
            &&
            engagement.ConnectionProgress != null)
        {
            return
                $"{engagement.ConnectionA.Name}" +
                $" ↔ " +
                $"{engagement.ConnectionB.Name}" +
                $" — " +
                $"{engagement.ConnectionProgress.Value:P0}";
        }


        return "Position inconnue";
    }


    private static void AddGroup(
        StackPanel root,
        string title,
        IEnumerable<Division> divisions)
    {
        var list =
            divisions.ToList();

        if (list.Count == 0)
            return;


        AddText(
            root,
            title,
            bold: true,
            margin: new Thickness(0, 5, 0, 0)
        );

        foreach (Division division in list)
        {
            root.Children.Add(
                BuildDivisionInfo(division)
            );
        }
    }


    private static Control BuildDivisionInfo(
        Division division)
    {
        var personnel =
            division.Composition.Manpower;

        var tanks =
            division.Composition.Tanks;


        var content =
            new StackPanel
            {
                Spacing = 2
            };


        content.Children.Add(
        MakeText(division.Name, bold: true)
        );
                


        content.Children.Add(
            new TextBlock
            {
                Text =
                    $"Org : " +
                    $"{division.Organization:F1}" +
                    $" / " +
                    $"{division.MaxOrganization:F1}"
            }
        );


        content.Children.Add(
            new TextBlock
            {
                Text =
                    $"Hommes : " +
                    $"{personnel.Current:N0}"
            }
        );


        content.Children.Add(
            new TextBlock
            {
                Text =
                    $"Fuel : " +
                    $"{division.Fuel:F1}" +
                    $" / " +
                    $"{division.FuelCapacity:F1}"
            }
        );


        if (tanks.Authorized > 0)
        {
            content.Children.Add(
                new TextBlock
                {
                    Text =
                        $"Chars : " +
                        $"{tanks.Operational}" +
                        $" opérationnels, " +
                        $"{tanks.Damaged} endommagés"
                }
            );
        }


        content.Children.Add(
            new TextBlock
            {
                Text =
                    $"KIA : " +
                    $"{personnel.KilledInAction:N0}" +
                    $" | WIA : " +
                    $"{personnel.WoundedUnavailable:N0}"
            }
        );


        return new Border
        {
            Padding =
                new Thickness(
                    6
                ),

            Margin =
                new Thickness(
                    0,
                    2
                ),

            BorderThickness =
                new Thickness(
                    0,
                    0,
                    0,
                    1
                ),

            BorderBrush =
                Brushes.DimGray,

            Child =
                content
        };
    }


    private static TextBlock MakeText(
    string text,
    bool bold = false,
    double fontSize = 12,
    Thickness? margin = null)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontWeight = bold
                ? FontWeight.Bold
                : FontWeight.Normal,
            FontSize = fontSize,
            Margin = margin ?? new Thickness(0)
        };
    }

    private static void AddText(
    Panel panel,
    string text,
    bool bold = false,
    double fontSize = 12,
        Thickness? margin = null)
    {
        panel.Children.Add(
            MakeText(text, bold, fontSize, margin)
        );
    }



}