using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Automation;
using Avalonia.Input;
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

    public static string ToneIconName(DareTone tone) => tone switch
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

    // WPF c2cf10de4d DareLook.cs:52-69 MDL2 glyphs, as Fluent kinds (IconMap D5 where it has the codepoint).
    public static IconKind ToneIcon(DareTone tone) => tone switch
    {
        DareTone.Pics => IconKind.Image, DareTone.Minutes => IconKind.Clock, DareTone.Video => IconKind.Video,
        DareTone.Session => IconKind.Play, DareTone.Bubbles => IconKind.BubbleMultiple, DareTone.Dare => IconKind.Mail,
        DareTone.Sp => IconKind.Star, DareTone.Lock => IconKind.Key, DareTone.Lockdown => IconKind.Shield,
        DareTone.When => IconKind.Calendar, DareTone.CameraOn => IconKind.Eye, DareTone.CameraOff => IconKind.EyeOff,
        DareTone.Cant => IconKind.Dismiss, DareTone.Duel => IconKind.ArrowSwap, _ => IconKind.Person,
    };

    // WPF 8d480c7d3f Controls/Dares/DareLook.cs:774-799.
    public static Button Choice(string label, Color color, string? tip, Action onClick, string tag)
    {
        // Retain the original lane API; WPF's later light-card design uses a shared edge color.
        _ = color;
        var tile = new SolidColorBrush(Color.FromRgb(0xF7, 0xEE, 0xF8));
        var face = new Border
        {
            Background = tile,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xC6, 0xDD)),
            BorderThickness = new Thickness(1, 1, 1, 4),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14, 6, 14, 5),
            Child = new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily("Arial Narrow, Segoe UI"),
                FontSize = 19,
                FontWeight = FontWeight.Bold,
                FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") },
                Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x18, 0x3A)),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            },
        };
        var shadow = new Border
        {
            Background = tile,
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(8, 6, 8, 1),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 7, Color = Color.FromArgb(102, 0, 0, 0) }),
            IsHitTestVisible = false,
        };
        var button = new Button
        {
            Content = new Grid { Children = { shadow, face } },
            Template = new FuncControlTemplate<Button>((b, _) => new ContentPresenter
            {
                [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
            }),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = false,
            Tag = tag,
        };
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, tip);
        button.PointerEntered += (_, _) => face.Background = Brushes.White;
        button.PointerExited += (_, _) => face.Background = tile;
        button.Click += (_, _) => onClick();
        return button;
    }
}
