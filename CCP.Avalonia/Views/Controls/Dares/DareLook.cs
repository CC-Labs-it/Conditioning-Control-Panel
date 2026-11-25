using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Controls.Dares;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Dares;

// WPF c2cf10de4d Controls/Dares/DareLook.cs:31-73, 307-318.
internal static class DareLook
{
    public static Color ToneColor(DareTone tone) => tone switch
    {
        DareTone.Cant => Color.FromRgb(0xFF, 0x5F, 0x7A),
        DareTone.Duel or DareTone.CameraOn => Color.FromRgb(0x5F, 0xFF, 0xD0),
        DareTone.Pics => Color.FromRgb(0xFF, 0x9A, 0x5C),
        DareTone.Minutes => Color.FromRgb(0x4F, 0xD8, 0xD0),
        DareTone.Video => Color.FromRgb(0xFF, 0x5F, 0xB4),
        DareTone.Session or DareTone.Who => Color.FromRgb(0xB9, 0x9C, 0xFF),
        DareTone.Bubbles => Color.FromRgb(0x6B, 0xB8, 0xFF),
        DareTone.Dare => Color.FromRgb(0xFF, 0x6F, 0x8E),
        DareTone.Sp => Color.FromRgb(0xFF, 0xCF, 0x6B),
        DareTone.Lock => Color.FromRgb(0x86, 0xA8, 0xFF),
        DareTone.Lockdown => Color.FromRgb(0x9C, 0x7B, 0xFF),
        DareTone.CameraOff => Color.FromRgb(0x6F, 0x62, 0x9A),
        _ => Color.FromRgb(0xA3, 0x95, 0xC4),
    };

    public static IconKind ToneIcon(DareTone tone) => tone switch
    {
        DareTone.Pics => IconKind.Image, DareTone.Minutes or DareTone.When => IconKind.Clock, DareTone.Video => IconKind.Video,
        DareTone.Session => IconKind.Play, DareTone.Bubbles => IconKind.BubbleMultiple, DareTone.Sp => IconKind.Star,
        DareTone.Lock => IconKind.LockClosed, DareTone.Lockdown => IconKind.ShieldLock, DareTone.CameraOn => IconKind.Eye,
        DareTone.CameraOff => IconKind.EyeOff, DareTone.Cant => IconKind.Dismiss, DareTone.Duel => IconKind.ArrowSwap, _ => IconKind.Mail,
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
}
