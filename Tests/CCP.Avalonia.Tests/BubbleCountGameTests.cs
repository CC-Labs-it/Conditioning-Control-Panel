using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Video.Browser;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The bubble-count game headless with a real clip on the shared LibVLC: the Test trigger
/// opens the game after the lead-in and frames reach the window; the clip's end opens the answer
/// window and a correct answer completes the game with XP; strict falls open to the panic key with
/// no global listener and swallows it with one; engine Stop (panic) closes it. The clip is committed
/// (Assets/bubblecount-2s.mp4); only a missing libvlc skips, and never on Linux CI.</summary>
public sealed class BubbleCountGameTests
{
    [Theory]
    [InlineData("ended")]
    [InlineData("error")]
    public Task Browser_metadata_ended_and_silent_secondary_share_one_clock(string terminal) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        using var game = new BrowserGame();
        game.Cache.StoreDuration(game.Clip, 60);
        var deadlines = new List<double>();
        BubbleCountWindow.ShowOnAllMonitors(game.Clip, BubbleCountScheduler.Difficulty.Medium, true,
            _ => game.Completed++, () => game.Skipped++, deadlines.Add);
        var primary = Assert.Single(BubbleCountWindow.OpenWindows);
        var browser = Assert.Single(game.Surfaces);
        browser.View.OnNavigationCompleted(new Uri("about:blank"));
        Assert.Equal(60, BubbleCountWindow.LastVideoDurationSeconds);
        Assert.Equal(TimeSpan.FromSeconds(65), primary.SafetyInterval);
        Assert.Equal(new[] { 180.0 }, deadlines);
        Assert.Equal(0.75, browser.Volume);

        var secondary = new BubbleCountWindow(game.Clip, BubbleCountScheduler.Difficulty.Medium, true,
            _ => game.Completed++, isPrimary: false);
        typeof(BubbleCountWindow).GetField("_useBrowser", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(secondary, true);
        secondary.Show();
        var mirror = secondary.Browser!;
        mirror.View.OnNavigationCompleted(new Uri("about:blank"));
        Assert.Equal(0, mirror.Volume);
        Send(mirror, "meta", 900000);
        Send(mirror, "ended");
        Assert.Equal(60, BubbleCountWindow.LastVideoDurationSeconds);
        Send(browser, "meta", 120000);
        Assert.Equal(120, BubbleCountWindow.LastVideoDurationSeconds);
        Assert.Equal(120, game.Cache.TryGetDuration(game.Clip));
        Assert.Equal(TimeSpan.FromSeconds(125), primary.SafetyInterval);
        Assert.Equal(primary.TargetCount, secondary.TargetCount);
        Assert.InRange(primary.TargetCount, 16, 24);
        Assert.Equal(TimeSpan.FromMilliseconds(120 * 700.0 / primary.TargetCount), primary.SpawnInterval);
        var target = primary.TargetCount;
        Send(browser, "meta", 120000); // repeats must not reroll targets or restart deadlines
        Send(browser, "meta", 0);
        primary.AdoptRealDuration(double.NaN);
        primary.AdoptRealDuration(double.PositiveInfinity);
        Assert.Equal(target, primary.TargetCount);
        Assert.Equal(new[] { 180.0, 240.0 }, deadlines);
        Send(browser, "playing");
        Send(browser, terminal); Send(browser, terminal);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Single(BubbleCountResultWindow.OpenWindows);
        Assert.Equal(0, game.Completed);
        Assert.Equal(0, game.Skipped);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Browser_start_failure_skips_once_and_late_async_callbacks_are_inert() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        using var game = new BrowserGame();
        var script = new TaskCompletionSource<string?>();
        BrowserVideoSurface.Create = () => { var s = new BrowserVideoSurface(_ => script.Task); game.Surfaces.Add(s); return s; };
        BubbleCountWindow.ShowOnAllMonitors(game.Clip, BubbleCountScheduler.Difficulty.Easy, true,
            _ => game.Completed++, () => game.Skipped++);
        var window = Assert.Single(BubbleCountWindow.OpenWindows);
        var browser = Assert.Single(game.Surfaces);
        browser.View.OnNavigationCompleted(new Uri("about:blank"));
        Send(browser, "error"); Send(browser, "error");
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, game.Skipped);
        Assert.Equal(0, game.Completed);
        Assert.Empty(BubbleCountWindow.OpenWindows);
        Assert.Empty(BubbleCountResultWindow.OpenWindows);
        Assert.False(BrowserVideoSurface.SessionActive);
        var duration = BubbleCountWindow.LastVideoDurationSeconds;
        script.SetResult(null); // initialization resumes after teardown
        await Task.Yield();
        Send(browser, "meta", 999000); Send(browser, "ended");
        window.AdoptRealDuration(999);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(duration, BubbleCountWindow.LastVideoDurationSeconds);
        Assert.Null(window.SafetyInterval);
        Assert.Equal(1, game.Skipped);
        Assert.Empty(BubbleCountResultWindow.OpenWindows);
    });

    [Fact]
    public Task Browser_gate_rejects_competing_owner_unmapped_file_and_missing_provider() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        using var game = new BrowserGame();
        var owner = new object();
        Assert.True(BrowserVideoSurface.TryAcquire(owner, game.Clip));
        var competing = new object();
        Assert.False(BrowserVideoSurface.TryAcquire(competing, game.Clip));
        BrowserVideoSurface.Release(competing);
        Assert.True(BrowserVideoSurface.SessionActive);
        BrowserVideoSurface.Release(owner);
        Assert.False(BrowserVideoSurface.SessionActive);
        Assert.Null(BrowserVideoSurface.Map(Path.Combine(Path.GetTempPath(), "outside.mp4")));
        game.Settings.Current.AudioOutputDeviceId = "headphones";
        Assert.False(BrowserVideoSurface.TryAcquire(owner, game.Clip));
        game.Settings.Current.AudioOutputDeviceId = "";
        BrowserVideoSurface.Seed(); // headless never offers a native browser, even if packages exist
        Assert.False(BrowserVideoGate.ShouldUseBrowser(game.Clip));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Browser_unseen_end_and_native_close_never_open_an_answer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        using var game = new BrowserGame();
        for (var i = 0; i < 2; i++)
        {
            BubbleCountWindow.ShowOnAllMonitors(game.Clip, BubbleCountScheduler.Difficulty.Easy, true,
                _ => game.Completed++, () => game.Skipped++);
            var window = Assert.Single(BubbleCountWindow.OpenWindows);
            var browser = window.Browser!;
            browser.View.OnNavigationCompleted(new Uri("about:blank"));
            if (i == 0) Send(browser, "ended"); else window.Close();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(BrowserVideoSurface.SessionActive);
            Send(browser, "playing"); Send(browser, "ended");
            Assert.Empty(BubbleCountResultWindow.OpenWindows);
        }
        Assert.Equal(2, game.Skipped);
        Assert.Equal(0, game.Completed);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(100, true)]
    [InlineData(101, true)]
    [InlineData(102, false)]
    [InlineData(103, false)]
    [InlineData(0, false)]
    public Task Browser_only_primary_media_failures_make_the_file_unsafe(int code, bool unsafeFile) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        using var game = new BrowserGame();
        BubbleCountWindow.ShowOnAllMonitors(game.Clip, BubbleCountScheduler.Difficulty.Easy, true,
            _ => game.Completed++, () => game.Skipped++);
        var primary = Assert.Single(BubbleCountWindow.OpenWindows);
        var browser = primary.Browser!;
        browser.View.OnNavigationCompleted(new Uri("about:blank"));
        var secondary = new BubbleCountWindow(game.Clip, BubbleCountScheduler.Difficulty.Easy, true,
            _ => game.Completed++, isPrimary: false);
        typeof(BubbleCountWindow).GetField("_useBrowser", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(secondary, true);
        secondary.Show();
        secondary.Browser!.View.OnNavigationCompleted(new Uri("about:blank"));
        Send(secondary.Browser, "error", code: code);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(BrowserUnsafeVideoCache.Contains(game.Clip));
        Assert.True(primary.IsVisible);
        Send(browser, "error", code: code);
        Send(browser, "error", code: code); // duplicate terminal reports still resolve just once
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(unsafeFile, BrowserUnsafeVideoCache.Contains(game.Clip));
        Assert.Equal(1, game.Skipped);
        Assert.Equal(0, game.Completed);
        Assert.Empty(BubbleCountResultWindow.OpenWindows);
        return Task.CompletedTask;
    });

    private static void Send(BrowserVideoSurface surface, string type, double durationMs = 0, int code = 0) =>
        surface.View.OnWebMessage(JsonSerializer.Serialize(new { session = surface.SessionId, type, durationMs, code }));

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private sealed class BrowserGame : IDisposable
    {
        private readonly Func<string>? _assets = CorePaths.EffectiveAssetsProvider;
        private readonly Func<SettingsService?>? _settings = CoreSettings.ServiceProvider;
        private readonly Func<string, string?, bool>? _gate = BrowserVideoGate.EngineAccepts;
        private readonly Func<BrowserVideoSurface?> _create = BrowserVideoSurface.Create;
        private readonly FieldInfo _cacheField = typeof(MandatoryVideoOverlay).GetField("_lengths", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _oldCache;
        private readonly string _dir = Directory.CreateTempSubdirectory("ccp-bcount-browser-").FullName;
        internal readonly SettingsService Settings = new();
        internal readonly VideoMetadataCache Cache = new(null!); // Store/TryGet never need a decoder.
        internal readonly List<BrowserVideoSurface> Surfaces = new();
        internal int Completed, Skipped;
        internal string Clip { get; }
        internal BrowserGame()
        {
            Clip = Path.Combine(_dir, "clip.mp4");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), Clip);
            _oldCache = _cacheField.GetValue(null);
            _cacheField.SetValue(null, Cache);
            CorePaths.EffectiveAssetsProvider = () => _dir;
            CoreSettings.ServiceProvider = () => Settings;
            Settings.Current.BrowserVideoEngineEnabled = true;
            Settings.Current.AudioOutputDeviceId = "";
            Settings.Current.DualMonitorEnabled = false;
            Settings.Current.MasterVolume = 75;
            BrowserVideoGate.EngineAccepts = (p, _) => BrowserVideoSurface.Map(p) != null;
            BrowserVideoSurface.Create = () => { var s = new BrowserVideoSurface(_ => Task.FromResult<string?>("true")); Surfaces.Add(s); return s; };
            Cache.StoreDuration(Clip, 30);
        }
        public void Dispose()
        {
            BubbleCountWindow.ForceCloseAll();
            BubbleCountResultWindow.ForceCloseAll();
            BrowserVideoSurface.Create = _create;
            BrowserVideoGate.EngineAccepts = _gate;
            CorePaths.EffectiveAssetsProvider = _assets;
            CoreSettings.ServiceProvider = _settings;
            _cacheField.SetValue(null, _oldCache);
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task PlaysAnswersAndHonoursStrictAndPanic()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-bcount-").FullName;
        var clip = Path.Combine(dir, "clip.mp4");
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), clip);
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e)
            {
                // core-linux installs libvlc (build.yml), so a missing one there is a broken job, not a skip.
                if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CI") == "true") throw;
                Assert.Skip("libvlc not installed (apt install libvlc-dev vlc-plugin-base): " + e.Message);
            }

            var s = CoreSettings.Current;
            var saved = (s.PanicKeyEnabled, s.PanicKey, s.BubbleCountStrictLock, s.DualMonitorEnabled);
            (s.PanicKeyEnabled, s.PanicKey, s.DualMonitorEnabled) = (true, "F12", false);
            double xp = 0;
            var prevXp = CoreProgression.AddXPProvider;
            CoreProgression.AddXPProvider = (a, _) => xp += a;
            var host = BubbleCountHost.Instance;
            var b = new BubbleCountScheduler(host, library: () => new[] { clip });
            var real = host.Scheduler;
            var prevEngine = CoreEngine.BubbleCount;
            var listener = BubbleCountWindow.PanicListenerLive;
            var routeAudio = BubbleCountWindow.RouteAudio;
            var audioRoutes = 0;
            var routed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            BubbleCountWindow.RouteAudio = player => { routeAudio(player); System.Threading.Interlocked.Increment(ref audioRoutes); routed.TrySetResult(); };
            var installClickHook = BubbleCountWindow.InstallClickHook;
            var hooked = new System.Collections.Generic.List<TopLevel>();
            BubbleCountWindow.InstallClickHook = w => { installClickHook(w); hooked.Add(w); };
            host.Scheduler = b;
            CoreEngine.BubbleCount = b;
            try
            {
                await AvaloniaTestDispatcher.RunAsync(async () =>
                {
                    if (Application.Current is null)
                        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                            .UseSkia()
                            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                            .SetupWithoutStarting();

                    async Task<BubbleCountWindow> Open(bool strict)
                    {
                        s.BubbleCountStrictLock = strict;
                        var routesBefore = System.Threading.Volatile.Read(ref audioRoutes);
                        routed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        b.Trigger(forceTest: true);
                        Assert.True(b.IsBusy);
                        Assert.Empty(BubbleCountWindow.OpenWindows);        // the 800 ms lead-in
                        for (var i = 0; i < 100 && (BubbleCountWindow.OpenWindows.Count == 0 || (BubbleCountWindow.Sink?.FrameCount ?? 0) < 3); i++) await Task.Delay(50);
                        var w = Assert.Single(BubbleCountWindow.OpenWindows);
                        Assert.True(BubbleCountWindow.Sink!.FrameCount >= 3, "frames decoded");
                        Assert.Same(BubbleCountWindow.Sink.Bitmap, w.VideoImage.Source);
                        Assert.True(w.Topmost && w.IsVisible);
                        Assert.Contains(w, hooked);                         // MA_NOACTIVATE on every game window
                        await routed.Task.WaitAsync(TimeSpan.FromSeconds(5));   // Playing routes the device once
                        Assert.Equal(routesBefore + 1, System.Threading.Volatile.Read(ref audioRoutes));
                        return w;
                    }

                    // Non-strict: the 2 s clip ends, the answer window asks, the right count wins.
                    await Open(strict: false);
                    for (var i = 0; i < 100 && BubbleCountResultWindow.OpenWindows.Count == 0; i++) await Task.Delay(50);
                    var r = Assert.Single(BubbleCountResultWindow.OpenWindows);
                    Assert.Equal(2, BubbleCountWindow.LastVideoDurationSeconds, 0);   // LengthChanged replaced the 30 s fallback
                    r.FindControl<TextBox>("TxtAnswer")!.Text = r.CorrectAnswer.ToString();
                    r.FindControl<TextBox>("TxtAnswer")!.Focus();
                    r.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
                    Assert.Equal(25, xp);                                 // 250 scaled by a 2 s clip
                    for (var i = 0; i < 60 && b.IsBusy; i++) await Task.Delay(50);
                    Assert.False(b.IsBusy);
                    Assert.Equal(25 + 10, xp);                            // + the service's 100, scaled
                    Assert.Empty(BubbleCountWindow.OpenWindows);

                    // Strict with a live listener: the panic key and Esc are swallowed; engine stop closes it.
                    BubbleCountWindow.PanicListenerLive = () => true;
                    var w = await Open(strict: true);
                    w.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, "");
                    w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                    Assert.True(b.IsBusy && w.IsVisible, "strict game survives the panic key and Esc");
                    CoreEngine.Stop();
                    Assert.False(b.IsBusy);
                    Assert.Empty(BubbleCountWindow.OpenWindows);
                    Assert.Null(BubbleCountWindow.Sink);

                    // Strict with NO live listener (#875): the panic key must escape, not trap.
                    BubbleCountWindow.PanicListenerLive = () => false;
                    w = await Open(strict: true);
                    w.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, "");
                    Assert.False(b.IsBusy, "no listener: the panic key force-stops a strict game");
                    Assert.Empty(BubbleCountWindow.OpenWindows);
                    Assert.Empty(host.Messages);                          // a stop, not a WRONG! retry
                });
            }
            finally
            {
                b.ForceCleanup();
                host.Scheduler = real;
                CoreEngine.BubbleCount = prevEngine;
                BubbleCountWindow.PanicListenerLive = listener;
                BubbleCountWindow.RouteAudio = routeAudio;
                BubbleCountWindow.InstallClickHook = installClickHook;
                CoreProgression.AddXPProvider = prevXp;
                (s.PanicKeyEnabled, s.PanicKey, s.BubbleCountStrictLock, s.DualMonitorEnabled) = saved;
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
