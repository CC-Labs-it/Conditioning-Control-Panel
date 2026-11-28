using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Avalonia.Controls.Rows
{
    /// <summary>
    /// Readable Rows (owner 2026-11-24), the Avalonia twin of WPF <c>Controls/Rows/RowHue.cs</c>: the
    /// section hue an option row wears, as an INHERITED attached property. A section sets it on its
    /// body, so every <see cref="RowLabel"/>, <see cref="RowValue"/> and <see cref="OptionEmblem"/>
    /// inside picks it up. Unset (transparent) = the part falls back to PinkBrush's colour.
    /// Colour maths live in CCP.Core <see cref="EmblemPalette"/>; this file only carries values.
    /// </summary>
    public static class RowHue
    {
        public static readonly AttachedProperty<Color> HueProperty =
            AvaloniaProperty.RegisterAttached<Control, Color>("Hue", typeof(RowHue), Colors.Transparent, inherits: true);

        public static Color GetHue(AvaloniaObject d) => d.GetValue(HueProperty);
        public static void SetHue(AvaloniaObject d, Color value) => d.SetValue(HueProperty, value);

        /// <summary>
        /// Opt-in: a page that sets this (inherited) asks every box inside it that names its own hue to
        /// fill its sliders' tracks in that hue. Pages without it keep the pink fill.
        /// </summary>
        public static readonly AttachedProperty<bool> TintSlidersProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("TintSliders", typeof(RowHue), false, inherits: true);

        public static bool GetTintSliders(AvaloniaObject d) => d.GetValue(TintSlidersProperty);
        public static void SetTintSliders(AvaloniaObject d, bool value) => d.SetValue(TintSlidersProperty, value);

        /// <summary>
        /// The brush a slider fills its track with (inherited). Null = PinkBrush. Set on the element that
        /// names its own <see cref="HueProperty"/> while <see cref="TintSlidersProperty"/> is on
        /// (WPF 3d646a89f0 + 5ed0725ee6). A slider theme reads it; the page sweep wires that.
        /// </summary>
        public static readonly AttachedProperty<IBrush?> SliderFillProperty =
            AvaloniaProperty.RegisterAttached<Control, IBrush?>("SliderFill", typeof(RowHue), null, inherits: true);

        public static IBrush? GetSliderFill(AvaloniaObject d) => d.GetValue(SliderFillProperty);
        public static void SetSliderFill(AvaloniaObject d, IBrush? value) => d.SetValue(SliderFillProperty, value);

        /// <summary>
        /// The sticker edge every <see cref="OptionEmblem"/> below draws (inherited, ccp-bugs #1742).
        /// Default <see cref="EmblemPalette.ShippedEdge"/>; set it on a page or window root to switch a
        /// whole surface without touching the emblems.
        /// </summary>
        public static readonly AttachedProperty<EmblemEdge> EdgeProperty =
            AvaloniaProperty.RegisterAttached<Control, EmblemEdge>("Edge", typeof(RowHue), EmblemPalette.ShippedEdge, inherits: true);

        public static EmblemEdge GetEdge(AvaloniaObject d) => d.GetValue(EdgeProperty);
        public static void SetEdge(AvaloniaObject d, EmblemEdge value) => d.SetValue(EdgeProperty, value);

        static RowHue()
        {
            HueProperty.Changed.AddClassHandler<Control>((d, _) => ApplyBoxFill(d));
            TintSlidersProperty.Changed.AddClassHandler<Control>((d, _) => ApplyBoxFill(d));
        }

        /// <summary>
        /// A box that names its own hue fills its sliders in that hue on a page that opted in. Only the
        /// element that SET the hue acts; the elements that inherit it do nothing here.
        /// </summary>
        private static void ApplyBoxFill(Control d)
        {
            if (!d.IsSet(HueProperty)) return;
            var hue = GetHue(d);
            if (GetTintSliders(d) && hue.A != 0) SetSliderFill(d, new ImmutableSolidColorBrush(hue));
            else d.ClearValue(SliderFillProperty);
        }

        /// <summary>
        /// The hue a row part draws in: its own <paramref name="own"/> when set, else the inherited
        /// <see cref="HueProperty"/>, else <paramref name="fallback"/> (PinkBrush), else the stock pink.
        /// </summary>
        internal static Color Resolve(AvaloniaObject part, Color own, IBrush? fallback)
        {
            if (own.A != 0) return own;
            var inherited = GetHue(part);
            if (inherited.A != 0) return inherited;
            if (fallback is ISolidColorBrush s) return s.Color;
            return ToColor(EmblemPalette.Pink);
        }

        /// <summary>The resource brush <paramref name="key"/> as the part sees it, or null.</summary>
        internal static IBrush? FindBrush(Control part, string key)
            => part.TryFindResource(key, part.ActualThemeVariant, out var v) ? v as IBrush : null;

        internal static uint ToArgb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        internal static Color ToColor(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        internal static IImmutableSolidColorBrush Brush(uint argb) => new ImmutableSolidColorBrush(ToColor(argb));
    }

    /// <summary>The faces of Readable Rows: bundled Nunito static cuts for rows, Fredoka for headers.</summary>
    public static class RowFonts
    {
        /// <summary>Nunito Bold + ExtraBold (OFL), bundled under Assets/fonts/nunito (WPF ConditioningControlPanel/Fonts).
        /// Its own folder, so the family source only scans Nunito's faces.</summary>
        public static readonly FontFamily Nunito = new("avares://CCP.Avalonia/Resources/fonts/nunito#Nunito");
        public static readonly FontFamily Fredoka = new(ConditioningControlPanel.Avalonia.Platform.AppFonts.Fredoka);

        /// <summary>Row label: 16 Bold Nunito.</summary>
        public const double LabelSize = 16;
        /// <summary>Row number: 19 ExtraBold Nunito.</summary>
        public const double ValueSize = 19;
        /// <summary>Unit under a number: 11 Bold Nunito in TextMuted.</summary>
        public const double UnitSize = 11;
        /// <summary>Emblem box beside a label.</summary>
        public const double EmblemSize = 34;
    }
}
