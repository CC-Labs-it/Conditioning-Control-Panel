using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private bool _bannerGlowsInitialized;

    private void InitializeBannerGlows()
    {
        if (_bannerGlowsInitialized) return;
        _bannerGlowsInitialized = true;
        Wire("TxtBannerSecondary", "BannerSecondaryGlow");
        Wire("TxtBannerPool", "BannerPoolGlow");

        void Wire(string textName, string imageName)
        {
            if (Named<TextBlock>(textName) is not { } text || Named<Image>(imageName) is not { } image) return;
            void Bake()
            {
                try
                {
                    var color = (text.Foreground as ISolidColorBrush)?.Color ?? Color.Parse("#FF69B4");
                    GlowBake.Swap(image, GlowBake.Text(text, new SolidColorBrush(color),
                        new DropShadowEffect { Color = color, BlurRadius = 8, Opacity = 0.6, OffsetX = 0, OffsetY = 0 }, 8), 8);
                }
                catch (Exception ex) { Log.Debug("Banner glow bake: {Error}", ex.Message); }
            }
            text.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBlock.TextProperty || e.Property == TextBlock.ForegroundProperty) Bake();
            };
            Closed += (_, _) => GlowBake.Swap(image, null, 8);
            Bake();
        }
    }
}
