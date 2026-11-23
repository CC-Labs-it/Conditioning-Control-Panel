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
            new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.Inherits));

        public static Color GetHue(DependencyObject d) => (Color)d.GetValue(HueProperty);
        public static void SetHue(DependencyObject d, Color value) => d.SetValue(HueProperty, value);

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
    }
}
