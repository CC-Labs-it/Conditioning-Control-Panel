using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using ConditioningControlPanel.Models;
using LibVLCSharp.Shared;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
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
    private sealed class OnDispose(Action action) : IDisposable { public void Dispose() => action(); }
    private sealed class FrozenClock : TimeProvider { public override long GetTimestamp() => 1; }
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
            Assert.Equal(8, starts); // Rebinding the beat must not start a player during CloseAll.
            Assert.Contains(BeatLoop.Running, loop => loop.Owner == windows[1]);
            BeatLoop.StepAll();
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
        Assert.DoesNotContain(BeatLoop.Running, loop => windows.Any(w => ReferenceEquals(w, loop.Owner)));
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
        var clip = Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4");
        Assert.True(File.Exists(clip));
        LibVLC? isolated = null;
        try { LibVlcAudio.InitializeNative(); isolated = new LibVLC("--aout=dummy"); }
        catch (Exception e) { Assert.Skip("LibVLC unavailable: " + e.Message); }
        using var engine = isolated!;
        var shared = typeof(LibVlcAudio).GetProperty("Shared", BindingFlags.Static | BindingFlags.NonPublic)!;
        var options = typeof(LibVlcAudio).GetField("_sharedOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = shared.GetValue(null);
        var previousOptions = options.GetValue(null);
        var factory = FlashClipPlayer.Create;
        shared.SetValue(null, null);
        try
        {
            FlashClipPlayer.Create = (path, width, speed, show) => new FlashClipPlayer.NativePlayer(engine, path, width, speed, show);
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
                Assert.Equal(0, FlashClipPlayer.TearingDown);
            });
            Assert.Null(shared.GetValue(null));
            Assert.Same(previousOptions, options.GetValue(null));
        }
        finally
        {
            FlashClipPlayer.Create = factory;
            shared.SetValue(null, previous);
            options.SetValue(null, previousOptions);
        }
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

    [Fact]
    public Task ShatterKeepsAStableCopyAfterVideoFrameChangesAndIsDisposed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var frame = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var pixels = frame.Lock()) Marshal.WriteInt32(pixels.Address, unchecked((int)0xff123456));
        var w = new FlashOverlayWindow(Picture()) { Width = 160, Height = 160 };
        w.Show(); w.SetPicture(frame);
        WriteableBitmap? snapshot = null;
        try
        {
            Assert.True(w.TryBeginShatter(MotionLevel.Full, new Random(3)));
            var shards = ((Panel)w.Content!).Children.OfType<Panel>().Last();
            var copies = shards.Children.Cast<Border>().Select(b => (WriteableBitmap)((Image)b.Child!).Source!).ToArray();
            Assert.NotEmpty(copies);
            snapshot = copies[0];
            Assert.All(copies, copy => Assert.NotSame(frame, copy));
            Assert.All(copies, copy => Assert.Same(copies[0], copy));
            using (var pixels = frame.Lock()) Marshal.WriteInt32(pixels.Address, unchecked((int)0xffabcdef));
            frame.Dispose();
            using (var frozen = copies[0].Lock())
                Assert.Equal(unchecked((int)0xff123456), Marshal.ReadInt32(frozen.Address));
        }
        finally { w.Close(); }
        Assert.Throws<ObjectDisposedException>(() => { _ = snapshot!.PixelSize; });
        return Task.CompletedTask;
    });

    [Fact]
    public Task GlitchKeepsFrameWhenItsPlayerIsEvicted() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldFactory = FlashClipPlayer.Create;
        var oldClock = FlashOverlay.Clock;
        var file = Path.GetTempFileName();
        var windows = new List<FlashOverlayWindow>();
        using var frame = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var pixels = frame.Lock()) Marshal.WriteInt32(pixels.Address, unchecked((int)0xff123456));
        var freed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WriteableBitmap? snapshot = null;
        var starts = 0;
        try
        {
            FlashOverlay.Clock = new FrozenClock();
            FlashClipPlayer.Create = (_, _, _, show) =>
            {
                if (++starts != 1) return new Fake();
                show(frame);
                return new OnDispose(() => Dispatcher.UIThread.Post(() => { frame.Dispose(); freed.TrySetResult(); }));
            };
            for (var i = 0; i < 8; i++)
            {
                var poster = Picture();
                var w = new FlashOverlayWindow(poster) { Width = 160, Height = 160 };
                windows.Add(w); w.Show(); FlashOverlay.StartClip(w, poster, file, 100, 1);
            }
            var first = windows[0];
            first.BeginExit(FlashExit.Begin(FlashExitStyle.Glitch, MotionLevel.Full, 3));
            var slices = first.Slices!.Children.Cast<Image>().Select(i => (WriteableBitmap)i.Source!).ToArray();
            Assert.Equal(7, slices.Length);
            snapshot = slices[0];
            Assert.All(slices, slice => { Assert.NotSame(frame, slice); Assert.Same(snapshot, slice); });
            using (var pixels = frame.Lock()) Marshal.WriteInt32(pixels.Address, unchecked((int)0xffabcdef));
            var waitingPoster = Picture();
            var waiting = new FlashOverlayWindow(waitingPoster) { Width = 160, Height = 160 };
            windows.Add(waiting); waiting.Show(); FlashOverlay.StartClip(waiting, waitingPoster, file, 100, 1);
            Assert.Equal(9, starts);
            await freed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<ObjectDisposedException>(() => { _ = frame.PixelSize; });
            using (var frozen = snapshot.Lock()) Assert.Equal(unchecked((int)0xff123456), Marshal.ReadInt32(frozen.Address));
            first.Close();
            Assert.Throws<ObjectDisposedException>(() => { _ = snapshot.PixelSize; });
        }
        finally
        {
            foreach (var w in windows) w.Close();
            FlashClipPlayer.Create = oldFactory; FlashOverlay.Clock = oldClock;
            File.Delete(file);
            for (var i = 0; i < 500 && FlashClipPlayer.TearingDown != 0; i++) await Task.Delay(10);
        }
        Assert.Equal(0, FlashClipPlayer.Budget.Live);
    });

}
