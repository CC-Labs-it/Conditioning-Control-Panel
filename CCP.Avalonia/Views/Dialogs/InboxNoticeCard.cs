using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

// WPF Dialogs/InboxNoticeCard.cs:24-133: one neutral, modeless card, replaced on the next open.
internal sealed class InboxNoticeCard : Window, IPassiveStartupSurface
{
    internal static InboxNoticeCard? Open { get; private set; }

    private InboxNoticeCard(string heading, string body, string primary, Action onPrimary)
    {
        Title = "CC Labs";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = heading, Foreground = Brushes.White, FontSize = 17,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = body, Foreground = Brush.Parse("#E0E0E0"), FontSize = 13,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });
        var button = new Button
        {
            Name = "Primary", Content = primary, FontSize = 14,
            Padding = new Thickness(14, 9), Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        button[!ThemeProperty] = new DynamicResourceExtension("SmallPinkButton"); // WPF InboxNoticeCard.cs:85
        button.Click += (_, _) =>
        {
            Close();
            try { onPrimary(); }
            catch (Exception ex) { Log.Warning(ex, "Inbox notice primary action failed"); }
        };
        stack.Children.Add(button);
        var quiet = new Button
        {
            Name = "Quiet", FontSize = 12, Cursor = new Cursor(StandardCursorType.Hand),
            Content = new TextBlock { Text = Loc.Get("vaultgate_close"), TextDecorations = TextDecorations.Underline },
            Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Foreground = Brush.Parse("#B8B1CC"), Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
        };
        quiet.Click += (_, _) => Close();
        stack.Children.Add(quiet);
        var card = new Border
        {
            Background = Brush.Parse("#151528"), CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(12), Padding = new Thickness(22, 18, 22, 16), Child = stack,
        };
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("PinkBrush"); // WPF InboxNoticeCard.cs:104
        // Button input must not become a drag (WPF InboxNoticeCard.cs:38-43,136-145).
        card.PointerPressed += (_, e) =>
        {
            if (!e.Handled && e.Source is Visual source
                && source.GetSelfAndVisualAncestors().OfType<Button>().Any() == false
                && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        Content = card;
    }

    internal static void Present(Window? owner, string heading, string body, string primary, Action onPrimary)
    {
        var card = new InboxNoticeCard(heading, body, primary, onPrimary);
        Open?.Close();
        Open = card;
        card.Closed += (_, _) => { if (ReferenceEquals(Open, card)) Open = null; };
        if (owner is { IsVisible: true }) card.Show(owner); else card.Show();
    }
}
