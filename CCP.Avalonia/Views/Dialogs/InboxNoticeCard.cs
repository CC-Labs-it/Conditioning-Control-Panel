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

/// <summary>Neutral conversion card, adapted from WPF a54ce18f5. One card at a time.</summary>
internal sealed class InboxNoticeCard : Window, IPassiveStartupSurface
{
    internal static InboxNoticeCard? Open { get; private set; }

    /// <summary>Render constructor (RenderProof --render-all): the one-day Prime lapse card.</summary>
    internal InboxNoticeCard() : this(Loc.Get("conv_lapse_warn_prime_one"), Loc.Get("conv_lapse_warn_body"),
        Loc.Get("conv_keep_prime"), null, Loc.Get("conv_let_close"), null) { }

    private InboxNoticeCard(string heading, string body, string? primary, Action? onPrimary,
        string? secondary, Action? onSecondary)
    {
        Title = "CC Labs";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            for (var v = e.Source as Visual; v != null; v = v.GetVisualParent())
                if (v is Button) return;
            BeginMoveDrag(e);
        };
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = heading, Foreground = Brushes.White, FontSize = 17,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(body))
            stack.Children.Add(new TextBlock
            {
                Text = body, Foreground = Brush.Parse("#E0E0E0"), FontSize = 13, TextWrapping = TextWrapping.Wrap,
            });
        if (!string.IsNullOrEmpty(primary))
        {
            // WPF InboxNoticeCard: Style=SmallPinkButton.
            var button = new Button
            {
                Theme = this.TryFindResource("SmallPinkButton", out var t) ? t as ControlTheme : null,
                Content = primary, FontSize = 14, Padding = new Thickness(14, 9),
                Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) => { Close(); Run(onPrimary, "primary"); };
            stack.Children.Add(button);
        }
        var quiet = new Button
        {
            Content = string.IsNullOrEmpty(secondary) ? Loc.Get("vaultgate_close") : secondary,
            Foreground = Brush.Parse("#B8B1CC"), Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12,
        };
        quiet.Click += (_, _) => { Close(); Run(onSecondary, "secondary"); };
        stack.Children.Add(quiet);
        Content = new Border
        {
            Background = Brush.Parse("#151528"), BorderBrush = this.TryFindResource("PinkBrush", out var pink) && pink is IBrush pb ? pb : Brush.Parse("#FF69B4"),
            BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22, 18, 22, 16), Child = stack,
        };
    }

    internal static void Present(Window? owner, string heading, string body, string? primary,
        Action? onPrimary, string? secondary = null, Action? onSecondary = null)
    {
        CloseActive();
        var card = new InboxNoticeCard(heading, body, primary, onPrimary, secondary, onSecondary);
        Open = card;
        card.Closed += (_, _) => { if (ReferenceEquals(Open, card)) Open = null; };
        if (owner is { IsVisible: true }) card.Show(owner); else card.Show();
    }

    internal static void CloseActive() => Open?.Close();

    private static void Run(Action? action, string which)
    {
        try { action?.Invoke(); }
        catch (Exception ex) { Log.Warning(ex, "[Inbox] notice card {Which} action failed", which); }
    }
}
