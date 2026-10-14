using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

internal enum MediaDropChoice { Cancel, Play, Edit, Library }

/// <summary>WPF MainWindow.Presets.cs ShowMediaDropChoiceDialog: one local media file,
/// three explicit actions. Closing the window or pressing Escape imports and plays nothing.</summary>
internal sealed class MediaDropDialog : Window
{
    internal MediaDropChoice Choice { get; private set; }

    internal MediaDropDialog() : this("sample.mp4") { }

    public MediaDropDialog(string path)
    {
        NameScope.SetNameScope(this, new NameScope());
        Title = Loc.Get("dlg_media_drop_title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MinHeight = 240;
        MaxHeight = 600;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("DarkerBgBrush");
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Title, Foreground = Brush("PinkBrush"), FontSize = 18, FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(path), Foreground = Brushes.White, FontSize = 12, Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 16),
        });
        AddButton(stack, "BtnPlay", "dlg_media_drop_play", MediaDropChoice.Play);
        AddButton(stack, "BtnEdit", "dlg_media_drop_edit", MediaDropChoice.Edit);
        AddButton(stack, "BtnOk", "dlg_media_drop_library", MediaDropChoice.Library);
        AddButton(stack, "BtnCancel", "dlg_media_drop_cancel", MediaDropChoice.Cancel);
        Content = new Border
        {
            BorderBrush = Brush("PinkBrush"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20), Child = stack,
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    private void AddButton(StackPanel stack, string name, string key, MediaDropChoice choice)
    {
        var cancel = choice == MediaDropChoice.Cancel;
        var button = new Button
        {
            Name = name, Content = new TextBlock { Text = Loc.Get(key) },
            Background = cancel ? Brushes.Transparent : Brush("PinkBrush"), Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Padding = cancel ? new Thickness(8, 6) : new Thickness(20, 12),
            Margin = cancel ? new Thickness(0, 8, 0, 0) : new Thickness(0, 0, 0, 8),
            FontSize = cancel ? 11 : 13, FontWeight = cancel ? FontWeight.Normal : FontWeight.SemiBold,
            Opacity = cancel ? 0.7 : 1, HorizontalAlignment = cancel ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
        };
        NameScope.GetNameScope(this)!.Register(name, button);
        button.Click += (_, _) => { Choice = choice; Close(); };
        stack.Children.Add(button);
    }

    private IBrush Brush(string key) => this.TryFindResource(key, out var value) && value is IBrush brush ? brush : Brushes.Transparent;
}
