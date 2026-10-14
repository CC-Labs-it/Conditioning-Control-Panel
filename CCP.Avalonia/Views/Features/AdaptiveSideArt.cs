using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Features/AdaptiveSideArt.cs: put
    /// <c>feat:AdaptiveSideArt.CollapseBelow="1000"</c> on a page's wrapping Grid and, while the Grid
    /// is narrower than that, its LAST column goes to zero, whatever sits in it hides, and the
    /// settings columns lose their MaxWidth cap (restored when the art comes back). The width rule
    /// with its #1321 hysteresis is Core's <see cref="SideArtCollapse"/>. No animation: a layout that
    /// reacts to a resize must not also perform.
    /// </summary>
    public sealed class AdaptiveSideArt : AvaloniaObject
    {
        /// <summary>NaN (the default) means "never collapse".</summary>
        public static readonly AttachedProperty<double> CollapseBelowProperty =
            AvaloniaProperty.RegisterAttached<AdaptiveSideArt, Grid, double>("CollapseBelow", double.NaN);

        private static readonly AttachedProperty<bool> IsCollapsedProperty =
            AvaloniaProperty.RegisterAttached<AdaptiveSideArt, Grid, bool>("IsCollapsed");

        private static readonly AttachedProperty<GridLength?> OriginalWidthProperty =
            AvaloniaProperty.RegisterAttached<AdaptiveSideArt, ColumnDefinition, GridLength?>("OriginalWidth");

        private static readonly AttachedProperty<double?> OriginalMaxWidthProperty =
            AvaloniaProperty.RegisterAttached<AdaptiveSideArt, ColumnDefinition, double?>("OriginalMaxWidth");

        static AdaptiveSideArt()
        {
            CollapseBelowProperty.Changed.AddClassHandler<Grid>((grid, _) =>
            {
                grid.SizeChanged -= OnGridSizeChanged;   // idempotent re-set
                if (double.IsNaN(GetCollapseBelow(grid))) return;
                grid.SizeChanged += OnGridSizeChanged;
                Apply(grid);
            });
        }

        public static void SetCollapseBelow(Grid element, double value) => element.SetValue(CollapseBelowProperty, value);
        public static double GetCollapseBelow(Grid element) => element.GetValue(CollapseBelowProperty);

        /// <summary>Whether the art column is folded right now (tests and the next pass read it).</summary>
        internal static bool GetIsCollapsed(Grid element) => element.GetValue(IsCollapsedProperty);

        private static void OnGridSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (sender is Grid grid) Apply(grid);
        }

        private static void Apply(Grid grid)
        {
            double threshold = GetCollapseBelow(grid);
            var cols = grid.ColumnDefinitions;
            double width = grid.Bounds.Width;
            if (double.IsNaN(threshold) || cols.Count < 2 || width <= 0) return;   // not measured yet: as authored

            bool collapse = SideArtCollapse.ShouldCollapse(width, threshold, GetIsCollapsed(grid));
            grid.SetValue(IsCollapsedProperty, collapse);

            var art = cols[^1];
            var authored = art.GetValue(OriginalWidthProperty) ?? art.Width;
            art.SetValue(OriginalWidthProperty, authored);
            var target = collapse ? new GridLength(0) : authored;
            if (art.Width != target) art.Width = target;

            // The settings column's MaxWidth (780) exists to give width BACK to the art; with the art
            // gone it only leaves the settings narrower than the full-width cards below them.
            for (int i = 0; i < cols.Count - 1; i++)
            {
                var c = cols[i];
                var max = c.GetValue(OriginalMaxWidthProperty) ?? c.MaxWidth;
                c.SetValue(OriginalMaxWidthProperty, max);
                var want = collapse ? double.PositiveInfinity : max;
                if (!c.MaxWidth.Equals(want)) c.MaxWidth = want;
            }

            foreach (var child in grid.Children)
                if (Grid.GetColumn(child) == cols.Count - 1 && child.IsVisible == collapse)
                    child.IsVisible = !collapse;
        }
    }
}
