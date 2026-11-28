using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Controls.Rows
{
    /// <summary>
    /// Readable Rows (owner 2026-11-24): an option's number, 19 ExtraBold Nunito in the section hue
    /// (82% hue + 18% white), with an optional unit under it (11 Bold Nunito, TextMuted), right
    /// aligned: "180" over "an hour". Code-behind keeps writing <c>TxtFrequency.Text = "..."</c>.
    /// </summary>
    [ContentProperty(nameof(Text))]
    public class RowValue : RowPart
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(RowValue),
            new FrameworkPropertyMetadata(string.Empty, (d, e) => ((RowValue)d)._number.Text = e.NewValue as string ?? string.Empty));

        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(RowValue),
            new FrameworkPropertyMetadata(string.Empty, (d, _) => ((RowValue)d).RefreshUnit()));

        private static readonly DependencyProperty FallbackBrushProperty = DependencyProperty.Register(
            "FallbackBrush", typeof(Brush), typeof(RowValue),
            new FrameworkPropertyMetadata(null, (d, _) => ((RowValue)d).Repaint()));

        static RowValue()
        {
            RowHue.HueProperty.OverrideMetadata(typeof(RowValue), new FrameworkPropertyMetadata(
                Colors.Transparent, FrameworkPropertyMetadataOptions.Inherits, (d, _) => ((RowValue)d).Repaint()));
        }

        private readonly TextBlock _number = new()
        {
            FontFamily = RowFonts.Nunito,
            FontSize = RowFonts.ValueSize,
            FontWeight = FontWeights.ExtraBold,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextWrapping = TextWrapping.NoWrap,
        };

        private readonly TextBlock _unit = new()
        {
            FontFamily = RowFonts.Nunito,
            FontSize = RowFonts.UnitSize,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 1, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        public RowValue()
        {
            System.Windows.Documents.Typography.SetNumeralAlignment(_number, FontNumeralAlignment.Tabular);
            _unit.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_number);
            stack.Children.Add(_unit);
            Child = stack;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Center;
            SetResourceReference(FallbackBrushProperty, "PinkBrush");
            Repaint();
        }

        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

        /// <summary>The section hue this number resolved to.</summary>
        public Color EffectiveHue => RowHue.Resolve(this, Colors.Transparent, (Brush?)GetValue(FallbackBrushProperty));

        internal TextBlock NumberPart => _number;
        internal TextBlock UnitPart => _unit;

        private void RefreshUnit()
        {
            _unit.Text = Unit ?? string.Empty;
            _unit.Visibility = string.IsNullOrEmpty(Unit) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Repaint() =>
            _number.Foreground = RowHue.Frozen(EmblemPalette.ValueTint(RowHue.ToArgb(EffectiveHue)));
    }
}
