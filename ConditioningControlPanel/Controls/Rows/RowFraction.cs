using System;
using System.Globalization;
using System.Windows.Data;

namespace ConditioningControlPanel.Controls.Rows
{
    /// <summary>
    /// Readable Rows: a slider's value as a 0..1 fraction of its Maximum, for the emblems that want one
    /// (Often's clock hand). XAML: a MultiBinding of the slider's Value then its Maximum.
    /// </summary>
    public sealed class RowFraction : IMultiValueConverter
    {
        public static readonly RowFraction Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not double v || values[1] is not double max || max <= 0) return 0.0;
            return Math.Clamp(v / max, 0.0, 1.0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
