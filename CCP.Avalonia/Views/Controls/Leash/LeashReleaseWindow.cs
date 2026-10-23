// Adapted from WPF LeashCutConfirmWindow. Keys cannot answer a release confirmation.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashReleaseWindow : Window
{
    internal static LeashReleaseWindow? Current { get; private set; }
    internal static void Open(string name, Action onYes)
    {
        if (Current is { } open) { open.Activate(); return; }
        var w = new LeashReleaseWindow(name, onYes);
        Current = w;
        w.Closed += (_, _) => { if (ReferenceEquals(Current, w)) Current = null; };
        w.Show(); w.Activate();
    }
    internal static void CloseCurrent() { var w = Current; Current = null; w?.Close(); }
    internal LeashReleaseWindow() : this("Juno", () => { }) { }
    private LeashReleaseWindow(string name, Action onYes)
    {
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        CanResize = false; ShowInTaskbar = false; Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AddHandler(KeyDownEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
        var sp = new StackPanel { MaxWidth = 380, Margin = new Thickness(26, 22, 26, 20) };
        var title = F.Label(Loc.GetF("leash_release_confirm_title", name), 22, F.Text);
        title.TextWrapping = TextWrapping.Wrap;
        sp.Children.Add(title);
        var body = F.Label(Loc.GetF("leash_release_confirm_body", name), 13, F.Muted);
        body.TextWrapping = TextWrapping.Wrap; body.Margin = new Thickness(0, 8, 0, 16);
        sp.Children.Add(body);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10 };
        var no = F.Pill(Loc.Get("leash_cut_confirm_no"), LeashHolderCard.Shade, F.Text, "leash-release-confirm:no", F.Line2);
        var yes = F.Pill(Loc.Get("leash_release_confirm_yes"), F.Red, LeashAskCard.Ink, "leash-release-confirm:yes", F.Red);
        no.Focusable = yes.Focusable = false;
        no.Click += (_, _) => Close();
        yes.Click += (_, _) => { Close(); onYes(); };
        row.Children.Add(no); row.Children.Add(yes); sp.Children.Add(row);
        Content = new Border { Background = LeashAskCard.Card, BorderBrush = F.Red, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(16), Child = sp, Margin = new Thickness(24) };
    }
}
