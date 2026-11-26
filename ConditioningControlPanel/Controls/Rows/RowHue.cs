using System;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Controls.Rows
{
    /// <summary>
    /// Readable Rows (owner 2026-11-24): the section hue an option row wears, as an INHERITED attached
    /// property. <see cref="Features.OptionSection"/> sets it on its body from its theme brush, so every
    /// <see cref="RowLabel"/>, <see cref="RowValue"/> and <see cref="OptionEmblem"/> inside picks it up.
    /// Unset (transparent) = the part falls back to PinkBrush's colour.
    /// </summary>
    public static class RowHue
    {
        public static readonly DependencyProperty HueProperty = DependencyProperty.RegisterAttached(
            "Hue", typeof(Color), typeof(RowHue),
            new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.Inherits,
                (d, _) => ApplyBoxFill(d)));

        public static Color GetHue(DependencyObject d) => (Color)d.GetValue(HueProperty);
        public static void SetHue(DependencyObject d, Color value) => d.SetValue(HueProperty, value);

        /// <summary>
        /// Opt-in for the pilot: a page that sets this (inherited) asks every <see cref="Features.OptionSection"/>
        /// inside it to fill its sliders' tracks in the section hue. Pages without it keep the pink fill.
        /// </summary>
        public static readonly DependencyProperty TintSlidersProperty = DependencyProperty.RegisterAttached(
            "TintSliders", typeof(bool), typeof(RowHue),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits,
                (d, _) => ApplyBoxFill(d)));

        /// <summary>
        /// A box that names its own hue outside a fold section (the gold Super boxes, the playlist card) fills
        /// its sliders in that hue on a page that opted in, the way an OptionSection does for its body.
        /// Only the element that SET the hue acts; the elements that inherit it do nothing here.
        /// </summary>
        private static void ApplyBoxFill(DependencyObject d)
        {
            if (DependencyPropertyHelper.GetValueSource(d, HueProperty).BaseValueSource != BaseValueSource.Local) return;
            var hue = GetHue(d);
            if (GetTintSliders(d) && hue.A != 0)
            {
                var fill = new SolidColorBrush(hue);
                fill.Freeze();
                SetSliderFill(d, fill);
            }
            else d.ClearValue(SliderFillProperty);
        }

        public static bool GetTintSliders(DependencyObject d) => (bool)d.GetValue(TintSlidersProperty);
        public static void SetTintSliders(DependencyObject d, bool value) => d.SetValue(TintSlidersProperty, value);

        /// <summary>
        /// The brush a PinkSlider fills its track with (inherited). Null = PinkBrush (the style's trigger).
        /// Set by an OptionSection on its body while <see cref="TintSlidersProperty"/> is on.
        /// </summary>
        public static readonly DependencyProperty SliderFillProperty = DependencyProperty.RegisterAttached(
            "SliderFill", typeof(Brush), typeof(RowHue),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        public static Brush? GetSliderFill(DependencyObject d) => (Brush?)d.GetValue(SliderFillProperty);
        public static void SetSliderFill(DependencyObject d, Brush? value) => d.SetValue(SliderFillProperty, value);

        /// <summary>
        /// The sticker edge every <see cref="OptionEmblem"/> below draws (inherited, ccp-bugs #1742).
        /// Default <see cref="EmblemPalette.ShippedEdge"/>; set it on a page or window root to switch
        /// a whole surface (stills, or a later user choice) without touching the emblems.
        /// </summary>
        public static readonly DependencyProperty EdgeProperty = DependencyProperty.RegisterAttached(
            "Edge", typeof(EmblemEdge), typeof(RowHue),
            new FrameworkPropertyMetadata(EmblemPalette.ShippedEdge, FrameworkPropertyMetadataOptions.Inherits));

        public static EmblemEdge GetEdge(DependencyObject d) => (EmblemEdge)d.GetValue(EdgeProperty);
        public static void SetEdge(DependencyObject d, EmblemEdge value) => d.SetValue(EdgeProperty, value);

        /// <summary>
        /// The hue a row part draws in: its own <paramref name="own"/> when set, else the inherited
        /// <see cref="HueProperty"/>, else <paramref name="fallback"/> (PinkBrush), else the stock pink.
        /// </summary>
        internal static Color Resolve(DependencyObject part, Color own, Brush? fallback)
        {
            if (own.A != 0) return own;
            var inherited = GetHue(part);
            if (inherited.A != 0) return inherited;
            if (fallback is SolidColorBrush s) return s.Color;
            return ToColor(EmblemPalette.Pink);
        }

        internal static uint ToArgb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        internal static Color ToColor(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        internal static SolidColorBrush Frozen(uint argb)
        {
            var b = new SolidColorBrush(ToColor(argb));
            b.Freeze();
            return b;
        }
    }

    /// <summary>The faces of Readable Rows: Nunito static cuts for rows, Fredoka for headers. Both bundled.</summary>
    public static class RowFonts
    {
        public static readonly FontFamily Nunito = new(new Uri("pack://application:,,,/"), "./Fonts/#Nunito, Segoe UI");
        public static readonly FontFamily Fredoka = new(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka, Segoe UI");

        /// <summary>Row label: 16 Bold Nunito.</summary>
        public const double LabelSize = 16;
        /// <summary>Row number: 19 ExtraBold Nunito.</summary>
        public const double ValueSize = 19;
        /// <summary>Unit under a number: 11 Bold Nunito in TextMuted.</summary>
        public const double UnitSize = 11;
        /// <summary>Emblem box beside a label.</summary>
        public const double EmblemSize = 34;
        /// <summary>A door list entry's caption (<see cref="DoorLabel"/>): the row label's size.</summary>
        public const double DoorSize = LabelSize;
    }
}
