using System;
using System.Globalization;
using System.Windows.Data;

namespace ConditioningControlPanel.Controls.Rows
{
    /// <summary>
    /// Readable Rows: a slider's value as a 0..1 fraction of its Maximum, for the emblems that want one
    /// (Often's clock hand). XAML: a MultiBinding of the slider's Value then its Maximum.
    /// <see cref="Inverse"/> is for "every N seconds" / interval / cooldown sliders, where a bigger
    /// number means LESS often: a MultiBinding of Value, Maximum then Minimum, giving 1 at the
    /// Minimum (most often) and 0 at the Maximum.
    /// </summary>
    public sealed class RowFraction : IMultiValueConverter
    {
        public static readonly RowFraction Instance = new();

        /// <summary>The period form: 1 - (value - min) / (max - min). Min defaults to 0 when not bound.</summary>
        public static readonly RowFraction Inverse = new() { IsInverse = true };

        public bool IsInverse { get; private init; }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not double v || values[1] is not double max || max <= 0) return 0.0;
            if (!IsInverse) return Math.Clamp(v / max, 0.0, 1.0);
            double min = values.Length > 2 && values[2] is double m ? m : 0.0;
            if (max <= min) return 0.0;
            return Math.Clamp(1.0 - (v - min) / (max - min), 0.0, 1.0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
