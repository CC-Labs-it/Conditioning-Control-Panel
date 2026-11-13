using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Fyp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosRemoteStillsTests
{
    [Fact]
    public Task WashUsesConsentedRemotePosterAndReleasesCredit() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var poster = ChaosFlashOverlayTests.WriteGif(out var dir);
        var oldRoot = CorePaths.EffectiveAssetsProvider;
        var oldQuery = ScreenList.QueryOverride;
        var oldSchedule = ChaosFlashOverlay.Schedule;
        var (settings, fetch, download, consumer) = (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download, RemoteFlashSource.ClipConsumer);
        var s = CoreSettings.Current;
        var (source, consent, fypConsent) = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented);
        var steps = new List<Action>();
        try
        {
            CorePaths.EffectiveAssetsProvider = () => dir;
            ScreenList.QueryOverride = _ => new[] { MultiMonitorResultTests.MakeScreen(1, new PixelRect(0, 0, 1280, 720), new PixelRect(0, 0, 1280, 720), true) };
            ChaosFlashOverlay.SkipPlatformChecksForTests = true;
            ChaosFlashOverlay.Schedule = (action, _) => steps.Add(action);
            s.MediaSource = "online"; s.RemoteMediaConsented = true; s.FypOnlineConsented = false;
            RemoteFlashSource.CleanupCache();
            RemoteFlashSource.Settings = () => s;
            RemoteFlashSource.ClipConsumer = false;
            RemoteFlashSource.Fetch = _ => Task.FromResult((new List<FypAssetManifest.Entry> { new() { Id = "one", Url = "https://example.invalid/one.mp4", PosterUrl = "https://example.invalid/poster.jpg", Origin = "online", Type = "video" } }, (string?)null));
            RemoteFlashSource.Download = (_, _, _) => Task.FromResult<string?>(poster);
            RemoteFlashSource.EnsurePrefetch();
            await RemoteFlashSource.Prefetching;
            Assert.True(RemoteFlashSource.ReadyCount > 0);
            ChaosFlashOverlay.Show(new Border());
            await ChaosFlashOverlay.LastShow!;
            Assert.NotNull(Assert.Single(ChaosFlashOverlay.Shown).Spiral.Source);
            Assert.Equal(1, ScrolllerCreditChip.HolderCount);
            foreach (var step in steps.ToArray()) step();
            Assert.Equal(0, ScrolllerCreditChip.HolderCount);
            steps.Clear();
            ChaosFlashOverlay.Show(new Border());
            await ChaosFlashOverlay.LastShow!;
            Assert.Equal(1, ScrolllerCreditChip.HolderCount);
            s.MediaSource = "local";
            Assert.Null(ChaosFlashOverlay.Pick(Array.Empty<string>(), dir, s).Path);
            s.MediaSource = "online"; s.RemoteMediaConsented = false;
            Assert.Null(ChaosFlashOverlay.Pick(Array.Empty<string>(), dir, s).Path);
            s.RemoteMediaConsented = true;
            PanicSurfaces.StopAll("remote wash test");
            Assert.Equal(0, ScrolllerCreditChip.HolderCount);
            RemoteFlashSource.CleanupCache();
            Assert.Equal((poster, false), ChaosFlashOverlay.Pick(new[] { poster }, dir, s));
        }
        finally
        {
            ChaosFlashOverlay.CloseActive();
            RemoteFlashSource.CleanupCache();
            (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download, RemoteFlashSource.ClipConsumer) = (settings, fetch, download, consumer);
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = (source, consent, fypConsent);
            ChaosFlashOverlay.SkipPlatformChecksForTests = false;
            ChaosFlashOverlay.Schedule = oldSchedule;
            ScreenList.QueryOverride = oldQuery;
            CorePaths.EffectiveAssetsProvider = oldRoot;
            Directory.Delete(dir, true);
        }
    });
}
