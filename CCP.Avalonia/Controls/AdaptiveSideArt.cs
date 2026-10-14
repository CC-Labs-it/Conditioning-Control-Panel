using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>WPF Features/AdaptiveSideArt: hide the last column below the page's
    /// threshold, lift the other columns' caps, then restore the authored dimensions.</summary>
    public static class AdaptiveSideArt
    {
        public static readonly AttachedProperty<double> CollapseBelowProperty =
            AvaloniaProperty.RegisterAttached<Grid, double>("CollapseBelow", typeof(AdaptiveSideArt), double.NaN);

        public static double GetCollapseBelow(Grid grid) => grid.GetValue(CollapseBelowProperty);
        public static void SetCollapseBelow(Grid grid, double value) => grid.SetValue(CollapseBelowProperty, value);

        private sealed class State
        {
            public bool Collapsed;
            public GridLength? ArtWidth;
            public readonly ConditionalWeakTable<ColumnDefinition, Cap> Caps = new();
        }

        private sealed class Cap(double value) { public readonly double Value = value; }
        private static readonly ConditionalWeakTable<Grid, State> States = new();

        static AdaptiveSideArt() => CollapseBelowProperty.Changed.AddClassHandler<Grid>((grid, e) =>
        {
            grid.SizeChanged -= OnSizeChanged;
            grid.Loaded -= OnLoaded;
            if (double.IsNaN(GetCollapseBelow(grid))) return;
            grid.SizeChanged += OnSizeChanged;
            grid.Loaded += OnLoaded;
            Apply(grid);
        });

        private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        { if (sender is Grid grid) Apply(grid); }

        private static void OnLoaded(object? sender, RoutedEventArgs e)
        { if (sender is Grid grid) Apply(grid); }

        private static void Apply(Grid grid)
        {
            try
            {
                var threshold = GetCollapseBelow(grid);
                var cols = grid.ColumnDefinitions;
                if (double.IsNaN(threshold) || cols.Count < 2 || grid.Bounds.Width <= 0) return;
                var state = States.GetOrCreateValue(grid);
                var art = cols[^1];
                state.ArtWidth ??= art.Width;
                state.Collapsed = AdaptiveSideArtRule.ShouldCollapse(grid.Bounds.Width, threshold, state.Collapsed);
                var target = state.Collapsed ? new GridLength(0) : state.ArtWidth.Value;
                if (art.Width != target) art.Width = target;
                for (int i = 0; i < cols.Count - 1; i++)
                {
                    var col = cols[i];
                    var cap = state.Caps.GetValue(col, c => new Cap(c.MaxWidth));
                    var max = state.Collapsed ? double.PositiveInfinity : cap.Value;
                    if (!col.MaxWidth.Equals(max)) col.MaxWidth = max;
                }
                foreach (var child in grid.Children)
                    if (Grid.GetColumn(child) == cols.Count - 1 && child.IsVisible == state.Collapsed)
                        child.IsVisible = !state.Collapsed;
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "AdaptiveSideArt apply failed"); }
        }
    }
}
