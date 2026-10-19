using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// The online media meter's "?" slip (owner, 2026-10-19): how the daily share works, in the
/// leash "?" slips' paper look. The figures are the live ones (the server can retune them); the
/// words come from <see cref="OnlineMeterRules.HelpParagraphs"/>. Opened by
/// <see cref="LeashHelpButton.Create(System.Func{UIElement}, string, double)"/>.
/// </summary>
public sealed class OnlineMediaHelpSlip : Border
{
    private static readonly FontFamily Sans = new("Segoe UI");

    public OnlineMediaHelpSlip()
    {
        Width = 320;
        Background = LeashHelpSlip.Paper;
        CornerRadius = new CornerRadius(6);
        BorderBrush = FriendsLook.Frozen(Color.FromRgb(0xD8, 0xCF, 0xBF));
        BorderThickness = new Thickness(0, 0, 0, 1);
        Tag = "online-help-slip";
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.25, Color = Color.FromRgb(0x1E, 0x1B, 0x22) };

        var root = new StackPanel();
        root.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(6, 6, 0, 0), Background = LeashHelpSlip.LilacAccent.BrightBrush });
        var sp = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        root.Children.Add(sp);
        sp.Children.Add(new TextBlock
        {
            Text = Loc.Get(OnlineMeterRules.HelpTitleKey),
            FontFamily = Sans,
            FontWeight = FontWeights.ExtraBold,
            FontSize = 17,
            Foreground = LeashHelpSlip.Ink,
            TextWrapping = TextWrapping.Wrap,
        });

        var lines = OnlineMeterRules.HelpParagraphs(Loc.Get,
            ScrolllerAllowance.DailyBytes(ScrolllerPlan.Free), ScrolllerAllowance.FreeFirstDayBytes,
            ScrolllerAllowance.DailyBytes(ScrolllerPlan.Basic), ScrolllerAllowance.DailyBytes(ScrolllerPlan.Prime),
            ScrolllerDiskCache.CapBytes, System.Globalization.CultureInfo.CurrentUICulture);
        for (int i = 0; i < lines.Length; i++)
        {
            var last = i == lines.Length - 1;
            sp.Children.Add(new TextBlock
            {
                Text = lines[i],
                FontFamily = last ? FriendsLook.Mono : Sans,
                FontSize = last ? 11.5 : 13,
                LineHeight = last ? double.NaN : 18,
                Foreground = last ? LeashHelpSlip.Muted : LeashHelpSlip.Ink,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, i == 0 ? 10 : 8, 0, 0),
            });
        }
        Child = root;
    }
}
