using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

/// <summary>WPF Dialogs/InboxNoticeCard.cs:24-105: a passive, one-at-a-time conversion notice.</summary>
internal sealed class InboxNoticeCard : Window, IPassiveStartupSurface
{
    private static InboxNoticeCard? _open;
    internal TextBlock Heading { get; }
    internal Button Primary { get; }
    internal Button Quiet { get; }

    private InboxNoticeCard(string heading, string body, string primary, string? secondary, Action action)
    {
        Title = "CC Labs"; Width = 400; SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None; TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent; ShowInTaskbar = false; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel();
        Heading = new TextBlock { Text = heading, Foreground = Brushes.White, FontSize = 17,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(Heading);
        if (!string.IsNullOrWhiteSpace(body)) stack.Children.Add(new TextBlock { Text = body,
            Foreground = Brush.Parse("#E0E0E0"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        Primary = new Button { Content = new TextBlock { Text = primary }, Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 14, Padding = new Thickness(14, 9), Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (Application.Current?.TryFindResource("SmallPinkButton", out var theme) == true && theme is ControlTheme buttonTheme) Primary.Theme = buttonTheme;
        Primary.Click += (_, _) => { Close(); try { action(); } catch (Exception ex) { Log.Warning(ex, "[Inbox] notice action failed"); } };
        stack.Children.Add(Primary);
        Quiet = new Button { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
            Theme = new ControlTheme(typeof(Button)) { Setters = { new Setter(TemplatedControl.TemplateProperty,
                new FuncControlTemplate<Button>((b, _) => new ContentPresenter { Background = Brushes.Transparent,
                    [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty] })) } },
            Cursor = new Cursor(StandardCursorType.Hand), Content = new TextBlock { Text = string.IsNullOrEmpty(secondary) ? Loc.Get("vaultgate_close") : secondary,
                Foreground = Brush.Parse("#B8B1CC"), FontSize = 12, TextDecorations = TextDecorations.Underline } };
        Quiet.Click += (_, _) => Close();
        stack.Children.Add(Quiet);
        Content = new Border { Background = Brush.Parse("#FF151528"), CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1.5), BorderBrush = Brush.Parse("#FF69B4"), Margin = new Thickness(12),
            Padding = new Thickness(22, 18, 22, 16), Child = stack };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }, RoutingStrategies.Tunnel);
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual v && (v is Button || v.GetVisualAncestors().Any(a => a is Button))) return;
            try { BeginMoveDrag(e); } catch (InvalidOperationException) { }
        };
    }

    internal static void Present(Window? owner, string heading, string body, string primary, string? secondary, Action action)
    {
        var card = new InboxNoticeCard(heading, body, primary, secondary, action);
        _open?.Close(); _open = card;
        card.Closed += (_, _) => { if (ReferenceEquals(_open, card)) _open = null; };
        if (owner is { IsVisible: true }) card.Show(owner); else card.Show();
    }
}
