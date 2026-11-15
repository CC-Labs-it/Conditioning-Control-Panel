using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashClipPlayerTests
{
    private sealed class Fake : IDisposable { public void Dispose() { } }
    private sealed class Blocked(ManualResetEventSlim gate) : IDisposable { public void Dispose() => gate.Wait(); }
    private static Bitmap Picture() => new(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")));

    [Fact]
    public Task RefusedFlashUpgradesAfterCloseAndFadingFlashCanBeEvicted() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var old = FlashClipPlayer.Create;
        var file = Path.GetTempFileName();
        var windows = new List<FlashOverlayWindow>();
        var starts = 0;
        using var frame = Picture();
        try
        {
            FlashClipPlayer.Create = (_, _, _, show) => { starts++; show(frame); return new Fake(); };
            for (var i = 0; i < 9; i++)
            {
                var poster = Picture();
                var w = new FlashOverlayWindow(poster);
                windows.Add(w); w.Show();
                FlashOverlay.StartClip(w, poster, file, 100, 1);
            }
            Assert.Equal(8, starts);
            Assert.Equal(8, FlashClipPlayer.Budget.Live);
            Assert.Same(frame, windows[0].Picture);
            Assert.NotSame(frame, windows[8].Picture);
            windows[0].Close();
            FlashOverlay.ClipTick();
            Assert.Equal(9, starts);
            Assert.Same(frame, windows[8].Picture);
            Assert.Equal(8, FlashClipPlayer.Budget.Live);
            windows[1].Run(1, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));
            windows[1].Expire();
            FlashOverlay.ClipTick();
            var poster10 = Picture();
            var w10 = new FlashOverlayWindow(poster10); windows.Add(w10); w10.Show();
            FlashOverlay.StartClip(w10, poster10, file, 100, 1);
            Assert.Equal(10, starts);
            Assert.NotSame(frame, windows[1].Picture);
            Assert.Same(frame, w10.Picture);
            Assert.Equal(8, FlashClipPlayer.Budget.Live);
        }
        finally
        {
            foreach (var w in windows) w.Close();
            FlashClipPlayer.Create = old;
            File.Delete(file);
            for (var i = 0; i < 200 && FlashClipPlayer.TearingDown != 0; i++) await Task.Delay(10);
        }
        Assert.Equal(0, FlashClipPlayer.Budget.Live);
    });

    [Fact]
    public Task NoClipDoesNotStartPlayer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = FlashClipPlayer.Create;
        var starts = 0;
        var poster = Picture();
        var w = new FlashOverlayWindow(poster); w.Show();
        try
        {
            FlashClipPlayer.Create = (_, _, _, _) => { starts++; return new Fake(); };
            FlashOverlay.StartClip(w, poster, null, 100, 1);
            Assert.Equal(0, starts);
            Assert.Same(poster, w.Picture);
        }
        finally { w.Close(); FlashClipPlayer.Create = old; }
        return Task.CompletedTask;
    });
    [Fact]
    public async Task NativePlayerDecodesIntoTheWindow()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-flash-native-").FullName;
        var clip = Path.Combine(dir, "clip.mp4");
        try
        {
            using (var ff = Process.Start(new ProcessStartInfo("ffmpeg", $"-v error -f lavfi -i testsrc=d=1:s=64x48:r=10 -pix_fmt yuv420p \"{clip}\"") { UseShellExecute = false })!)
                await ff.WaitForExitAsync();
            Assert.True(File.Exists(clip));
            try { if (LibVlcAudio.Shared == null) _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e) { Assert.Skip("LibVLC unavailable: " + e.Message); }
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                var poster = Picture();
                var w = new FlashOverlayWindow(poster); w.Show();
                try
                {
                    FlashOverlay.StartClip(w, poster, clip, 64, 1);
                    for (var i = 0; i < 500 && ReferenceEquals(w.Picture, poster); i++) await Task.Delay(10);
                    Assert.IsType<WriteableBitmap>(w.Picture);
                }
                finally
                {
                    w.Close();
                    for (var i = 0; i < 500 && FlashClipPlayer.TearingDown != 0; i++) await Task.Delay(10);
                }
                Assert.Equal(0, FlashClipPlayer.Budget.Live);
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task CloseAllAndStopReleaseLeases(int stop) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var old = FlashClipPlayer.Create;
        var oldStop = CoreFlash.StopProvider;
        var oldStart = CoreFlash.StartProvider;
        var oldShow = CoreFlash.ShowProvider;
        var oldAmbient = CoreFlash.AmbientShowProvider;
        var oldBusy = CoreFlash.IsBusyProvider;
        var oldConsumer = RemoteFlashSource.ClipConsumer;
        var file = Path.GetTempFileName();
        var w = new FlashOverlayWindow(Picture());
        try
        {
            FlashClipPlayer.Create = (_, _, _, _) => new Fake();
            FlashOverlay.SeedCore(() => null, () => { });
            Assert.True(RemoteFlashSource.ClipConsumer);
            w.Show();
            FlashOverlay.Active.Add((w, new PixelRect(0, 0, 100, 100)));
            FlashOverlay.StartClip(w, (Bitmap)w.Picture!, file, 100, 1);
            Assert.Equal(1, FlashClipPlayer.Budget.Live);
            if (stop == 1) CoreFlash.StopProvider!();
            else if (stop == 2) PanicSurfaces.All.Single(s => s.Id == "flash").Stop(null);
            else FlashOverlay.CloseAll(final: false);
            Assert.Equal(0, FlashClipPlayer.Budget.Live);
        }
        finally
        {
            FlashOverlay.Active.RemoveAll(x => x.Window == w);
            w.Close();
            FlashClipPlayer.Create = old;
            CoreFlash.StopProvider = oldStop; CoreFlash.StartProvider = oldStart;
            CoreFlash.ShowProvider = oldShow; CoreFlash.AmbientShowProvider = oldAmbient; CoreFlash.IsBusyProvider = oldBusy;
            RemoteFlashSource.ClipConsumer = oldConsumer;
            File.Delete(file);
            for (var i = 0; i < 200 && FlashClipPlayer.TearingDown != 0; i++) await Task.Delay(10);
        }
    });

    [Fact]
    public Task RemotePrefetchLoadsPosterAndStartsItsClip() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-remote-play-").FullName;
        var posterPath = Path.Combine(dir, "poster.png");
        var clipPath = Path.Combine(dir, "clip.mp4");
        using (var image = Picture()) image.Save(posterPath);
        File.WriteAllBytes(clipPath, new byte[] { 1, 2, 3 });
        var s = ConditioningControlPanel.CoreSettings.Current;
        var oldSource = s.MediaSource;
        var oldConsent = s.RemoteMediaConsented;
        var oldRoot = CorePaths.EffectiveAssetsProvider;
        var oldPresent = FlashOverlay.Present;
        var oldFactory = FlashClipPlayer.Create;
        var oldSettings = RemoteFlashSource.Settings;
        var oldFetch = RemoteFlashSource.Fetch;
        var oldDownload = RemoteFlashSource.Download;
        var oldConsumer = RemoteFlashSource.ClipConsumer;
        var probe = new global::Avalonia.Controls.Window();
        var screen = probe.Screens.All[0];
        using var frame = Picture();
        try
        {
            RemoteFlashSource.CleanupCache();
            s.MediaSource = "online"; s.RemoteMediaConsented = true;
            CorePaths.EffectiveAssetsProvider = () => dir;
            RemoteFlashSource.Settings = () => s;
            RemoteFlashSource.ClipConsumer = true;
            RemoteFlashSource.Fetch = _ => Task.FromResult((new List<FypAssetManifest.Entry> { new()
            { Id = "one", Url = "https://example.invalid/clip.mp4", PosterUrl = "https://example.invalid/poster.png", Origin = "online", Type = "video" } }, (string?)null));
            RemoteFlashSource.Download = (url, _, _) => Task.FromResult<string?>(url.EndsWith(".mp4") ? clipPath : posterPath);
            RemoteFlashSource.EnsurePrefetch();
            await RemoteFlashSource.Prefetching;
            var owned = RemoteFlashSource.ClipPath("https://example.invalid/clip.mp4");
            Assert.NotNull(owned);
            Assert.True(File.Exists(owned));
            var load = typeof(FlashOverlay).GetMethod("LoadPictures", BindingFlags.NonPublic | BindingFlags.Static)!;
            var pictures = (List<(Bitmap Bitmap, PixelRect Rect, string Path, Screen Screen)>)load.Invoke(null,
                new object?[] { 1, new[] { screen }, new[] { 0 }, s, new List<PixelRect>(), null })!;
            var picture = Assert.Single(pictures);
            var starts = 0;
            FlashOverlay.Present = (_, _, _) => true;
            FlashClipPlayer.Create = (path, _, _, show) => { Assert.Equal(owned, path); starts++; show(frame); return new Fake(); };
            Assert.True(FlashOverlay.Show(picture.Bitmap, picture.Rect, picture.Path, picture.Screen, 1, TimeSpan.Zero,
                TimeSpan.FromSeconds(10), false, false, 0));
            Assert.Equal(1, starts);
            Assert.Same(frame, FlashOverlay.Active[^1].Window.Picture);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            RemoteFlashSource.CleanupCache();
            (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download, RemoteFlashSource.ClipConsumer) =
                (oldSettings, oldFetch, oldDownload, oldConsumer);
            CorePaths.EffectiveAssetsProvider = oldRoot;
            FlashOverlay.Present = oldPresent; FlashClipPlayer.Create = oldFactory;
            s.MediaSource = oldSource; s.RemoteMediaConsented = oldConsent;
            probe.Close();
            for (var i = 0; i < 200 && FlashClipPlayer.TearingDown != 0; i++) await Task.Delay(10);
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public Task PendingTeardownsRefuseStartsUntilTheyDrain() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var old = FlashClipPlayer.Create;
        var file = Path.GetTempFileName();
        using var gate = new ManualResetEventSlim();
        var players = new List<FlashClipPlayer>();
        FlashClipPlayer? extra = null;
        try
        {
            FlashClipPlayer.Create = (_, _, _, _) => new Blocked(gate);
            for (var i = 0; i < 8; i++)
            {
                var player = FlashClipPlayer.TryStart(file, 100, 1, _ => { }, () => { }, out var refused);
                Assert.False(refused); Assert.NotNull(player); players.Add(player);
            }
            foreach (var player in players) player.Dispose();
            Assert.Equal(0, FlashClipPlayer.Budget.Live);
            Assert.Equal(8, FlashClipPlayer.TearingDown);
            Assert.False(FlashClipPlayer.CanStartFlash);
            extra = FlashClipPlayer.TryStart(file, 100, 1, _ => { }, () => { }, out var atCap);
            Assert.True(atCap); Assert.Null(extra);
        }
        finally
        {
            extra?.Dispose();
            foreach (var player in players) player.Dispose();
            gate.Set();
            for (var i = 0; i < 500 && FlashClipPlayer.TearingDown != 0; i++) await Task.Delay(10);
            FlashClipPlayer.Create = old;
            File.Delete(file);
        }
        Assert.Equal(0, FlashClipPlayer.TearingDown);
        Assert.True(FlashClipPlayer.CanStartFlash);
    });

}
