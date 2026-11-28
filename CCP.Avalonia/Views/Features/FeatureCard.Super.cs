using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Super;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>WPF FeatureCard.ApplySuper: the cut corner that switches the tile's V2 (top-left; the
    /// badge column steps aside for it), under the rim, the active ring and the lock veil.</summary>
    public partial class FeatureCard
    {
        /// <summary>The V2 this tile owns, by SuperEffect name ("FlickerDeck"). Blank = no corner.</summary>
        public static readonly StyledProperty<string?> SuperProperty =
            AvaloniaProperty.Register<FeatureCard, string?>(nameof(Super));

        public string? Super { get => GetValue(SuperProperty); set => SetValue(SuperProperty, value); }

        private SuperCorner? _superCorner;
        internal SuperCorner? SuperCornerControl => _superCorner;

        static FeatureCard() => SuperProperty.Changed.AddClassHandler<FeatureCard>((c, _) => c.ApplySuper());

        private void ApplySuper()
        {
            var root = this.FindControl<Grid>("ContentRoot");
            if (root is null) return;
            if (_superCorner != null) { root.Children.Remove(_superCorner); _superCorner = null; }
            _superCorner = SuperCorner.For(Super, bottomRight: false,
                () => DashboardDepth && DimWhenInactive && CoreSettings.Current.DashboardInvertClicks);
            if (_superCorner != null)
                root.Children.Insert(root.Children.IndexOf(this.FindControl<Border>("RimLight")!), _superCorner);
            if (this.FindControl<StackPanel>("BadgeColumn") is { } badges)
                badges.Margin = new Thickness(_superCorner != null ? SuperCorner.Leg + 2 : 8, 8, 0, 0);
        }

        /// <summary>WPF RouteClick: the on/off click on the corner flips the V2; the other click falls through.</summary>
        private bool PressedSuperCorner(PointerReleasedEventArgs e, bool toggle)
        {
            if (!toggle || _superCorner == null || e.Source is not Visual src) return false;
            if (src != _superCorner && !_superCorner.IsVisualAncestorOf(src)) return false;
            e.Handled = true;
            _superCorner.Press();
            return true;
        }
    }
}
