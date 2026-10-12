using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// IMPORTED from trunk (CCP.Avalonia/Views/Features/CardGlow.cs; BreathClock dropped, the breath
    /// rides <see cref="Helpers.BeatLoop"/>). The on glow of a dashboard tile. WPF puts a
    /// DropShadowEffect on the tile root and breathes its Opacity (FeatureCard.xaml.cs:679-688); in
    /// Avalonia an Effect turns the whole tile into an offscreen layer re-blurred on every frame (P82).
    /// The glow is a sibling Border wearing a BoxShadow in FxGlowColor; only its Opacity moves.
    /// </summary>
    internal static class CardGlow
    {
        /// <summary>Default blur, the WPF DropShadowEffect BlurRadius.</summary>
        public const double BlurRadius = 18;

        public static BoxShadows Shadow(object? colour, double blur) =>
            new(new BoxShadow { Blur = blur, Color = colour is Color c ? c : Colors.HotPink });

        /// <summary>Keeps <paramref name="layer"/>'s shadow in the current mod's FxGlowColor at
        /// <paramref name="blur"/>; a new call replaces the old binding (performance tier change).</summary>
        public static void Bind(Border layer, double blur) =>
            layer.Bind(Border.BoxShadowProperty, layer.GetResourceObservable("FxGlowColor", c => Shadow(c, blur)));
    }
}
