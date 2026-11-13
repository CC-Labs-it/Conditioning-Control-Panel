using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF BubbleService.cs:3551-3565 (hand-ported from #4599, restack b144): plain chaos bubbles
/// wear a glass highlight while the Skia FX layer is on; ambient bubbles and variant sprites never do.</summary>
public sealed class BubbleChaosShineTests
{
    private static uint PixelAt(Control layer, int x, int y)
    {
        layer.Measure(new Size(400, 300));
        layer.Arrange(new Rect(0, 0, 400, 300));
        using var image = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(400, 300));
        image.Render(layer);
        var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            image.CopyPixels(new PixelRect(x, y, 1, 1), buf, 4, 4);
            return unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(buf));
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
    }

    [Fact]
    public Task Shine_lights_plain_chaos_bubbles_only_with_skia_fx() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        bool oldFlag = s.ChaosSkiaFxEnabled;
        var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 400, 300), 1);
        try
        {
            var b = new AmbientBubble { Screen = 0, X = 100, Y = 100, Size = 100, Spec = new EffectBubbleSpec() };
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.Field.Bubbles.Add(b);
            var layer = (Control)w.Content!;
            uint Sample(bool on) { s.ChaosSkiaFxEnabled = on; layer.InvalidateVisual(); return PixelAt(layer, 134, 127); }

            uint off = Sample(false), on = Sample(true);
            Assert.True(((on >> 16) & 255) > ((off >> 16) & 255) && (on & 255) > (off & 255),
                $"plain chaos bubble: off {off:X8}, on {on:X8}");
            Assert.True(BubbleOverlayWindow.HasChaosShine(b, variantSprite: false));
            Assert.False(BubbleOverlayWindow.HasChaosShine(b, variantSprite: true));   // a Tease face brings its own art

            b.Spec = null;   // ambient bubble: bubble.png already has its highlights
            Assert.Equal(Sample(false), Sample(true));
            Assert.False(BubbleOverlayWindow.HasChaosShine(b, variantSprite: false));
        }
        finally
        {
            BubbleOverlay.Field.Bubbles.Clear();
            w.Close();
            s.ChaosSkiaFxEnabled = oldFlag;
        }
        return Task.CompletedTask;
    });
}
