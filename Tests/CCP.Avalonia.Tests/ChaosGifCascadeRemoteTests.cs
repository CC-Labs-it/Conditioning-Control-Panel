using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Fyp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosGifCascadeRemoteTests
{
    [Fact]
    public Task ShowStartsPrefetchOnlyForConsentedRemoteSources() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (settings, fetch) = (RemoteFlashSource.Settings, RemoteFlashSource.Fetch);
        var pick = ChaosGifCascadeOverlay.PickForTests;
        var s = CoreSettings.Current;
        var (source, consent, fypConsent) = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented);
        int calls = 0;
        try
        {
            RemoteFlashSource.CleanupCache();
            RemoteFlashSource.Settings = () => s;
            RemoteFlashSource.Fetch = _ => { calls++; return Task.FromResult((new List<FypAssetManifest.Entry>(), (string?)null)); };
            ChaosGifCascadeOverlay.PickForTests = (_, _) => new List<string>();
            s.MediaSource = "local"; s.RemoteMediaConsented = true; s.FypOnlineConsented = false;
            ChaosGifCascadeOverlay.Show(1, 1, 100, 1, 1);
            await ChaosGifCascadeOverlay.LastPick!;
            Assert.Equal(0, calls);
            s.MediaSource = "online"; s.RemoteMediaConsented = false;
            ChaosGifCascadeOverlay.Show(1, 1, 100, 1, 1);
            await ChaosGifCascadeOverlay.LastPick!;
            Assert.Equal(0, calls);
            s.RemoteMediaConsented = true;
            ChaosGifCascadeOverlay.Show(1, 1, 100, 1, 1);
            await RemoteFlashSource.Prefetching;
            Assert.True(calls > 0);
            await ChaosGifCascadeOverlay.LastPick!;
        }
        finally
        {
            ChaosGifCascadeOverlay.CloseActive();
            Dispatcher.UIThread.RunJobs();
            RemoteFlashSource.CleanupCache();
            (RemoteFlashSource.Settings, RemoteFlashSource.Fetch) = (settings, fetch);
            ChaosGifCascadeOverlay.PickForTests = pick;
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = (source, consent, fypConsent);
        }
    });

    [Fact]
    public Task RemoteBatchAndCreditLifecycle() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var poster = ChaosFlashOverlayTests.WriteGif(out var dir);
        var (settings, fetch, download, consumer) = (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download, RemoteFlashSource.ClipConsumer);
        var (skip, manual, creditSkip) = (ChaosGifCascadeOverlay.SkipPlatformChecksForTests, ChaosGifCascadeOverlay.ManualFramesForTests, ChaosFlashOverlay.SkipPlatformChecksForTests);
        var s = CoreSettings.Current;
        var (source, consent, fypConsent, ratio) = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.RemoteMediaRatio);
        var (root, pick, beforeDecode) = (CorePaths.EffectiveAssetsProvider, ChaosGifCascadeOverlay.PickForTests, ChaosGifCascadeOverlay.BeforeDecodeForTests);
        bool decodedOffUi = false;
        var packs = new PackMediaPool(ContentPackStore.ImageType, store: () => null);
        ChaosGifCascadeOverlay? rain = null;
        try
        {
            ChaosGifCascadeOverlay.CloseActive();
            RemoteFlashSource.CleanupCache();
            s.MediaSource = "online"; s.RemoteMediaConsented = true; s.FypOnlineConsented = false;
            CorePaths.EffectiveAssetsProvider = () => dir; // no images folder
            ChaosGifCascadeOverlay.PickForTests = ChaosGifCascadeOverlay.PickFiles;
            ChaosGifCascadeOverlay.BeforeDecodeForTests = _ => decodedOffUi = !Dispatcher.UIThread.CheckAccess();
            RemoteFlashSource.Settings = () => s;
            RemoteFlashSource.ClipConsumer = false;
            RemoteFlashSource.Fetch = _ => Task.FromResult((Enumerable.Range(0, 3).Select(i => new FypAssetManifest.Entry { Id = $"{i}", Url = $"https://example.invalid/{i}.mp4", PosterUrl = $"https://example.invalid/{i}.jpg", Origin = "online", Type = "video" }).ToList(), (string?)null));
            RemoteFlashSource.Download = (_, _, _) => Task.FromResult<string?>(poster);
            RemoteFlashSource.EnsurePrefetch();
            await RemoteFlashSource.Prefetching;
            Assert.Equal(3, RemoteFlashSource.ReadyCount);
            var remote = ChaosGifCascadeOverlay.PickFiles(8, Array.Empty<string>(), packs, s);
            Assert.Equal(3, remote.Count);
            Assert.Equal(3, remote.Distinct().Count());
            // A stale entry leaves the pool count temporarily high; recycled URLs must still be unique.
            File.Delete(RemoteFlashSource.TryTake(FlashOverlay.Rng)!.Value.PosterPath);
            remote = ChaosGifCascadeOverlay.PickFiles(8, Array.Empty<string>(), packs, s);
            Assert.Equal(2, remote.Count);
            Assert.Equal(2, remote.Distinct().Count());

            s.MediaSource = "mixed"; s.RemoteMediaRatio = 0;
            Assert.Equal(poster, Assert.Single(ChaosGifCascadeOverlay.PickFiles(1, new[] { poster }, packs, s)));
            s.RemoteMediaRatio = 100;
            Assert.NotEqual(poster, Assert.Single(ChaosGifCascadeOverlay.PickFiles(1, new[] { poster }, packs, s)));
            s.MediaSource = "local";
            Assert.Equal(poster, Assert.Single(ChaosGifCascadeOverlay.PickFiles(1, new[] { poster }, packs, s)));
            s.MediaSource = "online"; s.RemoteMediaConsented = false;
            Assert.Empty(ChaosGifCascadeOverlay.PickFiles(1, Array.Empty<string>(), packs, s));
            s.RemoteMediaConsented = true;

            ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
            ChaosGifCascadeOverlay.ManualFramesForTests = true;
            ChaosFlashOverlay.SkipPlatformChecksForTests = true;
            void Show() => ChaosGifCascadeOverlay.ShowWithFiles(remote, 1, 1, 100, 30, 1, 1);
            ChaosGifCascadeOverlay.Show(1, 1, 100, 30, 1);
            await ChaosGifCascadeOverlay.LastPick!;
            Dispatcher.UIThread.RunJobs();
            rain = ChaosGifCascadeOverlay.Active!;
            Assert.Equal(1, ScrolllerCreditChip.HolderCount);
            Assert.Equal(0, rain.Cascade.AnimatedAlive); // even a GIF poster is a still
            await ChaosGifCascadeOverlay.LastDecode!;
            Dispatcher.UIThread.RunJobs();
            Assert.True(decodedOffUi);
            Assert.NotNull(((global::Avalonia.Controls.Image)Assert.Single(rain.Surface.Children)).Source);
            for (int i = 0; i < 100 && rain.Cascade.IsRaining; i++) rain.Cascade.Step(0.1);
            Assert.Equal(0, ScrolllerCreditChip.HolderCount);
            Show();
            Assert.Equal(1, ScrolllerCreditChip.HolderCount);
            Show(); // restart releases the old faller and holds for the new one
            Assert.Equal(1, ScrolllerCreditChip.HolderCount);
            ChaosGifCascadeOverlay.ShowWithFiles(new() { poster }, 1, 1, 100, 30, 1, 1);
            Assert.Equal(0, ScrolllerCreditChip.HolderCount);
            Show();
            Assert.Equal(1, ScrolllerCreditChip.HolderCount);
            ChaosGifCascadeOverlay.CloseActive();
            Assert.Equal(0, ScrolllerCreditChip.HolderCount);
            await rain.PendingDecodes;
            Show();
            rain = ChaosGifCascadeOverlay.Active!;
            Assert.Equal(1, ScrolllerCreditChip.HolderCount);
            PanicSurfaces.All.Single(x => x.Id == "gif-cascade").Stop(null);
            Assert.Equal(0, ScrolllerCreditChip.HolderCount);
            await rain.PendingDecodes;
            await RemoteFlashSource.Prefetching;
            RemoteFlashSource.CleanupCache();
            Assert.Equal(poster, Assert.Single(ChaosGifCascadeOverlay.PickFiles(1, new[] { poster }, packs, s)));
            Assert.Empty(ChaosGifCascadeOverlay.PickFiles(1, Array.Empty<string>(), packs, s));
        }
        finally
        {
            ChaosGifCascadeOverlay.CloseActive();
            if (rain != null) await rain.PendingDecodes;
            RemoteFlashSource.CleanupCache();
            (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download, RemoteFlashSource.ClipConsumer) = (settings, fetch, download, consumer);
            (ChaosGifCascadeOverlay.SkipPlatformChecksForTests, ChaosGifCascadeOverlay.ManualFramesForTests, ChaosFlashOverlay.SkipPlatformChecksForTests) = (skip, manual, creditSkip);
            (CorePaths.EffectiveAssetsProvider, ChaosGifCascadeOverlay.PickForTests, ChaosGifCascadeOverlay.BeforeDecodeForTests) = (root, pick, beforeDecode);
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.RemoteMediaRatio) = (source, consent, fypConsent, ratio);
            Directory.Delete(dir, true);
        }
    });
}
