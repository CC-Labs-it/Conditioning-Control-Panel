using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

/// <summary>WPF Controls/OnlineMediaHelpSlip.cs: the online meter's paper help card.</summary>
public sealed class OnlineMediaHelpSlip : Border
{
    private static readonly FontFamily Sans = new("Segoe UI");

    public OnlineMediaHelpSlip()
    {
        Width = 320;
        Background = LeashHelpSlip.Paper;
        CornerRadius = new CornerRadius(6);
        BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xD8, 0xCF, 0xBF));
        BorderThickness = new Thickness(0, 0, 0, 1);
        Tag = "online-help-slip";
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 18, Color = Color.FromArgb(0x40, 0x1E, 0x1B, 0x22) });

        var root = new StackPanel();
        root.Children.Add(new Border
        {
            Height = 6, CornerRadius = new CornerRadius(6, 6, 0, 0), Background = LeashHelpSlip.LilacAccent.BrightBrush,
        });
        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        root.Children.Add(body);
        body.Children.Add(new TextBlock
        {
            Text = Loc.Get(OnlineMeterRules.HelpTitleKey), FontFamily = Sans, FontWeight = FontWeight.ExtraBold,
            FontSize = 17, Foreground = LeashHelpSlip.Ink, TextWrapping = TextWrapping.Wrap,
        });
        var lines = OnlineMeterRules.HelpParagraphs(Loc.Get);
        for (int i = 0; i < lines.Length; i++)
        {
            bool last = i == lines.Length - 1;
            body.Children.Add(new TextBlock
            {
                Text = lines[i], FontFamily = last ? FriendsDrawer.Mono : Sans,
                FontSize = last ? 11.5 : 13, LineHeight = last ? double.NaN : 18,
                Foreground = last ? LeashHelpSlip.Muted : LeashHelpSlip.Ink, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, i == 0 ? 10 : 8, 0, 0),
            });
        }
        Child = root;
    }
}
