using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

internal enum MediaDropChoice { Cancel, Play, Edit, Library }

/// <summary>WPF ShowMediaDropChoiceDialog: same copy, order and dismiss-as-cancel behavior.</summary>
internal sealed class MediaDropDialog : Window
{
    private static readonly HashSet<MediaDropDialog> Open = new();
    internal MediaDropDialog() : this("media.mp4") { } // render-all sample
    internal MediaDropDialog(string path)
    {
        Title = Loc.Get("dlg_media_drop_title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        MinHeight = 240;
        // WPF Presets.cs:822-921: borderless, pink heading, pink buttons, small Cancel link.
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        IBrush Res(string key, IBrush fallback) =>
            Application.Current?.TryFindResource(key, out var v) == true && v is IBrush b ? b : fallback;
        var pink = Res("PinkBrush", Brushes.HotPink);
        var pinkTheme = Application.Current?.TryFindResource("PinkButton", out var t) == true ? t as ControlTheme : null;
        var stack = new StackPanel();
        var names = new NameScope();   // code-built window: register the button names so FindControl reaches them
        NameScope.SetNameScope(this, names);
        stack.Children.Add(new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = pink,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new TextBlock { Text = Path.GetFileName(path), Opacity = .75, FontSize = 12, Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 16) });
        foreach (var (choice, key) in new[] {
            (MediaDropChoice.Play, "dlg_media_drop_play"), (MediaDropChoice.Edit, "dlg_media_drop_edit"),
            (MediaDropChoice.Library, "dlg_media_drop_library"), (MediaDropChoice.Cancel, "dlg_media_drop_cancel") })
        {
            var link = choice == MediaDropChoice.Cancel;
            var button = new Button { Name = "Btn" + choice, Theme = pinkTheme, Content = new TextBlock { Text = Loc.Get(key) },
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = link ? 11 : 13, FontWeight = link ? FontWeight.Normal : FontWeight.SemiBold,
                Padding = link ? new Thickness(8, 6) : new Thickness(20, 12),
                Margin = link ? new Thickness(0, 8, 0, 0) : new Thickness(0, 0, 0, 8),
                HorizontalAlignment = link ? HorizontalAlignment.Center : HorizontalAlignment.Stretch };
            if (link) { button.Background = Brushes.Transparent; button.Opacity = .7; }
            names.Register(button.Name, button);
            button.Click += (_, _) => Close(choice);
            stack.Children.Add(button);
        }
        KeyDown += (_, e) => { if (e.Key == global::Avalonia.Input.Key.Escape) { e.Handled = true; Close(); } };
        Content = new Border { Background = Res("DarkerBgBrush", new SolidColorBrush(Color.Parse("#1A1A2E"))), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), BorderBrush = pink, Padding = new Thickness(20), Child = stack };
        Opened += (_, _) => Open.Add(this);
        Closed += (_, _) => Open.Remove(this);
    }
    internal static void CloseForPanic() { foreach (var window in new List<MediaDropDialog>(Open)) window.Close(); }
}
