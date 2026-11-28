using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Controls.Dares;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Dares;

// WPF c2cf10de4d Controls/Dares/DareLook.cs:31-73, 307-318.
internal static class DareLook
{
    // WPF 6247691138 Controls/Dares/DareLook.cs:165-201.
    public static Color ToneColor(DareTone tone) => tone switch
    {
        DareTone.Cant or DareTone.Dare => Color.FromRgb(0xC2, 0x2B, 0x2B),
        DareTone.Duel or DareTone.CameraOn => Color.FromRgb(0x2C, 0x8A, 0x57),
        DareTone.Pics => Color.FromRgb(0xE0, 0x66, 0x3A),
        DareTone.Minutes => Color.FromRgb(0x1F, 0x8A, 0x8A),
        DareTone.Video => Color.FromRgb(0xCC, 0x3D, 0x7A),
        DareTone.Session => Color.FromRgb(0x8A, 0x3F, 0x8F),
        DareTone.Bubbles => Color.FromRgb(0x2F, 0x86, 0xC9),
        DareTone.Sp => Color.FromRgb(0xB0, 0x7A, 0x00),
        DareTone.Lock => Color.FromRgb(0x33, 0x61, 0x8F),
        DareTone.CameraOff => Color.FromRgb(0x7A, 0x6A, 0x8F),
        _ => Color.FromRgb(0x3A, 0x1D, 0x78),
    };

    public static string ToneIcon(DareTone tone) => tone switch
    {
        DareTone.Pics => "picture",
        DareTone.Minutes or DareTone.When => "clock",
        DareTone.Video => "video",
        DareTone.Session => "play",
        DareTone.Bubbles => "bubble",
        DareTone.Sp => "star",
        DareTone.Lock => "lock",
        DareTone.Lockdown => "ldn",
        DareTone.CameraOn => "eye",
        DareTone.CameraOff => "eyeoff",
        DareTone.Cant => "cross",
        DareTone.Duel => "versus",
        _ => "envelope",
    };

    public static string ToneGlyph(DareTone tone) => tone switch
    {
        DareTone.Pics => "\uE8B9", DareTone.Minutes => "\uE823", DareTone.Video => "\uE714",
        DareTone.Session => "\uE768", DareTone.Bubbles => "\uEA3A", DareTone.Dare => "\uE715",
        DareTone.Sp => "\uE734", DareTone.Lock => "\uE72E", DareTone.Lockdown => "\uEA18",
        DareTone.When => "\uE787", DareTone.CameraOn => "\uE7B3", DareTone.CameraOff => "\uED1A",
        DareTone.Cant => "\uE711", DareTone.Duel => "\uE8AB", _ => "\uE77B",
    };

    // WPF 8d480c7d3f Controls/Dares/DareLook.cs:774-790: choices have a flat base edge.
    public static Button Choice(string label, Color color, string? tip, Action onClick, string tag)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xEA, 0xFF)) },
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1, 1, 1, 4),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14, 6, 14, 5),
            Margin = new Thickness(0, 0, 8, 8),
            Tag = tag,
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }

    // Card shell for the offer and debt cards. shortcut: plain print and flat desk, not WPF's slip
    // paper, fonts and motion (Dialogs/Dares/*); upgrade when the slip-window lane lands its look.
    public static readonly IBrush ChalkBrush = new SolidColorBrush(Color.FromRgb(0xF1, 0xEA, 0xFF));
    public static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xB1, 0xCC));
    public static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0x8A));

    // WPF Controls/Dares/DareLook.cs:1197-1229: borderless, sized to content, dragged by its desk.
    public static StackPanel Shell(Window w, double width, string tag)
    {
        w.Title = "CC Labs";
        w.Width = width;
        w.SizeToContent = SizeToContent.Height;
        w.WindowDecorations = WindowDecorations.None;
        w.Background = Brushes.Transparent;
        w.ShowInTaskbar = false;
        w.CanResize = false;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.PointerPressed += (_, e) =>
        {
            if (e.Source is not Button && e.GetCurrentPoint(w).Properties.IsLeftButtonPressed)
                try { w.BeginMoveDrag(e); } catch (InvalidOperationException) { }
        };
        var body = new StackPanel();
        w.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x29, 0x6B)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
            Margin = new Thickness(14), Padding = new Thickness(22, 20), Tag = tag, Child = body,
        };
        return body;
    }

    public static TextBlock Words(string text, double size, IBrush? fg = null, string? tag = null) => new()
    {
        Text = text, FontSize = size, Foreground = fg ?? ChalkBrush, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0), Tag = tag,
    };

    /// <summary>WPF DareLook.Big (the go button) and Quiet (a word with an optional small line under it).</summary>
    public static Button Act(string label, string? sub, bool big, Action onClick, string tag)
    {
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = label, FontSize = big ? 18 : 15, FontWeight = big ? FontWeight.Bold : FontWeight.Normal });
        if (sub != null) words.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = MutedBrush });
        var button = new Button
        {
            Content = words, Tag = tag, Cursor = new Cursor(StandardCursorType.Hand),
            Foreground = big ? Brushes.White : ChalkBrush,
            Background = big ? new SolidColorBrush(ToneColor(DareTone.Duel)) : Brushes.Transparent,
            Padding = new Thickness(16, 8), Margin = new Thickness(0, 0, 10, 8), VerticalAlignment = VerticalAlignment.Top,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>WPF DareLook.Stamp: the result word set at a slant over the slip.</summary>
    public static Border Stamp(string word, bool grey) => new()
    {
        BorderBrush = grey ? MutedBrush : WarnBrush, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12, 2), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
        RenderTransform = new RotateTransform(-6), Tag = "dares-stamp",
        Child = new TextBlock { Text = word.ToUpperInvariant(), FontSize = 22, FontWeight = FontWeight.Black, Foreground = grey ? MutedBrush : WarnBrush },
    };
}
