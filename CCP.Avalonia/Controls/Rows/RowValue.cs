using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Avalonia.Controls.Rows
{
    /// <summary>
    /// Readable Rows (owner 2026-11-24), the Avalonia twin of WPF <c>Controls/Rows/RowValue.cs</c>: an
    /// option's number, 19 ExtraBold Nunito in the section hue (82% hue + 18% white), with an optional
    /// unit under it (11 Bold Nunito, TextMuted), right aligned: "180" over "an hour". Tabular figures,
    /// so a number that changes while a slider moves does not wobble.
    /// </summary>
    public class RowValue : RowPart
    {
        public static readonly StyledProperty<string?> TextProperty =
            AvaloniaProperty.Register<RowValue, string?>(nameof(Text), string.Empty);

        public static readonly StyledProperty<string?> UnitProperty =
            AvaloniaProperty.Register<RowValue, string?>(nameof(Unit), string.Empty);

        private readonly TextBlock _number = new()
        {
            FontFamily = RowFonts.Nunito,
            FontSize = RowFonts.ValueSize,
            FontWeight = FontWeight.ExtraBold,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextWrapping = TextWrapping.NoWrap,
            FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") },
        };

        private readonly TextBlock _unit = new()
        {
            FontFamily = RowFonts.Nunito,
            FontSize = RowFonts.UnitSize,
            FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 1, 0, 0),
            IsVisible = false,
        };

        public RowValue()
        {
            _unit.Bind(TextBlock.ForegroundProperty, _unit.GetResourceObservable("TextMutedBrush"));
            var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_number);
            stack.Children.Add(_unit);
            Child = stack;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Center;
            Repaint();
        }

        [Content]
        public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public string? Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

        /// <summary>The section hue this number resolved to.</summary>
        public Color EffectiveHue => RowHue.Resolve(this, Colors.Transparent, RowHue.FindBrush(this, "PinkBrush"));

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
        }

        protected override void Repaint() =>
            _number.Foreground = RowHue.Brush(EmblemPalette.ValueTint(RowHue.ToArgb(EffectiveHue)));
    }
}
