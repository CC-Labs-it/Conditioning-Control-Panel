// WPF Controls/Leash/Explain/LeashExplainer.cs:63 and LeashExplainCard.cs:99.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashHolderIntro : Window
{
    internal LeashHolderIntro(string name, Action? offer)
    {
        Title = offer == null || string.IsNullOrWhiteSpace(name) ? Loc.Get("leash_explain_title") : Loc.GetF("leash_explain_offer_title", name.Trim());
        Tag = "leash-explainer";
        WindowDecorations = WindowDecorations.None;
        Width = 640; SizeToContent = SizeToContent.Height; CanResize = false;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = F.Foot;
        var root = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        var title = F.Label(Title, 20, F.Text, F.Display, FontWeight.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(title);
        var x = F.Pill("×", F.Raised, F.Text, "leash-explain-close"); x.Click += (_, _) => Close();
        Grid.SetColumn(x, 1); header.Children.Add(x); root.Children.Add(header);
        var pictures = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        // Flat, finished-state drawings: offer, collar and level, eye and day, scissors cutting.
        string[] paths = {
            "M 17,20 A 8,8 0 1 1 33,20 A 8,8 0 1 1 17,20 M 10,58 Q 10,35 25,35 Q 40,35 40,58 M 40,48 C 55,55 58,25 73,38 M 73,38 A 6,6 0 1 1 85,38 A 6,6 0 1 1 73,38",
            "M 20,18 A 8,8 0 1 1 36,18 A 8,8 0 1 1 20,18 M 15,55 Q 15,32 28,32 Q 41,32 41,55 M 19,35 L 37,35 M 53,53 L 53,42 M 65,53 L 65,32 M 77,53 L 77,22",
            "M 15,25 Q 50,-5 85,25 Q 50,55 15,25 Z M 50,18 A 7,7 0 1 1 49.9,18 M 20,59 L 28,59 M 33,59 L 41,59 M 46,59 L 54,59 M 59,59 L 67,59 M 72,59 L 80,59",
            "M 10,58 L 42,58 M 58,58 L 90,58 M 37,26 A 7,7 0 1 1 36.9,26 M 37,45 A 7,7 0 1 1 36.9,45 M 43,29 L 76,49 M 43,42 L 76,22" };
        for (int i = 0; i < 4; i++)
        {
            var drawing = new Path { Data = Geometry.Parse(paths[i]), Stroke = i == 3 ? F.Mint : F.Gold, StrokeThickness = 2.5, Stretch = Stretch.Uniform, Width = 100, Height = 70, Tag = "leash-explain-art:" + (i + 1) };
            var caption = F.Label(Loc.Get($"leash_explain_step{i + 1}_holder"), 13, i == 3 ? F.Mint : F.Text, F.Display, FontWeight.SemiBold);
            caption.TextWrapping = TextWrapping.Wrap; caption.TextAlignment = TextAlignment.Center; caption.HorizontalAlignment = HorizontalAlignment.Center;
            var panel = new Border { Tag = "leash-explain-panel:" + (i + 1), Background = F.Raised, BorderBrush = i == 3 ? F.Mint : F.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(3), Padding = new Thickness(6), Child = new StackPanel { Spacing = 6, Children = { drawing, caption } } };
            Grid.SetColumn(panel, i); pictures.Children.Add(panel);
        }
        root.Children.Add(pictures);
        foreach (var key in new[] { "leash_explain_holder_see", "leash_explain_holder_cut" })
        {
            var line = F.Label(Loc.Get(key), 13, F.Text); line.TextWrapping = TextWrapping.Wrap; line.TextTrimming = TextTrimming.None; root.Children.Add(line);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var close = F.Pill(Loc.Get(offer == null ? "leash_explain_got_it" : "leash_explain_not_now"), F.Raised, F.Text, "leash-explain-not-now");
        close.Click += (_, _) => Close(); buttons.Children.Add(close);
        if (offer != null)
        {
            var send = F.Pill(Loc.Get("leash_explain_offer"), F.Raised, F.Gold, "leash-explain-offer");
            send.Click += (_, _) => { Close(); offer(); }; buttons.Children.Add(send);
        }
        root.Children.Add(buttons);
        Content = new ScrollViewer { Content = root, MaxHeight = 700 };
    }
}
