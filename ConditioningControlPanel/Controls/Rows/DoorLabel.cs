using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Controls.Rows
{
    /// <summary>
    /// Readable Rows reach (owner 2026-11-25, 7.4.1 round): the caption of an entry in a DOOR LIST,
    /// a side list whose entries open a page (the Studio rack, the Settings mini-rail), in the row
    /// label look: 16 Bold Nunito in 70% TextLight + 30% hue, like <see cref="RowLabel"/>. No emblem:
    /// the door keeps its own chip or icon beside it.
    ///
    /// <para>A TextBlock on purpose, so code that holds an entry's caption as a TextBlock (the rack
    /// sets Text and Opacity on it) keeps working, and so a door's on/off dimming stays Opacity.</para>
    ///
    /// <para>The hue: <see cref="HueKey"/> names a brush resource (the entry's own section hue,
    /// read as a resource reference so a mod's theme recolours it), else the inherited
    /// <see cref="RowHue"/>, else PinkBrush. The colour is set locally, so a template trigger that
    /// sets the host's Foreground (hover, checked) leaves it alone: the door's fill and bar carry
    /// selection, the label stays readable in every state. It wraps rather than trims; a door list
    /// that can wrap must let its rows grow.</para>
    /// </summary>
    public class DoorLabel : TextBlock
    {
        /// <summary>Resource key of the brush whose colour tints this label; null = inherited RowHue, then PinkBrush.</summary>
        public static readonly DependencyProperty HueKeyProperty = DependencyProperty.Register(
            nameof(HueKey), typeof(string), typeof(DoorLabel),
            new FrameworkPropertyMetadata(null, (d, e) => ((DoorLabel)d).OnHueKey((string?)e.NewValue)));

        private static readonly DependencyProperty HueBrushProperty = DependencyProperty.Register(
            "HueBrush", typeof(Brush), typeof(DoorLabel),
            new FrameworkPropertyMetadata(null, (d, _) => ((DoorLabel)d).Repaint()));

        private static readonly DependencyProperty FallbackBrushProperty = DependencyProperty.Register(
            "FallbackBrush", typeof(Brush), typeof(DoorLabel),
            new FrameworkPropertyMetadata(null, (d, _) => ((DoorLabel)d).Repaint()));

        private static readonly DependencyProperty TextLightBrushProperty = DependencyProperty.Register(
            "TextLightBrush", typeof(Brush), typeof(DoorLabel),
            new FrameworkPropertyMetadata(null, (d, _) => ((DoorLabel)d).Repaint()));

        static DoorLabel()
        {
            RowHue.HueProperty.OverrideMetadata(typeof(DoorLabel), new FrameworkPropertyMetadata(
                Colors.Transparent, FrameworkPropertyMetadataOptions.Inherits, (d, _) => ((DoorLabel)d).Repaint()));
        }

        public DoorLabel()
        {
            FontFamily = RowFonts.Nunito;
            FontSize = RowFonts.DoorSize;
            FontWeight = FontWeights.Bold;
            TextWrapping = TextWrapping.Wrap;
            TextTrimming = TextTrimming.None;
            VerticalAlignment = VerticalAlignment.Center;
            // No hyphenation: WPF's hyphenator left "Benachrichtigungen" and "Производительность"
            // broken mid-word without a hyphen on a narrow rail (tried 2026-11-25), so a single word
            // wider than its door breaks where it must. The door list sizes for its own words.
            SetResourceReference(FallbackBrushProperty, "PinkBrush");
            SetResourceReference(TextLightBrushProperty, "TextLightBrush");
            Repaint();
        }

        public string? HueKey { get => (string?)GetValue(HueKeyProperty); set => SetValue(HueKeyProperty, value); }

        /// <summary>The colour this label is tinted with right now (own key, inherited, PinkBrush).</summary>
        public Color EffectiveHue =>
            GetValue(HueBrushProperty) is SolidColorBrush own && own.Color.A != 0
                ? own.Color
                : RowHue.Resolve(this, Colors.Transparent, (Brush?)GetValue(FallbackBrushProperty));

        private void OnHueKey(string? key)
        {
            if (string.IsNullOrEmpty(key)) ClearValue(HueBrushProperty);
            else SetResourceReference(HueBrushProperty, key);
        }

        private void Repaint()
        {
            var light = GetValue(TextLightBrushProperty) is SolidColorBrush l ? RowHue.ToArgb(l.Color) : EmblemPalette.TextLight;
            Foreground = RowHue.Frozen(EmblemPalette.LabelTint(light, RowHue.ToArgb(EffectiveHue)));
        }
    }
}
