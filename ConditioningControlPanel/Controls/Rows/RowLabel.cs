using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Controls.Rows
{
    /// <summary>
    /// A FrameworkElement that hosts one private child (visual + logical), so the row parts expose
    /// only their own properties and their insides inherit fonts, resources and <see cref="RowHue"/>.
    /// </summary>
    public abstract class RowPart : FrameworkElement
    {
        private UIElement? _child;

        protected UIElement? Child
        {
            get => _child;
            set
            {
                if (_child != null) { RemoveVisualChild(_child); RemoveLogicalChild(_child); }
                _child = value;
                if (_child != null) { AddLogicalChild(_child); AddVisualChild(_child); }
                InvalidateMeasure();
            }
        }

        protected override int VisualChildrenCount => _child == null ? 0 : 1;
        protected override Visual GetVisualChild(int index) =>
            index == 0 && _child != null ? _child : throw new System.ArgumentOutOfRangeException(nameof(index));
        protected override IEnumerator LogicalChildren =>
            _child == null ? System.Linq.Enumerable.Empty<object>().GetEnumerator() : new object[] { _child }.GetEnumerator();

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_child == null) return default;
            _child.Measure(availableSize);
            return _child.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _child?.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }

    /// <summary>
    /// Readable Rows (owner 2026-11-24): an option's label. The emblem (when <see cref="Emblem"/> is
    /// set) then the text, 16 Bold Nunito in 70% TextLight + 30% section hue. With an emblem a leading
    /// emoji in the text is dropped ("🐙Hydra" draws "Hydra"); the loc files keep it. An OFF option
    /// (<see cref="IsOn"/> false) draws its label at 60% and its emblem grey. No animation.
    /// The hue comes from the inherited <see cref="RowHue"/>, else PinkBrush.
    /// </summary>
    [ContentProperty(nameof(Text))]
    public class RowLabel : RowPart
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(RowLabel),
            new FrameworkPropertyMetadata(string.Empty, (d, _) => ((RowLabel)d).Refresh()));

        /// <summary>The picture; null = a label with no emblem (the text keeps its emoji).</summary>
        public static readonly DependencyProperty EmblemProperty = DependencyProperty.Register(
            nameof(Emblem), typeof(EmblemKind?), typeof(RowLabel),
            new FrameworkPropertyMetadata(null, (d, _) => ((RowLabel)d).Refresh()));

        public static readonly DependencyProperty SubjectProperty = DependencyProperty.Register(
            nameof(Subject), typeof(EmblemSubject), typeof(RowLabel),
            new FrameworkPropertyMetadata(EmblemSubject.Image, (d, _) => ((RowLabel)d).Refresh()));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(RowLabel),
            new FrameworkPropertyMetadata(0.0, (d, _) => ((RowLabel)d).Refresh()));

        public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
            nameof(IsOn), typeof(bool), typeof(RowLabel),
            new FrameworkPropertyMetadata(true, (d, _) => ((RowLabel)d).Refresh()));

        private static readonly DependencyProperty FallbackBrushProperty = DependencyProperty.Register(
            "FallbackBrush", typeof(Brush), typeof(RowLabel),
            new FrameworkPropertyMetadata(null, (d, _) => ((RowLabel)d).Repaint()));

        private static readonly DependencyProperty TextLightBrushProperty = DependencyProperty.Register(
            "TextLightBrush", typeof(Brush), typeof(RowLabel),
            new FrameworkPropertyMetadata(null, (d, _) => ((RowLabel)d).Repaint()));

        static RowLabel()
        {
            RowHue.HueProperty.OverrideMetadata(typeof(RowLabel), new FrameworkPropertyMetadata(
                Colors.Transparent, FrameworkPropertyMetadataOptions.Inherits, (d, _) => ((RowLabel)d).Repaint()));
        }

        private readonly OptionEmblem _emblem = new() { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _text = new()
        {
            FontFamily = RowFonts.Nunito,
            FontSize = RowFonts.LabelSize,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        public RowLabel()
        {
            var row = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(_emblem, Dock.Left);
            row.Children.Add(_emblem);
            row.Children.Add(_text);
            Child = row;
            VerticalAlignment = VerticalAlignment.Center;
            SetResourceReference(FallbackBrushProperty, "PinkBrush");
            SetResourceReference(TextLightBrushProperty, "TextLightBrush");
            Refresh();
        }

        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public EmblemKind? Emblem { get => (EmblemKind?)GetValue(EmblemProperty); set => SetValue(EmblemProperty, value); }
        public EmblemSubject Subject { get => (EmblemSubject)GetValue(SubjectProperty); set => SetValue(SubjectProperty, value); }
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }

        /// <summary>The text as drawn (emoji dropped when there is an emblem). For tests and automation.</summary>
        public string ShownText => _text.Text;

        /// <summary>The section hue this label resolved to.</summary>
        public Color EffectiveHue => RowHue.Resolve(this, Colors.Transparent, (Brush?)GetValue(FallbackBrushProperty));

        internal OptionEmblem EmblemPart => _emblem;
        internal TextBlock TextPart => _text;

        private void Refresh()
        {
            var kind = Emblem;
            _emblem.Visibility = kind.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (kind.HasValue) _emblem.Kind = kind.Value;
            _emblem.Subject = Subject;
            _emblem.Value = Value;
            _emblem.IsOn = IsOn;
            _text.Text = kind.HasValue ? LeadingEmoji.Strip(Text) : (Text ?? string.Empty);
            _text.Opacity = IsOn ? 1.0 : EmblemPalette.OffLabelOpacity;
            Repaint();
        }

        private void Repaint()
        {
            var light = GetValue(TextLightBrushProperty) is SolidColorBrush l ? RowHue.ToArgb(l.Color) : EmblemPalette.TextLight;
            uint hue = RowHue.ToArgb(EffectiveHue);
            _text.Foreground = RowHue.Frozen(EmblemPalette.LabelTint(light, hue));
        }
    }
}
