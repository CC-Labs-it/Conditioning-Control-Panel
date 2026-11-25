using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Super;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>WPF SplitFeatureCard.ApplySuper: a V2 corner per half, top-left for A and bottom-right
    /// for B, each just under its half's active ring so the ring runs over it and the half's clip cuts
    /// it with the seam. The title pills step in so no name sits under a corner.</summary>
    public partial class SplitFeatureCard
    {
        public static readonly StyledProperty<string?> SuperAProperty =
            AvaloniaProperty.Register<SplitFeatureCard, string?>(nameof(SuperA));

        public static readonly StyledProperty<string?> SuperBProperty =
            AvaloniaProperty.Register<SplitFeatureCard, string?>(nameof(SuperB));

        public string? SuperA { get => GetValue(SuperAProperty); set => SetValue(SuperAProperty, value); }
        public string? SuperB { get => GetValue(SuperBProperty); set => SetValue(SuperBProperty, value); }

        private SuperCorner? _cornerA, _cornerB;
        internal SuperCorner? CornerA => _cornerA;
        internal SuperCorner? CornerB => _cornerB;
        private const double CornerInset = SuperCorner.Leg + 2;

        static SplitFeatureCard()
        {
            SuperAProperty.Changed.AddClassHandler<SplitFeatureCard>((c, _) => c.ApplySuper());
            SuperBProperty.Changed.AddClassHandler<SplitFeatureCard>((c, _) => c.ApplySuper());
        }

        private void ApplySuper()
        {
            _cornerA = Swap(_cornerA, SuperA, bottomRight: false);
            _cornerB = Swap(_cornerB, SuperB, bottomRight: true);
            if (this.FindControl<Border>("TitlePillA") is { } a) a.Margin = new Thickness(_cornerA != null ? CornerInset : 8, 8, 0, 0);
            if (this.FindControl<Border>("TitlePillB") is { } b) b.Margin = new Thickness(0, 0, _cornerB != null ? CornerInset : 8, 8);
            ApplySuperHover();
        }

        private SuperCorner? Swap(SuperCorner? old, string? name, bool bottomRight)
        {
            var region = this.FindControl<Grid>(bottomRight ? "HalfRegionB" : "HalfRegionA");
            if (region is null) return null;
            if (old != null) region.Children.Remove(old);
            var corner = SuperCorner.For(name, bottomRight, () => DashboardDepth && CoreSettings.Current.DashboardInvertClicks);
            if (corner != null)
                region.Children.Insert(region.Children.IndexOf(this.FindControl<Path>(bottomRight ? "ActiveRingB" : "ActiveRingA")!), corner);
            return corner;
        }

        /// <summary>The corners step aside with the "?" buttons, so the other half's peek wedge stays its own.</summary>
        private void ApplySuperHover()
        {
            if (_cornerA != null) { _cornerA.Opacity = _halfHover == false ? 0 : 1; _cornerA.IsHitTestVisible = _halfHover != false; }
            if (_cornerB != null) { _cornerB.Opacity = _halfHover == true ? 0 : 1; _cornerB.IsHitTestVisible = _halfHover != true; }
        }

        /// <summary>WPF RouteClick: the on/off click on a corner flips that half's V2 (true = handled);
        /// the other click opens the corner's own half.</summary>
        private bool PressedSuperCorner(PointerReleasedEventArgs e, bool toggle, ref bool halfA)
        {
            if (e.Source is not Visual src) return false;
            var corner = Within(src, _cornerA) ? _cornerA : Within(src, _cornerB) ? _cornerB : null;
            if (corner == null) return false;
            if (toggle) { e.Handled = true; corner.Press(); return true; }
            halfA = ReferenceEquals(corner, _cornerA);
            return false;
        }

        private static bool Within(Visual src, SuperCorner? c) => c != null && (src == c || c.IsVisualAncestorOf(src));
    }
}
