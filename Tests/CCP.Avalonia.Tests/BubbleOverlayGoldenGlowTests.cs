using System;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleOverlayGoldenGlowTests
{
    [Fact]
    public void GoldenSpawnUsesGoldGlowUnlessPerformanceDisablesIt() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var saved = (settings.PerformanceMode, settings.AutoPerformanceMode);
        try
        {
            settings.AutoPerformanceMode = false;
            settings.PerformanceMode = false;
            var golden = BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildGolden(), new Random(1));
            var effect = BubbleOverlayWindow.GlowEffect(golden, 1);
            Assert.NotNull(effect);
            Assert.Equal(20, effect.BlurRadius);
            Assert.Equal(Color.FromRgb(255, 215, 0), effect.Color);
            Assert.Equal(0.55, effect.Opacity);
            var plain = BubbleOverlay.SpawnChaos(new EffectBubbleSpec(), new Random(1));
            Assert.Null(BubbleOverlayWindow.GlowEffect(plain, 1));
            settings.PerformanceMode = true;
            var low = BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildGolden(), new Random(1));
            Assert.Null(BubbleOverlayWindow.GlowEffect(low, 1));
        }
        finally
        {
            (settings.PerformanceMode, settings.AutoPerformanceMode) = saved;
            BubbleOverlay.Stop();
        }
    });
}
