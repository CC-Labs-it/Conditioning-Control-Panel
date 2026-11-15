using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FlashCuesTests
{
    private static Bitmap Picture(byte value)
    {
        var pixels = new byte[8 * 8 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        { pixels[i] = pixels[i + 1] = pixels[i + 2] = value; pixels[i + 3] = 255; }
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pin.AddrOfPinnedObject(), new PixelSize(8, 8), new Vector(96, 96), 32); }
        finally { pin.Free(); }
    }

    [Fact]
    public Task ShownPicturesDriveLuminanceOnlyWhenEnabled() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var settings = new HapticSettings { LuminanceSyncEnabled = true, LuminanceSyncIntensity = 0.6 };
        using var service = new HapticService(settings);
        var oldService = CoreHaptics.Service;
        var oldSet = FlashOverlay.SetLuminance;
        var oldPresent = FlashOverlay.Present;
        var calls = new List<(double, int)>();
        try
        {
            CoreHaptics.Service = service;
            FlashOverlay.SetLuminance = (value, ms) => calls.Add((value, ms));
            FlashOverlay.Present = (_, _, _) => true;
            var screen = new Window().Screens.All[0];
            bool Show(byte color, string path, int ms) => FlashOverlay.Show(Picture(color), new PixelRect(0, 0, 80, 80), path, screen, 1, TimeSpan.Zero, TimeSpan.FromMilliseconds(ms), false, false, 0);
            var key = Guid.NewGuid().ToString();
            Assert.True(Show(255, key, 50));
            Assert.Equal(0.6, calls[0].Item1, 6);
            Assert.Equal(100, calls[0].Item2);
            Assert.True(Show(0, key, 40000)); // cached per file
            Assert.Equal(0.6, calls[1].Item1, 6);
            Assert.Equal(30000, calls[1].Item2);
            Assert.True(Show(0, key + "black", 1234));
            Assert.Equal(0, calls[2].Item1, 6);
            Assert.Equal(1234, calls[2].Item2);
            settings.LuminanceSyncEnabled = false;
            Assert.True(Show(255, key + "off", 500));
            Assert.Equal(3, calls.Count);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            CoreHaptics.Service = oldService;
            FlashOverlay.SetLuminance = oldSet;
            FlashOverlay.Present = oldPresent;
        }
        return Task.CompletedTask;
    });
}
