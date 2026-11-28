using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Metadata;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Avalonia.Controls.Rows;

/// <summary>A section-coloured number with an optional unit underneath.</summary>
public class RowValue : ContentControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<RowValue, string?>(nameof(Text), string.Empty);
    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<RowValue, string?>(nameof(Unit), string.Empty);
    private static readonly StyledProperty<IBrush?> FallbackBrushProperty =
        AvaloniaProperty.Register<RowValue, IBrush?>("FallbackBrush");

    private static readonly FontFamily Nunito = new("avares://CCP.Avalonia/Resources/fonts/nunito#Nunito");
    private readonly TextBlock _number = new()
    {
        FontFamily = Nunito, FontSize = 19, FontWeight = FontWeight.ExtraBold,
        TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right,
        TextWrapping = TextWrapping.NoWrap,
    };
    private readonly TextBlock _unit = new()
    {
        FontFamily = Nunito, FontSize = 11, FontWeight = FontWeight.Bold,
        TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right,
        TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 1, 0, 0), IsVisible = false,
    };

    public RowValue()
    {
        _unit[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        this[!FallbackBrushProperty] = new DynamicResourceExtension("PinkBrush");
        Content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Children = { _number, _unit },
        };
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        Repaint();
    }

    [Content]
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public Color EffectiveHue => RowHue.Resolve(this, GetValue(FallbackBrushProperty));
    internal TextBlock NumberPart => _number;
    internal TextBlock UnitPart => _unit;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) _number.Text = Text ?? string.Empty;
        else if (change.Property == UnitProperty)
        {
            _unit.Text = Unit ?? string.Empty;
            _unit.IsVisible = !string.IsNullOrEmpty(Unit);
        }
        else if (change.Property == RowHue.HueProperty || change.Property == FallbackBrushProperty) Repaint();
    }

    private void Repaint() => _number.Foreground = new SolidColorBrush(
        RowHue.ToColor(EmblemPalette.ValueTint(RowHue.ToArgb(EffectiveHue))));
}
