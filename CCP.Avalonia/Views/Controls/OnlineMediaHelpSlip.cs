using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

/// <summary>The daily share explained in the same paper slip as the leash help.</summary>
public sealed class OnlineMediaHelpSlip : Border
{
    private static readonly FontFamily Sans = new("Segoe UI");

    public OnlineMediaHelpSlip()
    {
        Width = 320;
        Background = LeashHelpSlip.Paper;
        CornerRadius = new CornerRadius(6);
        BorderBrush = Brush.Parse("#D8CFBF");
        BorderThickness = new Thickness(0, 0, 0, 1);
        BoxShadow = BoxShadows.Parse("0 6 18 0 #401E1B22");
        Tag = "online-help-slip";
        var root = new StackPanel();
        root.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(6, 6, 0, 0), Background = LeashHelpSlip.LilacAccent.BrightBrush });
        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        root.Children.Add(body);
        body.Children.Add(new TextBlock
        {
            Text = Loc.Get(OnlineMeterRules.HelpTitleKey), FontWeight = FontWeight.ExtraBold,
            FontFamily = Sans, FontSize = 17, Foreground = LeashHelpSlip.Ink, TextWrapping = TextWrapping.Wrap,
        });
        var lines = OnlineMeterRules.HelpParagraphs(Loc.Get);
        for (int i = 0; i < lines.Length; i++)
            body.Children.Add(new TextBlock
            {
                Text = lines[i], FontSize = i == lines.Length - 1 ? 11.5 : 13,
                FontFamily = i == lines.Length - 1 ? FriendsDrawer.Mono : Sans,
                LineHeight = i == lines.Length - 1 ? double.NaN : 18,
                Foreground = i == lines.Length - 1 ? LeashHelpSlip.Muted : LeashHelpSlip.Ink,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, i == 0 ? 10 : 8, 0, 0),
            });
        Child = root;
    }
}
