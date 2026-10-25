// PORTED from WPF ConditioningControlPanel/Controls/Leash/LeashCutConfirmWindow.cs.
// "Cut the leash?" after the panic key was held five seconds. Topmost, centred on the primary screen,
// two buttons. Keys do nothing here on purpose (tunnel KeyDown handled): the finger that held the key
// is often still on it, and its repeats must not answer the question. Only Yes runs the cut.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashCutConfirmWindow : Window
{
    private static LeashCutConfirmWindow? _open;
    internal static LeashCutConfirmWindow? Current => _open;
    internal Button YesButton { get; }
    internal Button NoButton { get; }

    public static void Ask(string? holderName, Action onYes) => Show(
        Loc.Get("leash_cut_confirm_title"),
        string.IsNullOrWhiteSpace(holderName) ? Loc.Get("leash_cut_confirm_body_anon") : Loc.GetF("leash_cut_confirm_body", holderName!.Trim()),
        Loc.Get("leash_cut_confirm_yes"), Loc.Get("leash_cut_confirm_no"), "leash-cut-confirm", onYes);

    private static void Show(string title, string body, string yes, string no, string tag, Action onYes)
    {
        if (_open != null) { try { _open.Activate(); } catch { } return; }
        var w = new LeashCutConfirmWindow(title, body, yes, no, tag, onYes);
        _open = w;
        w.Closed += (_, _) => { if (_open == w) _open = null; };
        w.Show();
        w.Activate();
    }

    /// <summary>The render-all sample.</summary>
    private LeashCutConfirmWindow() : this(Loc.Get("leash_cut_confirm_title"), Loc.GetF("leash_cut_confirm_body", "Vex"),
        Loc.Get("leash_cut_confirm_yes"), Loc.Get("leash_cut_confirm_no"), "leash-cut-confirm", () => { }) { }

    internal LeashCutConfirmWindow(string titleText, string bodyText, string yesText, string noText, string tag, Action onYes)
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = titleText;
        AddHandler(KeyDownEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);

        var title = F.Label(titleText, 22, F.Text, F.Display, FontWeight.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var body = F.Label(bodyText, 13, F.Muted);
        (body.TextWrapping, body.TextTrimming, body.TextAlignment) = (TextWrapping.Wrap, TextTrimming.None, TextAlignment.Center);
        body.Margin = new Thickness(0, 8, 0, 16);

        NoButton = Pill(noText, new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33)), F.Text, F.Line2, tag + ":no", new SolidColorBrush(Color.FromRgb(0x3A, 0x1F, 0x66)));
        NoButton.Click += (_, _) => Close();
        YesButton = Pill(yesText, F.Red, new SolidColorBrush(Color.FromRgb(0x0B, 0x07, 0x16)), F.Red, tag + ":yes", F.Pink);
        YesButton.Margin = new Thickness(10, 0, 0, 0);
        YesButton.Click += (_, _) =>
        {
            Close();
            try { onYes(); } catch (Exception ex) { Serilog.Log.Debug("Leash confirm action failed: {E}", ex.Message); }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { NoButton, YesButton } };

        Content = new Border
        {
            Background = LeashHoldRing.Glass(), BorderBrush = F.Red, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16), Margin = new Thickness(24),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 28, Color = Color.FromArgb(0x99, 0, 0, 0) }),
            Child = new StackPanel { Margin = new Thickness(26, 22, 26, 20), MaxWidth = 380, Children = { title, body, row } },
        };
    }

    private static Button Pill(string text, IBrush bg, IBrush fg, IBrush border, string tag, IBrush hover)
    {
        var b = F.Pill(text, bg, fg, tag, border);
        (b.Padding, b.CornerRadius, b.Focusable) = (new Thickness(18, 7, 18, 7), new CornerRadius(10), false);
        if (b.Content is TextBlock t) t.FontSize = 13;
        // Fluent's template otherwise paints its generic hover over the WPF pill colors.
        b.Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, hover),
                new Setter(ContentPresenter.BorderBrushProperty, border),
                new Setter(ContentPresenter.ForegroundProperty, fg) },
        });
        b.PointerEntered += (_, _) => { if (b.IsEnabled) b.Background = hover; };
        b.PointerExited += (_, _) => b.Background = bg;
        return b;
    }
}
