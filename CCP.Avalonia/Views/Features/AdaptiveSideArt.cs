using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Port of WPF Features/AdaptiveSideArt.cs: <c>feat:AdaptiveSideArt.CollapseBelow="1000"</c> on a
    /// Grid collapses its LAST column (the art) below that width, hides the children in it, and lifts
    /// the other columns' MaxWidth while collapsed. Hysteresis is Core <see cref="SideArtRules"/>, the
    /// same rule WPF calls. No animation: a layout that reacts to a resize must not also perform.
    /// </summary>
    public static class AdaptiveSideArt
    {
        public static readonly AttachedProperty<double> CollapseBelowProperty =
            AvaloniaProperty.RegisterAttached<Grid, double>("CollapseBelow", typeof(AdaptiveSideArt), double.NaN);

        public static double GetCollapseBelow(Grid g) => g.GetValue(CollapseBelowProperty);
        public static void SetCollapseBelow(Grid g, double value) => g.SetValue(CollapseBelowProperty, value);

        private static readonly AttachedProperty<bool> IsCollapsedProperty =
            AvaloniaProperty.RegisterAttached<Grid, bool>("IsCollapsed", typeof(AdaptiveSideArt));
        private static readonly AttachedProperty<GridLength?> OriginalWidthProperty =
            AvaloniaProperty.RegisterAttached<ColumnDefinition, GridLength?>("OriginalWidth", typeof(AdaptiveSideArt));
        private static readonly AttachedProperty<double?> OriginalMaxWidthProperty =
            AvaloniaProperty.RegisterAttached<ColumnDefinition, double?>("OriginalMaxWidth", typeof(AdaptiveSideArt));

        static AdaptiveSideArt() => CollapseBelowProperty.Changed.AddClassHandler<Grid>((g, _) =>
        {
            g.SizeChanged -= OnSizeChanged;
            if (double.IsNaN(GetCollapseBelow(g))) return;
            g.SizeChanged += OnSizeChanged;
            Apply(g);
        });

        private static void OnSizeChanged(object? s, SizeChangedEventArgs e) => Apply((Grid)s!);

        private static void Apply(Grid grid)
        {
            var threshold = GetCollapseBelow(grid);
            var cols = grid.ColumnDefinitions;
            var width = grid.Bounds.Width;
            if (double.IsNaN(threshold) || cols.Count < 2 || width <= 0) return;

            var collapse = SideArtRules.ShouldCollapse(width, threshold, grid.GetValue(IsCollapsedProperty));
            grid.SetValue(IsCollapsedProperty, collapse);

            var art = cols[cols.Count - 1];
            art.SetValue(OriginalWidthProperty, art.GetValue(OriginalWidthProperty) ?? art.Width);
            var width0 = collapse ? new GridLength(0) : art.GetValue(OriginalWidthProperty)!.Value;
            if (art.Width != width0) art.Width = width0;

            // The settings cap (780) only exists to give width back to the art; lift it while collapsed.
            for (var i = 0; i < cols.Count - 1; i++)
            {
                var c = cols[i];
                c.SetValue(OriginalMaxWidthProperty, c.GetValue(OriginalMaxWidthProperty) ?? c.MaxWidth);
                var max = collapse ? double.PositiveInfinity : c.GetValue(OriginalMaxWidthProperty)!.Value;
                if (!c.MaxWidth.Equals(max)) c.MaxWidth = max;
            }

            foreach (var child in grid.Children)
                if (Grid.GetColumn(child) == cols.Count - 1 && child.IsVisible == collapse)
                    child.IsVisible = !collapse;
        }
    }
}
