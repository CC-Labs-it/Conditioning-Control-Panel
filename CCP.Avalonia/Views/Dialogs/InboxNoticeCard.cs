using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

/// <summary>WPF InboxNoticeCard: one nonmodal passive card, with an optional primary action.</summary>
public sealed class InboxNoticeCard : Window, IPassiveStartupSurface
{
    private static InboxNoticeCard? _open;

    private InboxNoticeCard()
    {
        Title = "CC Labs";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !OnClickable(e.Source as Visual))
                try { BeginMoveDrag(e); } catch (InvalidOperationException) { }
        };
    }

    public static InboxNoticeCard Present(Window? owner, string heading, string body, string? primary, Action? onPrimary,
        string? secondary = null, Action? onSecondary = null)
    {
        var card = new InboxNoticeCard();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = heading, Foreground = Brushes.White, FontSize = 17,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(body)) stack.Children.Add(new TextBlock { Text = body,
            Foreground = Brush.Parse("#E0E0E0"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        if (!string.IsNullOrEmpty(primary))
        {
            var button = new Button { Content = primary, Cursor = CursorHand, FontSize = 14,
                Padding = new Thickness(14, 9), Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            if (Application.Current?.TryFindResource("SmallPinkButton", out var theme) == true && theme is ControlTheme t) button.Theme = t;
            button.Click += (_, _) => { card.Close(); Run(onPrimary); };
            stack.Children.Add(button);
        }
        var quiet = new TextBlock { Text = string.IsNullOrEmpty(secondary) ? Loc.Get("vaultgate_close") : secondary,
            Foreground = Brush.Parse("#B8B1CC"), FontSize = 12, Cursor = CursorHand,
            TextDecorations = TextDecorations.Underline, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        quiet.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) { card.Close(); Run(onSecondary); } };
        stack.Children.Add(quiet);
        card.Content = new Border { Background = Brush.Parse("#FF151528"), CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1.5), BorderBrush = Brush.Parse("#FF69B4"), Margin = new Thickness(12),
            Padding = new Thickness(22, 18, 22, 16), Child = stack };
        try { _open?.Close(); } catch (InvalidOperationException) { }
        _open = card;
        card.Closed += (_, _) => { if (ReferenceEquals(_open, card)) _open = null; };
        try { if (owner is { IsVisible: true }) card.Show(owner); else card.Show(); }
        catch (InvalidOperationException) { card.Show(); }
        return card;
    }

    private static void Run(Action? action)
    {
        try { action?.Invoke(); }
        catch (Exception ex) { Log.Warning(ex, "[Inbox] notice card action failed"); }
    }

    private static bool OnClickable(Visual? visual)
    {
        for (; visual != null; visual = visual.GetVisualParent())
            if (visual is Button || visual is InputElement { Cursor: { } cursor } && cursor == CursorHand) return true;
        return false;
    }
    private static readonly Cursor CursorHand = new(StandardCursorType.Hand);
}
