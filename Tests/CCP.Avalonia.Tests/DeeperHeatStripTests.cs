using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public class DeeperHeatStripTests
{
    [Fact]
    public void StripDrawsTrackAndHeatAndDoesNotAcceptInput() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var strip = new DeeperHeatStrip { Fill = Brushes.Red, Values = new[] { 1.0, 0.0 } };
        Assert.False(strip.IsHitTestVisible);
        strip.Measure(new Size(100, 20));
        strip.Arrange(new Rect(0, 0, 100, 20));
        byte[] Render()
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(100, 20));
            bitmap.Render(strip);
            var pixels = new byte[100 * 20 * 4];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(0, 0, 100, 20), handle.AddrOfPinnedObject(), pixels.Length, 400); }
            finally { handle.Free(); }
            return pixels;
        }
        var hot = Render();
        strip.Values = null;
        var empty = Render();
        int hotPixel = (10 * 100 + 25) * 4;
        int quietPixel = (10 * 100 + 75) * 4;
        Assert.True(hot[hotPixel + 3] > empty[hotPixel + 3]);
        Assert.Equal(empty[quietPixel + 3], hot[quietPixel + 3]);
        Assert.True(empty[quietPixel + 3] > 0);
    });
}
