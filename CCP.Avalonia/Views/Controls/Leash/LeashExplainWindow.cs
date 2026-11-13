// PORTED from WPF ConditioningControlPanel/Controls/Leash/Explain/LeashExplainer.cs Show/Build/Present
// (the "?" door: title, close X, the four-step card in a row, "Got it") and LeashLook.Help (the 22px
// lilac "?" pill). Leashed side only: the holder variant and BeforeOffer are LeashHolderIntro.cs (friend card offer chip).
// Flat on this head (P82): the WPF DropShadowEffect is a BoxShadow; the drawn step art is the
// ask card's Fluent glyphs (LeashAskCard.Explainer).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

/// <summary>"How the leash works": a borderless window over its owner. Escape, X and "Got it" close it.</summary>
public sealed class LeashExplainWindow : Window
{
    private static readonly IBrush Lilac22 = new SolidColorBrush(Color.FromArgb(0x22, 0xB9, 0x9C, 0xFF));
    private static readonly IBrush Lilac66 = new SolidColorBrush(Color.FromArgb(0x66, 0xB9, 0x9C, 0xFF));
    private static readonly IBrush ButtonBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33));

    internal LeashExplainWindow()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = Loc.Get("leash_explain_title");
        Tag = "leash-explainer";

        var head = new Grid { Margin = new Thickness(4, 0, 0, 12), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(new TextBlock
        {
            Text = Loc.Get("leash_explain_title"), FontFamily = F.Display, FontWeight = FontWeight.SemiBold, FontSize = 20,
            Foreground = F.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var x = F.Pill(new IconGlyph { Kind = IconKind.Dismiss, Size = 14, Foreground = F.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            ButtonBg, F.Muted, "leash-explain-close", F.Line);
        x.Width = x.Height = 26;
        x.Padding = new Thickness(0);
        x.CornerRadius = new CornerRadius(13);
        x.HorizontalContentAlignment = HorizontalAlignment.Center;
        x.Click += (_, _) => Close();
        Grid.SetColumn(x, 1);
        head.Children.Add(x);

        var ok = F.Pill(new TextBlock { Text = Loc.Get("leash_explain_got_it"), FontFamily = F.Display, FontWeight = FontWeight.SemiBold, FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, Foreground = F.MintInk }, F.Mint, F.MintInk, "leash-explain-got-it", F.Mint);
        ok.MinWidth = 96;
        ok.Padding = new Thickness(18, 8, 18, 8);
        ok.CornerRadius = new CornerRadius(12);
        ok.HorizontalContentAlignment = HorizontalAlignment.Center;
        ok.Click += (_, _) => Close();
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok } };

        var card = LeashAskCard.Explainer(null, row: true);
        card.Width = 600;
        card.Margin = new Thickness(0);

        Content = new Border
        {
            Child = new StackPanel { Children = { head, card, foot } },
            Padding = new Thickness(18, 16, 18, 16),
            Margin = new Thickness(18),
            CornerRadius = new CornerRadius(20),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0x22, 0x15, 0x3E), 0), new GradientStop(Color.FromRgb(0x10, 0x0A, 0x1E), 1) },
            },
            BorderBrush = F.Line2,
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 24, OffsetY = 4, Color = Color.FromArgb(0x8C, 0, 0, 0) }),
        };
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) try { BeginMoveDrag(e); } catch { }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    /// <summary>WPF LeashExplainer.Show + Present (Open: Window already owns Show): over the owner when it is up, else centred on screen.</summary>
    internal static LeashExplainWindow? Open(Window? owner)
    {
        try
        {
            var w = new LeashExplainWindow();
            if (owner is { IsVisible: true }) w.Show(owner);
            else { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Show(); }
            w.Activate();
            return w;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("[Leash] explainer failed to open: {E}", ex.Message);
            return null;
        }
    }

    /// <summary>WPF LeashLook.Help: the 22px lilac "?" pill. <paramref name="role"/> names the surface in its Tag.</summary>
    internal static Button HelpButton(string role, Func<Window?> owner)
    {
        var b = F.Pill(new TextBlock
        {
            Text = "?", FontFamily = F.Display, FontWeight = FontWeight.Bold, FontSize = 12, Foreground = F.Lilac,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        }, Lilac22, F.Lilac, "leash-help:" + role, Lilac66);
        b.Width = b.Height = 22;
        b.Padding = new Thickness(0);
        b.CornerRadius = new CornerRadius(11);
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.VerticalContentAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(b, Loc.Get("leash_help"));
        b.Click += (_, e) => { e.Handled = true; Open(owner()); };
        return b;
    }
}
