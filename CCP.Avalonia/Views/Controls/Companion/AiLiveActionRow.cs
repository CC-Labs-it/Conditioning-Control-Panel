using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.Commands;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion;

/// <summary>WPF's timestamped workshop row, with the action's emoji mapped to a Fluent glyph.</summary>
public sealed class AiLiveActionRow : Grid
{
    private readonly TextBlock _time = new() { FontSize = 10.5, Margin = new Thickness(0, 0, 5, 0) };
    private readonly IconGlyph _icon = new() { Size = 10.5, Margin = new Thickness(0, 0, 5, 0) };
    private readonly TextBlock _text = new() { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };

    public AiLiveActionRow()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*");
        Margin = new Thickness(0, 2.5, 0, 2.5);
        _time.Foreground = _text.Foreground = new SolidColorBrush(Color.Parse("#FF9A93B8"));
        _icon.Foreground = _text.Foreground;
        SetColumn(_icon, 1);
        SetColumn(_text, 2);
        Children.Add(_time);
        Children.Add(_icon);
        Children.Add(_text);
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        var parts = AiLiveActionFeed.Parts(DataContext as string ?? "");
        var action = IconText.Split(parts.Action);
        _time.Text = parts.Timestamp;
        _text.Text = action.Rest;
        _icon.IsVisible = action.Icons.Length > 0;
        _icon.Kind = action.Icons.Length > 0 && IconMap.TryGet(action.Icons[0], out var mapped)
            ? mapped.Kind : IconKind.Info;
    }
}
