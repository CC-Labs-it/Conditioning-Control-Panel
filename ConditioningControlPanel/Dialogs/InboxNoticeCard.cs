using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel;

/// <summary>
/// The small card behind a conversion Inbox row (lapse warnings, the companion cap, the Daily
/// Gift relock, the free-member hello): one heading, one line, an optional primary button and a
/// quiet second choice. Neutral UI on purpose: no persona voice, no price, no art. Code-only like
/// <see cref="VaultGateDialog"/>, and one at a time.
/// </summary>
public sealed class InboxNoticeCard : Window, Services.Startup.IPassiveStartupSurface
{
    private static readonly Brush CardBg = Frozen("#FF151528");
    private static readonly Brush Text = Frozen("#E0E0E0");
    private static readonly Brush Muted = Frozen("#B8B1CC");

    private static InboxNoticeCard? _open;

    private InboxNoticeCard()
    {
        Title = "CC Labs";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // Anything clickable is skipped, or DragMove eats its button-up (AGENTS: draggable borderless).
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed || OnClickable(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch (InvalidOperationException) { } // swallow: the button was already up
        };
    }

    /// <summary>
    /// Shows the card. <paramref name="primary"/> null = a card with only the quiet choice.
    /// <paramref name="secondary"/> null = the plain close word.
    /// </summary>
    public static void Present(Window? owner, string heading, string body, string? primary, Action? onPrimary,
        string? secondary = null, Action? onSecondary = null)
    {
        var card = new InboxNoticeCard();
        try { if (owner is { IsVisible: true }) card.Owner = owner; }
        catch (InvalidOperationException) { } // swallow: owner closing; the card stands alone
        card.Build(heading, body, primary, onPrimary, secondary, onSecondary);
        try { _open?.Close(); }
        catch (InvalidOperationException) { } // swallow: the previous card was already closing
        _open = card;
        card.Closed += (_, _) => { if (ReferenceEquals(_open, card)) _open = null; };
        card.Show();
    }

    private void Build(string heading, string body, string? primary, Action? onPrimary, string? secondary, Action? onSecondary)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = heading, Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(body))
            stack.Children.Add(new TextBlock
            {
                Text = body, Foreground = Text, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });

        if (!string.IsNullOrEmpty(primary))
        {
            var button = new Button
            {
                Content = primary, Cursor = Cursors.Hand, FontSize = 14,
                Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            button.SetResourceReference(StyleProperty, "SmallPinkButton");
            button.Click += (_, _) => { Close(); Run(onPrimary, "primary"); };
            stack.Children.Add(button);
        }

        var quiet = new TextBlock
        {
            Text = string.IsNullOrEmpty(secondary) ? Loc.Get("vaultgate_close") : secondary,
            Foreground = Muted, FontSize = 12, Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
        };
        quiet.MouseLeftButtonUp += (_, _) => { Close(); Run(onSecondary, "secondary"); };
        stack.Children.Add(quiet);

        var card = new Border
        {
            Background = CardBg, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5),
            Margin = new Thickness(12), Padding = new Thickness(22, 18, 22, 16), Child = stack,
        };
        card.SetResourceReference(Border.BorderBrushProperty, "PinkBrush");
        Content = card;
    }

    private static void Run(Action? action, string which)
    {
        if (action == null) return;
        try { action(); }
        catch (Exception ex) { App.Logger?.Warning(ex, "[Inbox] notice card {Which} action failed", which); }
    }

    private static bool OnClickable(DependencyObject? d)
    {
        for (; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement fe && fe.Cursor == Cursors.Hand) return true;
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (d is Window) return false;
        }
        return false;
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
