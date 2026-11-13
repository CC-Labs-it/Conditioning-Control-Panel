using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF App.xaml.cs:3218: FunScriptService follows the video playhead. Here the mandatory
/// video overlay's LibVLC TimeChanged is that playhead.</summary>
public sealed class FunScriptPlayheadTests
{
    [Fact]
    public void The_overlay_playhead_reaches_a_subscriber_until_it_unsubscribes()
    {
        long seen = -1;
        var unsubscribe = MandatoryVideoOverlay.SubscribePlaybackTime(ms => seen = ms);
        Assert.NotNull(unsubscribe);   // null would tell FunScriptService "no video service"
        try
        {
            MandatoryVideoOverlay.Instance.RaisePlaybackTime(1234);
            Assert.Equal(1234, seen);
        }
        finally { unsubscribe!(); }
        MandatoryVideoOverlay.Instance.RaisePlaybackTime(5678);
        Assert.Equal(1234, seen);
    }

    [Fact]
    public void Startup_seeds_the_seam_and_the_player_feeds_it()
    {
        Assert.Matches(@"FunScriptService\.SubscribePlaybackTime\s*=\s*[\w.]*MandatoryVideoOverlay\.SubscribePlaybackTime;",
            Src("App.axaml.cs"));
        Assert.Matches(@"_player\.TimeChanged \+=[^}]*?RaisePlaybackTime\(e\.Time\)",
            Src("Views", "Overlays", "MandatoryVideoOverlay.cs"));
    }

    [Fact]
    public void The_welcome_back_sheet_flushes_a_parked_media_offer_when_it_closes()
    {
        // WPF parks the offer behind every startup modal (App.xaml.cs:909-914); each one flushes on close.
        // The sheet is a startup-ladder modal (#4281): the ladder clears the flag and flushes after every modal.
        // Src blanks string literals and comments, so the modal key reads as a space here.
        // #4602 hands the ladder the whole choose-and-apply job through the RunModal seam.
        var offer = Src("Platform", "WelcomeBackOffer.cs");
        Assert.Matches(@"RunModal\s*=\s*QueueOnLadder;", offer);
        Assert.Matches(@"Task QueueOnLadder\(Func<Task> show\)\s*\{\s*StartupLadder\.EnqueueModal\(\s*,\s*\d+,\s*show\);", offer);
        Assert.Matches(@"else await RunModal\(async \(\) =>", offer);
        Assert.Matches(@"finally\s*\{\s*_modalUp = false;\s*Views\.Windows\.MainShellWindow\.IsStartupDialogShowing = false;\s*RemoteMediaOffer\.FlushPending\(\);",
            Src("Platform", "StartupLadder.cs"));
    }

    [Fact]
    public void The_update_and_first_run_modals_flush_the_parked_offer()
    {
        Assert.Matches(@"_busy = _offering = false;\s*RemoteMediaOffer\.FlushPending\(\);",
            Src("Platform", "AppUpdater.cs"));
        Assert.Matches(@"BeginFirstLaunchQuiet\(TimeSpan\.FromMinutes\(10\)\);\s*Platform\.RemoteMediaOffer\.FlushPending\(\);",
            Src("Views", "Windows", "FirstRunWizard.axaml.cs"));
    }

    [Fact]
    public Task CleanupDropsLoadedScriptAndUnsubscribesBeforePlayerTeardown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = (CoreHaptics.Service, FunScriptService.SubscribePlaybackTime);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clip.mp4");
        File.WriteAllText(Path.ChangeExtension(path, ".funscript"), "{\"actions\":[{\"at\":0,\"pos\":0},{\"at\":1000,\"pos\":100}]}");
        using var subscribed = new ManualResetEventSlim();
        var detached = false;
        using var haptics = new HapticService(new HapticSettings { FunScriptEnabled = true });
        try
        {
            CoreHaptics.Service = haptics;
            FunScriptService.SubscribePlaybackTime = handler =>
            {
                var off = MandatoryVideoOverlay.SubscribePlaybackTime(handler);
                subscribed.Set();
                return () => { off!(); detached = true; };
            };
            haptics.FunScript.OnVideoStarted(path);
            Assert.True(subscribed.Wait(TimeSpan.FromSeconds(10)), "companion script never subscribed");
            // Subscribe returns before Core arms its timer. Drain that startup before cleanup.
            var timer = typeof(FunScriptService).GetField("_timer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.True(SpinWait.SpinUntil(() => timer.GetValue(haptics.FunScript) != null, TimeSpan.FromSeconds(10)));
            Assert.True(haptics.FunScript.IsActive);
            MandatoryVideoOverlay.Instance.CloseAll();
            Assert.False(haptics.FunScript.IsActive);
            Assert.Null(haptics.FunScript.LoadedScriptPath);
            Assert.True(detached);
        }
        finally
        {
            haptics.FunScript.OnVideoStopped();
            (CoreHaptics.Service, FunScriptService.SubscribePlaybackTime) = old;
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void TheOverlayStartsTheScriptAfterPlayAndStopsItBeforeTheStop()
    {
        // This lane's overlay hooks (WPF VideoService.cs:3410/7944): start after Play, stop before the player stops.
        var player = Src("Views", "Overlays", "MandatoryVideoOverlay.cs");
        Assert.Matches(@"_player\.Play\(_media\);\s*try\s*\{\s*CoreHaptics\.Service\?\.FunScript.OnVideoStarted\(path\)", player);
        // The teardown stops the native player through the playback seam (IMandatoryPlayback.Stop).
        var stopHook = player.IndexOf("FunScript.OnVideoStopped()", System.StringComparison.Ordinal);
        Assert.True(stopHook >= 0 && stopHook < player.IndexOf("_playback.Stop()", System.StringComparison.Ordinal));
    }

    private static string Src(params string[] p) =>
        Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", Path.Combine(p))),
            @"//[^\n]*|/\*.*?\*/|'(?:[^'\\]|\\.)'|[$@]{0,2}""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    /// <summary>Panic stops a running funscript at once (position writes are outside the 400 ms panic
    /// mute), not only later when the engine closes the video.</summary>
    [Fact]
    public Task PanicHapticsSurfaceStopsTheFunscriptBeforeTheEngine() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = CoreHaptics.Service;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clip.mp4");
        File.WriteAllText(Path.ChangeExtension(path, ".funscript"), "{\"actions\":[{\"at\":0,\"pos\":0},{\"at\":1000,\"pos\":100}]}");
        using var haptics = new HapticService(new HapticSettings { FunScriptEnabled = true });
        try
        {
            CoreHaptics.Service = haptics;
            haptics.FunScript.OnVideoStarted(path);
            Assert.True(SpinWait.SpinUntil(() => haptics.FunScript.IsActive, TimeSpan.FromSeconds(10)));
            PanicSurfaces.All.Single(s => s.Id == "haptics").Stop(null);
            Assert.False(haptics.FunScript.IsActive);
            Assert.Null(haptics.FunScript.LoadedScriptPath);
        }
        finally
        {
            haptics.FunScript.OnVideoStopped();
            CoreHaptics.Service = old;
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });
}
