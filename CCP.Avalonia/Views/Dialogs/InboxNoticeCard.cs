using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

/// <summary>WPF InboxNoticeCard: one neutral, passive card with an optional action.</summary>
public sealed class InboxNoticeCard : Window, IPassiveStartupSurface
{
    internal static InboxNoticeCard? Open { get; private set; }

    internal InboxNoticeCard() : this(Loc.GetF("conv_lapse_warn_basic", 3), Loc.Get("conv_lapse_warn_body"),
        Loc.Get("conv_keep_basic"), null, Loc.Get("conv_let_close"), null) { }

    internal InboxNoticeCard(string heading, string body, string? primary, Action? onPrimary, string? secondary, Action? onSecondary)
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
        PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = heading, Foreground = Brushes.White, FontSize = 17,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(body)) stack.Children.Add(new TextBlock { Text = body,
            Foreground = Brush.Parse("#E0E0E0"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        if (!string.IsNullOrEmpty(primary))
        {
            var action = new Button { Name = "Primary", Content = new TextBlock { Text = primary }, FontSize = 14, Padding = new Thickness(14, 9),
                Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            if (Application.Current?.TryFindResource("SmallPinkButton", out var theme) == true && theme is ControlTheme pink) action.Theme = pink;
            action.Click += (_, _) => { Close(); Run(onPrimary); };
            stack.Children.Add(action);
        }
        var quiet = new Button { Name = "Secondary", Content = new TextBlock { Text = string.IsNullOrEmpty(secondary) ? Loc.Get("vaultgate_close") : secondary,
                TextDecorations = TextDecorations.Underline }, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            FontSize = 12, Foreground = Brush.Parse("#B8B1CC"), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0) };
        quiet.Click += (_, _) => { Close(); Run(onSecondary); };
        stack.Children.Add(quiet);
        Content = new Border { Background = Brush.Parse("#FF151528"), BorderBrush = Brush.Parse("#FF69B4"),
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5), Margin = new Thickness(12),
            Padding = new Thickness(22, 18, 22, 16), Child = stack };
    }

    internal static void Present(Window? owner, string heading, string body, string? primary, Action? onPrimary,
        string? secondary = null, Action? onSecondary = null)
    {
        var card = new InboxNoticeCard(heading, body, primary, onPrimary, secondary, onSecondary);
        Open?.Close();
        Open = card;
        card.Closed += (_, _) => { if (Open == card) Open = null; };
        if (owner is { IsVisible: true }) card.Show(owner); else card.Show();
    }

    internal static void CloseForPanic() => Open?.Close();
    private static void Run(Action? action)
    {
        try { action?.Invoke(); }
        catch (Exception ex) { Log.Warning(ex, "Inbox notice action failed"); }
    }
}
