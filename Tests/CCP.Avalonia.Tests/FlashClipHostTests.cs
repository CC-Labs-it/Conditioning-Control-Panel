using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Fyp;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class FlashClipHostTests
{
    [Theory]
    [InlineData(1920, 1080, 480, 270)]
    [InlineData(100, 200, 100, 200)]
    [InlineData(0, 0, 2, 2)]
    public void DecodeIsBounded(int w, int h, int expectedW, int expectedH)
        => Assert.Equal((expectedW, expectedH), FlashClipPlayer.DecodeSize(w, h));

    [Theory]
    [InlineData(.1, .25)]
    [InlineData(1, 1)]
    [InlineData(8, 4)]
    public void PlaybackRateMatchesWpf(double input, float expected)
        => Assert.Equal(expected, FlashClipPlayer.Rate(input));

    [Fact]
    public void PanicInvalidatesPendingFlashBurst()
    {
        var generation = FlashOverlay.Generation;
        PanicSurfaces.All.Single(s => s.Id == "flash-clips").Stop(null);
        Assert.Equal(generation + 1, FlashOverlay.Generation);
    }

    [Fact]
    public void MissingClipNeverConsumesAdmission()
    {
        var before = FlashClipPlayer.Budget.Live;
        Assert.Null(FlashClipPlayer.TryStart(null, "missing.mp4", 100, 100, _ => { }, 1, out var capped));
        Assert.False(capped);
        Assert.Equal(before, FlashClipPlayer.Budget.Live);
    }

    /// <summary>The user path of a remote burst (P04): warm pool -> LoadPictures draws the remote
    /// poster -> Show starts that clip. A Stop keeps the warm pool (WPF FlashService.cs:3846-3848);
    /// panic stops every clip lease and drops the pool.</summary>
    [Fact]
    public Task RemoteBurstStartsItsClipStopKeepsThePoolAndPanicStopsEverything() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-flash-clip-host-").FullName;
        var (settings, fetch, download) = (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download);
        var (present, chime, xp) = (FlashOverlay.Present, FlashOverlay.LuckyChime, CoreProgression.AddXPProvider);
        var s = new AppSettings { MediaSource = "online", RemoteMediaConsented = true };
        var entry = new FypAssetManifest.Entry
        {
            Id = "host", Url = "https://example.invalid/host.mp4", PosterUrl = "https://example.invalid/host.png",
            Origin = "online", Type = "video"
        };
        var poster = Path.Combine(dir, "host.png");
        var clip = Path.Combine(dir, "host.mp4");
        try
        {
            using (var src = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")))
            using (var dst = File.Create(poster)) src.CopyTo(dst);
            File.WriteAllBytes(clip, new byte[] { 1, 2, 3 });
            RemoteFlashSource.CleanupCache();
            RemoteFlashSource.Settings = () => s;
            RemoteFlashSource.Fetch = _ => Task.FromResult((new List<FypAssetManifest.Entry> { entry }, (string?)null));
            RemoteFlashSource.Download = (url, _, _) => Task.FromResult<string?>(url == entry.PosterUrl ? poster : clip);
            FlashOverlay.Present = (_, _, _) => true;
            FlashOverlay.LuckyChime = _ => { };
            CoreProgression.AddXPProvider = (_, _) => { };

            RemoteFlashSource.EnsurePrefetch();
            await RemoteFlashSource.Prefetching;
            var screen = new Window().Screens.All[0];
            var picked = FlashOverlay.LoadPictures(1, new[] { screen }, new[] { 0 }, s, new List<PixelRect>(), null);
            var (bmp, rect, path, _) = Assert.Single(picked);
            Assert.Equal(entry.Url, path);
            Assert.True(FlashOverlay.Show(bmp, rect, path, screen, 1, TimeSpan.Zero, TimeSpan.FromSeconds(6),
                clickable: false, ambient: false, hydraGeneration: 0));
            Assert.Equal(clip, FlashOverlay.Active[^1].Window.ClipSource);

            FlashOverlay.CloseAll(final: false);   // the engine Stop button
            Assert.Equal(clip, RemoteFlashSource.ClipPath(entry.Url));

            int stopped = 0;
            var lease = FlashClipPlayer.Budget.TryAcquire(() => stopped++);
            Assert.NotNull(lease);
            PanicSurfaces.All.Single(x => x.Id == "flash-clips").Stop(null);
            Assert.Equal(1, stopped);
            Assert.Equal(0, FlashClipPlayer.Budget.Live);
            Assert.Null(RemoteFlashSource.ClipPath(entry.Url));
            Assert.Equal(0, RemoteFlashSource.ReadyCount);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            FlashClipPlayer.StopAll();
            RemoteFlashSource.CleanupCache();
            (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download) = (settings, fetch, download);
            (FlashOverlay.Present, FlashOverlay.LuckyChime, CoreProgression.AddXPProvider) = (present, chime, xp);
            Directory.Delete(dir, recursive: true);
        }
    });
}
