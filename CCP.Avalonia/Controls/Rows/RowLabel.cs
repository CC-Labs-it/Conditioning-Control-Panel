using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Avalonia.Controls.Rows
{
    /// <summary>
    /// A control that hosts one private child (visual + logical), so the row parts expose only their own
    /// properties and their insides inherit fonts, resources and <see cref="RowHue"/>. WPF RowPart.
    /// </summary>
    public abstract class RowPart : Control
    {
        private Control? _child;

        protected Control? Child
        {
            get => _child;
            set
            {
                if (_child != null) { VisualChildren.Remove(_child); LogicalChildren.Remove(_child); }
                _child = value;
                if (_child != null) { LogicalChildren.Add(_child); VisualChildren.Add(_child); }
                InvalidateMeasure();
            }
        }

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

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Repaint();   // PinkBrush / TextLightBrush resolve only once the part sits in a tree
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            // The inherited section hue arrives as an Inherited-priority change: repaint for it too.
            if (change.Property == RowHue.HueProperty || change.Property == ThemeVariantScope.ActualThemeVariantProperty) Repaint();
        }

        /// <summary>Re-reads the hue and the theme brushes.</summary>
        protected abstract void Repaint();
    }

    /// <summary>
    /// Readable Rows (owner 2026-11-24), the Avalonia twin of WPF <c>Controls/Rows/RowLabel.cs</c>: an
    /// option's label. The emblem (when <see cref="Emblem"/> is set) then the text, 16 Bold Nunito in
    /// 70% TextLight + 30% section hue. With an emblem a leading emoji in the text is dropped
    /// ("🐙Hydra" draws "Hydra"); the loc files keep it. An OFF option (<see cref="IsOn"/> false) draws
    /// its label at 60% and its emblem grey. No animation. Long labels WRAP (never an ellipsis, never
    /// smaller text, WPF 4511e6e4c5).
    /// </summary>
    public class RowLabel : RowPart
    {
        public static readonly StyledProperty<string?> TextProperty =
            AvaloniaProperty.Register<RowLabel, string?>(nameof(Text), string.Empty);

        /// <summary>The picture; null = a label with no emblem (the text keeps its emoji).</summary>
        public static readonly StyledProperty<EmblemKind?> EmblemProperty =
            AvaloniaProperty.Register<RowLabel, EmblemKind?>(nameof(Emblem));

        public static readonly StyledProperty<EmblemSubject> SubjectProperty =
            AvaloniaProperty.Register<RowLabel, EmblemSubject>(nameof(Subject), EmblemSubject.Image);

        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<RowLabel, double>(nameof(Value));

        public static readonly StyledProperty<bool> IsOnProperty =
            AvaloniaProperty.Register<RowLabel, bool>(nameof(IsOn), true);

        private readonly OptionEmblem _emblem = new() { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _text = new()
        {
            FontFamily = RowFonts.Nunito,
            FontSize = RowFonts.LabelSize,
            FontWeight = FontWeight.Bold,
            // Never clipped, never shrunk: a label whose column cannot hold it on one line wraps to a
            // second line and the row grows. The emblem stays centred on the block.
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
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
            Refresh();
        }

        [Content]
        public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public EmblemKind? Emblem { get => GetValue(EmblemProperty); set => SetValue(EmblemProperty, value); }
        public EmblemSubject Subject { get => GetValue(SubjectProperty); set => SetValue(SubjectProperty, value); }
        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public bool IsOn { get => GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }

        /// <summary>The text as drawn (emoji dropped when there is an emblem). For tests and automation.</summary>
        public string ShownText => _text.Text ?? string.Empty;

        /// <summary>The section hue this label resolved to.</summary>
        public Color EffectiveHue => RowHue.Resolve(this, Colors.Transparent, RowHue.FindBrush(this, "PinkBrush"));

        internal OptionEmblem EmblemPart => _emblem;
        internal TextBlock TextPart => _text;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == TextProperty || change.Property == EmblemProperty || change.Property == SubjectProperty
                || change.Property == ValueProperty || change.Property == IsOnProperty)
                Refresh();
        }

        private void Refresh()
        {
            var kind = Emblem;
            _emblem.IsVisible = kind.HasValue;
            if (kind.HasValue) _emblem.Kind = kind.Value;
            _emblem.Subject = Subject;
            _emblem.Value = Value;
            _emblem.IsOn = IsOn;
            _text.Text = kind.HasValue ? LeadingEmoji.Strip(Text) : (Text ?? string.Empty);
            _text.Opacity = IsOn ? 1.0 : EmblemPalette.OffLabelOpacity;
            Repaint();
        }

        protected override void Repaint()
        {
            var light = RowHue.FindBrush(this, "TextLightBrush") is ISolidColorBrush l ? RowHue.ToArgb(l.Color) : EmblemPalette.TextLight;
            uint hue = RowHue.ToArgb(EffectiveHue);
            _text.Foreground = RowHue.Brush(EmblemPalette.LabelTint(light, hue));
        }
    }
}
