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
