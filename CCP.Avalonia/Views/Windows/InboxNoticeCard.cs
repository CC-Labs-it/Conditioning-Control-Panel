// Adapted from WPF Dialogs/InboxNoticeCard.cs: neutral, single-instance passive notice.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public sealed class InboxNoticeCard : Window, Services.Startup.IPassiveStartupSurface
{
    internal static InboxNoticeCard? Current { get; private set; }
    internal static void CloseForPanic() => Current?.Close();

    /// <summary>Render constructor (--render-all): both buttons and a body, the card's full layout.</summary>
    internal InboxNoticeCard() : this("Prime is on", "Everything Prime unlocks is open again.", "Open", null, "Later", null) { }

    private InboxNoticeCard(string heading, string body, string? primary, Action? onPrimary,
        string? secondary, Action? onSecondary)
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
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            for (var v = e.Source as Visual; v != null && v != this; v = v.GetVisualParent())
                if (v is Button) return;
            try { BeginMoveDrag(e); }
            catch (InvalidOperationException) { } // the pointer was already released
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Name = "Heading", Text = heading, Foreground = Brushes.White, FontSize = 17,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(body)) stack.Children.Add(new TextBlock
        {
            Name = "Body", Text = body, Foreground = Brush.Parse("#E0E0E0"), FontSize = 13,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });
        if (!string.IsNullOrEmpty(primary))
        {
            var button = new Button
            {
                Name = "Primary", Content = primary, Cursor = new Cursor(StandardCursorType.Hand), FontSize = 14,
                Padding = new Thickness(14, 9), Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            if (Application.Current?.TryFindResource("SmallPinkButton", out var theme) == true && theme is ControlTheme t)
                button.Theme = t;
            button.Click += (_, _) => { Close(); Run(onPrimary, "primary"); };
            stack.Children.Add(button);
        }
        // A real button gives the WPF quiet choice keyboard access too.
        var quiet = new Button
        {
            Name = "Secondary", Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Content = new TextBlock
            {
                Text = string.IsNullOrEmpty(secondary) ? Loc.Get("vaultgate_close") : secondary,
                Foreground = Brush.Parse("#B8B1CC"), FontSize = 12, TextDecorations = TextDecorations.Underline,
            },
            Cursor = new Cursor(StandardCursorType.Hand), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };
        quiet.Click += (_, _) => { Close(); Run(onSecondary, "secondary"); };
        stack.Children.Add(quiet);
        Content = new Border
        {
            Background = Brush.Parse("#151528"), CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Application.Current?.TryFindResource("PinkBrush", out var pink) == true && pink is IBrush brush
                ? brush : Brush.Parse("#FF69B4"),
            Margin = new Thickness(12), Padding = new Thickness(22, 18, 22, 16), Child = stack,
        };
    }

    public static void Present(Window? owner, string heading, string body, string? primary, Action? onPrimary,
        string? secondary = null, Action? onSecondary = null)
    {
        var card = new InboxNoticeCard(heading, body, primary, onPrimary, secondary, onSecondary);
        Current?.Close();
        Current = card;
        card.Closed += (_, _) => { if (ReferenceEquals(Current, card)) Current = null; };
        if (owner is { IsVisible: true })
        {
            try { card.Show(owner); return; }
            catch (InvalidOperationException) { } // owner closing: stand alone as WPF does
        }
        card.Show();
    }

    private static void Run(Action? action, string which)
    {
        try { action?.Invoke(); }
        catch (Exception ex) { Log.Warning(ex, "[Inbox] notice card {Which} action failed", which); }
    }
}
