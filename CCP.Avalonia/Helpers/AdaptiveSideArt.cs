using System;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// Port of ConditioningControlPanel/Features/AdaptiveSideArt.cs. Put
    /// <c>feat:AdaptiveSideArt.CollapseBelow="700"</c> on the wrapping Grid: below that width (DIPs,
    /// measured on the Grid) the LAST column goes to zero, whatever sits in it hides, and the other
    /// columns' MaxWidth cap is lifted; restored once the Grid is threshold + <see cref="WidenMargin"/>
    /// wide again (hysteresis, ccp-bugs #1321). No animation, idempotent, same as WPF.
    /// </summary>
    public static class AdaptiveSideArt
    {
        public static readonly AttachedProperty<double> CollapseBelowProperty =
            AvaloniaProperty.RegisterAttached<Grid, double>("CollapseBelow", typeof(AdaptiveSideArt), double.NaN);

        public static void SetCollapseBelow(Grid element, double value) => element.SetValue(CollapseBelowProperty, value);
        public static double GetCollapseBelow(Grid element) => element.GetValue(CollapseBelowProperty);

        private static readonly AttachedProperty<GridLength?> OriginalWidthProperty =
            AvaloniaProperty.RegisterAttached<ColumnDefinition, GridLength?>("OriginalWidth", typeof(AdaptiveSideArt));
        private static readonly AttachedProperty<double?> OriginalMaxWidthProperty =
            AvaloniaProperty.RegisterAttached<ColumnDefinition, double?>("OriginalMaxWidth", typeof(AdaptiveSideArt));
        private static readonly AttachedProperty<bool> IsCollapsedProperty =
            AvaloniaProperty.RegisterAttached<Grid, bool>("IsCollapsed", typeof(AdaptiveSideArt));

        /// <summary>WPF AdaptiveSideArt.WidenMargin: wider than any scrollbar.</summary>
        internal const double WidenMargin = 48;

        internal static bool ShouldCollapse(double width, double threshold, bool collapsedNow)
            => collapsedNow ? width < threshold + WidenMargin : width < threshold;

        static AdaptiveSideArt()
        {
            CollapseBelowProperty.Changed.AddClassHandler<Grid>((grid, e) =>
            {
                grid.SizeChanged -= OnSizeChanged;
                grid.Loaded -= OnLoaded;
                if (double.IsNaN(GetCollapseBelow(grid))) return;
                grid.SizeChanged += OnSizeChanged;
                grid.Loaded += OnLoaded;
                Apply(grid);
            });
        }

        private static void OnLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => Apply(sender as Grid);
        private static void OnSizeChanged(object? sender, SizeChangedEventArgs e) => Apply(sender as Grid);

        internal static void Apply(Grid? grid)
        {
            try
            {
                if (grid == null) return;
                double threshold = GetCollapseBelow(grid);
                if (double.IsNaN(threshold)) return;

                var cols = grid.ColumnDefinitions;
                if (cols.Count < 2) return;               // one-column page: nothing to collapse

                double width = grid.Bounds.Width;
                if (width <= 0) return;                   // not measured yet - leave as authored

                var col = cols[^1];
                if (col.GetValue(OriginalWidthProperty) is null) col.SetValue(OriginalWidthProperty, col.Width);

                bool collapse = ShouldCollapse(width, threshold, grid.GetValue(IsCollapsedProperty));
                grid.SetValue(IsCollapsedProperty, collapse);
                var target = collapse ? new GridLength(0) : col.GetValue(OriginalWidthProperty) ?? new GridLength(2, GridUnitType.Star);
                if (col.Width != target) col.Width = target;

                // WPF: the settings column's MaxWidth (780) only exists to give width back to the art.
                for (int i = 0; i < cols.Count - 1; i++)
                {
                    var c = cols[i];
                    if (c.GetValue(OriginalMaxWidthProperty) is null) c.SetValue(OriginalMaxWidthProperty, c.MaxWidth);
                    double wantMax = collapse ? double.PositiveInfinity : c.GetValue(OriginalMaxWidthProperty) ?? double.PositiveInfinity;
                    if (!c.MaxWidth.Equals(wantMax)) c.MaxWidth = wantMax;
                }

                int index = cols.Count - 1;
                foreach (var child in grid.Children)
                    if (Grid.GetColumn(child) == index && child.IsVisible == collapse) child.IsVisible = !collapse;
            }
            catch (Exception ex)
            {
                // A layout helper must never take the page down with it.
                Log.Debug("AdaptiveSideArt apply failed: {E}", ex.Message);
            }
        }
    }
}
