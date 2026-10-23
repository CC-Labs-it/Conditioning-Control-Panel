using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
        Background = new SolidColorBrush(Color.Parse("#1A1A2E"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = Path.GetFileName(path), Opacity = .75,
            TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextAlignment = TextAlignment.Center });
        foreach (var (choice, key) in new[] {
            (MediaDropChoice.Play, "dlg_media_drop_play"), (MediaDropChoice.Edit, "dlg_media_drop_edit"),
            (MediaDropChoice.Library, "dlg_media_drop_library"), (MediaDropChoice.Cancel, "dlg_media_drop_cancel") })
        {
            var button = new Button { Name = "Btn" + choice, Content = Loc.Get(key),
                HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(20, 12) };
            button.Click += (_, _) => Close(choice);
            stack.Children.Add(button);
        }
        KeyDown += (_, e) => { if (e.Key == global::Avalonia.Input.Key.Escape) { e.Handled = true; Close(); } };
        Content = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), BorderBrush = Brushes.HotPink, Padding = new Thickness(20), Child = stack };
        Opened += (_, _) => Open.Add(this);
        Closed += (_, _) => Open.Remove(this);
    }
    internal static void CloseForPanic() { foreach (var window in new List<MediaDropDialog>(Open)) window.Close(); }
}
