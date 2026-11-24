using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Services.Haptics.Core;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FlashCuesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task FlashTestsPreserveFullLuminanceCache(int scenario)
    {
        var cache = (Dictionary<string, double>)typeof(FlashOverlay).GetField("LuminanceCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        var original = cache.ToArray();
        var oldService = CoreHaptics.Service;
        using var service = new HapticService(new HapticSettings { LuminanceSyncEnabled = true, LuminanceSyncIntensity = 0.6 });
        try
        {
            cache.Clear();
            for (int i = 0; i < 500; i++) cache.Add($"sentinel-{i}", i / 500.0);
            var expected = cache.ToArray();
            CoreHaptics.Service = service;
            switch (scenario)
            {
                case 0: await ShownPicturesDriveLuminanceOnlyWhenEnabled(); break;
                case 1: await BurstSuppressionReachesScheduledFlashes(false); break;
                case 2: await new JackpotRemixFlowTests().OwnedToggleAndCachedRemixReachAnimatedFlashAndCleanUp(); break;
                case 3: await new JackpotRemixFlowTests().RemixLuminanceFollowsSuccessfulSpawns(false, true, false, 2); break;
                case 4:
                    Assert.Throws<InvalidOperationException>((Action)(() =>
                    {
                        using var isolation = new FlashLuminanceCacheIsolation();
                        cache["temporary"] = 1;
                        throw new InvalidOperationException();
                    }));
                    break;
            }
            Assert.Equal(expected.OrderBy(x => x.Key), cache.OrderBy(x => x.Key));
        }
        finally
        {
            CoreHaptics.Service = oldService;
            cache.Clear();
            foreach (var pair in original) cache.Add(pair.Key, pair.Value);
        }
    }

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
        using var cache = new FlashLuminanceCacheIsolation();
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task BurstSuppressionReachesScheduledFlashes(bool suppress) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var cache = new FlashLuminanceCacheIsolation();
        var root = Directory.CreateTempSubdirectory("ccp-flash-suppression-").FullName;
        var images = Directory.CreateDirectory(Path.Combine(root, "images")).FullName;
        using (var picture = Picture(255)) picture.Save(Path.Combine(images, "white.png"));
        var settings = new HapticSettings { Enabled = true, FlashDisplayEnabled = true,
            LuminanceSyncEnabled = true, LuminanceSyncIntensity = 0.6 };
        settings.EnsureV2Migrated();
        settings.V2.Provider("lovense").Enabled = false;
        settings.V2.Provider("mock").Enabled = true;
        settings.V2.Rule(HapticEventKind.FlashDecay).Enabled = true;
        using var service = new HapticService(settings);
        var luminance = new List<double>();
        var haptics = new List<string>();
        service.HapticTriggered += (_, label) => haptics.Add(label);
        var saved = (CoreHaptics.Service, FlashOverlay.SetLuminance, FlashOverlay.Present,
            FlashOverlay.OverlaysAvailable, FlashOverlay.After, FlashOverlay.VoiceLead,
            CorePaths.EffectiveAssetsProvider, ScreenList.QueryOverride, CoreSettings.Current.FlashAudioEnabled);
        var host = new Window();
        var scheduled = new List<Action>();
        var announcements = 0;
        void Announced() => announcements++;
        FlashOverlay.AboutToDisplay += Announced;
        try
        {
            CoreHaptics.Service = service;
            FlashOverlay.SetLuminance = (value, _) => luminance.Add(value);
            FlashOverlay.Present = (_, _, _) => true;
            FlashOverlay.OverlaysAvailable = () => true;
            FlashOverlay.After = (action, _) => scheduled.Add(action);
            FlashOverlay.VoiceLead = () => Task.CompletedTask;
            CorePaths.EffectiveAssetsProvider = () => root;
            ScreenList.QueryOverride = _ => host.Screens.All.ToArray();
            CoreSettings.Current.FlashAudioEnabled = false;
            FlashOverlay.TriggerOnce(host, amount: 1, durationMs: 1600, suppressHaptic: suppress);
            for (int i = 0; i < 250 && scheduled.Count == 0; i++) await Task.Delay(20);
            Assert.Single(scheduled);
            Assert.Equal(1, announcements);
            scheduled[0]();
            Assert.Single(FlashOverlay.Active);
            Assert.Equal(suppress ? 0 : 1, luminance.Count);
            Assert.Equal(suppress ? 0 : 1, haptics.Count(label => label.StartsWith("Flash", StringComparison.Ordinal)));
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            FlashOverlay.AboutToDisplay -= Announced;
            host.Close();
            (CoreHaptics.Service, FlashOverlay.SetLuminance, FlashOverlay.Present,
                FlashOverlay.OverlaysAvailable, FlashOverlay.After, FlashOverlay.VoiceLead,
                CorePaths.EffectiveAssetsProvider, ScreenList.QueryOverride, CoreSettings.Current.FlashAudioEnabled) = saved;
            Directory.Delete(root, recursive: true);
        }
    });

}

// These tests run serially, and the overlay cache is process-wide.
internal sealed class FlashLuminanceCacheIsolation : IDisposable
{
    private readonly Dictionary<string, double> cache = (Dictionary<string, double>)typeof(FlashOverlay)
        .GetField("LuminanceCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    private readonly KeyValuePair<string, double>[] saved;

    public FlashLuminanceCacheIsolation()
    {
        saved = cache.ToArray();
        cache.Clear();
    }

    public void Dispose()
    {
        cache.Clear();
        foreach (var pair in saved) cache.Add(pair.Key, pair.Value);
    }
}
