using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Video.Browser;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF VideoService.cs:3207 and VideoService.Browser.cs:110,361: with the browser engine on, a
/// mandatory clip plays in the page player, LibVLC is the fallback, and panic closes the session.</summary>
public sealed class MandatoryVideoBrowserTests
{
    [Fact]
    public Task EligibleClipPlaysInTheBrowserAndPanicClosesIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new Rig();
        var o = MandatoryVideoOverlay.Instance;
        File.WriteAllText(Path.ChangeExtension(rig.Clip, ".funscript"), "{\"actions\":[{\"at\":0,\"pos\":0},{\"at\":1000,\"pos\":100}]}");
        var oldHaptics = ConditioningControlPanel.CoreHaptics.Service;
        using var haptics = new ConditioningControlPanel.Services.HapticService(new ConditioningControlPanel.Models.HapticSettings { FunScriptEnabled = true });
        ConditioningControlPanel.CoreHaptics.Service = haptics;
        try
        {
            await rig.Open(strict: false);
            var browser = Assert.IsType<MandatoryVideoOverlay.BrowserPlayback>(o.Browser);
            var page = browser.Views[0];
            Assert.Null(o.Sink);                                           // no LibVLC decoder
            Assert.Contains(page.View, o.Surfaces[0].Picture.Children);
            Assert.False(o.Surfaces[0].Video.IsVisible);
            Assert.True(BrowserVideoSurface.SessionActive);
            Assert.Equal(o.EffectivePlayerVolume / 100.0, page.Volume);
            Assert.Equal(1, rig.Ducks);

            page.View.OnNavigationCompleted(new Uri("about:blank"));
            await Task.Delay(10);
            var times = new List<long>();
            using var sub = new Unsub(MandatoryVideoOverlay.SubscribePlaybackTime(times.Add));
            Send(page, new { type = "playing" });
            Send(page, new { type = "meta", durationMs = 2000 });
            Send(page, new { type = "time", ms = 1500 });
            Assert.True(o.FirstFrameMs >= 0);
            Assert.Equal(2000, browser.Length);
            Assert.Equal(new long[] { 1500 }, times);
            for (var i = 0; i < 100 && haptics.FunScript.LoadedScriptPath == null; i++) await Task.Delay(50);
            Assert.NotNull(haptics.FunScript.LoadedScriptPath);              // the Core hook runs the script on the page clock
            for (var i = 0; i < 5; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
            var controls = Assert.Single(o.ControlHosts);                   // mute and close sit above the web view
            Assert.Same(o.Surfaces[0].Buttons.Row, controls.Content);
            Assert.True(controls.IsVisible && o.Surfaces[0].Buttons.Close.IsVisible);

            o.ToggleUserMute();                                             // the mute button reaches the page
            Assert.Equal(0, page.Volume);

            Send(page, new { type = "key", key = "Escape" });               // a focused page reports Esc
            Dispatcher.UIThread.RunJobs();
            Assert.True(o.GracePaused);
            Assert.False(page.View.IsVisible);                              // the grace card is not under the web view
            Assert.True(o.Surfaces[0].Grace.IsVisible);

            var windows = o.Windows.ToList();
            PanicSurfaces.All.Single(s => s.Id == "engine").Stop(null);
            Assert.Empty(o.Windows);
            Assert.All(windows, w => Assert.False(w.IsVisible));
            Assert.Null(o.Browser);
            Assert.Empty(o.ControlHosts);
            Assert.False(BrowserVideoSurface.SessionActive);
            Assert.Equal(rig.Ducks, rig.Unducks);
            Assert.Null(haptics.FunScript.LoadedScriptPath);
        }
        finally { haptics.FunScript.OnVideoStopped(); ConditioningControlPanel.CoreHaptics.Service = oldHaptics; }
    });

    /// <summary>WPF VideoService.cs:637-664 (#735) and VideoService.Browser.cs:585 (#874): a Deeper hold pauses
    /// through PausePrimary with the frame left on screen, its release never resumes under the grace card,
    /// and the page's own pause flag answers IsPlaying.</summary>
    [Fact]
    public Task EnhancementHoldPausesThePageAndCannotResumeUnderTheGraceCard() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new Rig();
        var o = MandatoryVideoOverlay.Instance;
        var e = new Enhancement { MediaType = MediaTypes.Video, Metadata = new EnhancementMetadata { Name = "test", Creator = "test" } };
        e.TimelineItems.Add(new TimelineItem { Kind = TimelineItemKind.Rule, Trigger = new TimeReachedTrigger { Time = 1 }, CooldownMs = 0,
            Action = new TriggerEffectAction { EffectType = EffectTypes.Flash, DurationMs = 500 } });
        File.WriteAllText(Path.ChangeExtension(rig.Clip, ".ccpenh.json"), EnhancementSerializer.Save(e));
        rig.Settings.Current.VideoEnhanceIfPossible = true;
        var (oldDispatcher, oldWebcam, oldPost) = (EnhancementHostService.DispatcherFactory, EnhancementHostService.WebcamProvider, CoreDispatch.PostProvider);
        var recorder = new SourceRecorder();
        try
        {
            EnhancementHostService.DispatcherFactory = () => recorder;
            EnhancementHostService.WebcamProvider = null;
            CoreDispatch.PostProvider = action => action();
            await rig.Open(strict: false);
            Assert.True(o.EnhancementRunning);
            var browser = o.Browser!;
            var page = browser.Views[0];
            page.View.OnNavigationCompleted(new Uri("about:blank"));
            await Task.Delay(10);
            Send(page, new { type = "playing" });
            Send(page, new { type = "time", ms = 1500, playedMs = 1500, paused = false });
            var source = Assert.IsAssignableFrom<IPlaybackTimeSource>(recorder.Source);

            source.Pause();                                                  // a Deeper speak hold
            Assert.True(o.PrimaryPaused);
            Assert.False(browser.IsPlaying);
            Assert.True(page.View.IsVisible);                                // the paused frame stays on screen
            source.Play();
            Assert.False(o.PrimaryPaused);
            Assert.True(browser.IsPlaying);

            Send(page, new { type = "key", key = "Escape" });
            Dispatcher.UIThread.RunJobs();
            Assert.True(o.GracePaused);
            source.Pause();
            source.Play();                                                   // the hold released under the card
            Assert.True(o.GracePaused);
            Assert.False(browser.IsPlaying);
            Assert.False(page.View.IsVisible);
            o.ResumeFromGrace("test card resume");
            Assert.True(browser.IsPlaying && page.View.IsVisible && !o.PrimaryPaused);

            Send(page, new { type = "time", ms = 1600, playedMs = 1600, paused = true });   // a pause the page sits in
            Assert.False(browser.IsPlaying);
        }
        finally
        {
            (EnhancementHostService.DispatcherFactory, EnhancementHostService.WebcamProvider, CoreDispatch.PostProvider) = (oldDispatcher, oldWebcam, oldPost);
        }
    });

    private sealed class SourceRecorder : IActionDispatcher
    {
        internal IPlaybackTimeSource? Source;
        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, System.Threading.CancellationToken ct = default)
        { Source = ctx.Source; return Task.CompletedTask; }
    }

    [Fact]
    public Task AttentionChecksKeepTheClipOnLibVlc() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new Rig();
        rig.Settings.Current.AttentionChecksEnabled = true;            // in-window targets: the web view would cover them
        await rig.Open(strict: false);
        Assert.Null(MandatoryVideoOverlay.Instance.Browser);
        Assert.False(BrowserVideoSurface.SessionActive);
    });

    [Theory]
    [InlineData(false, false, 4, true)]    // media error before playing: LibVLC replay, the file is marked
    [InlineData(false, false, 0, true)]    // engine failure before playing: LibVLC replay, the engine stands down
    [InlineData(true, false, 100, false)]  // mid-clip: the clip ends
    [InlineData(true, true, 0, true)]      // mid-clip under strict, not the file: the lock holds via LibVLC
    [InlineData(true, true, 4, false)]     // mid-clip under strict, a bad file: the clip ends
    public Task BrowserFailureFallsBackOrEnds(bool started, bool strict, int code, bool replays) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var rig = new Rig();
        try { _ = new LibVlcAudio("--aout=dummy"); }
        catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }
        var o = MandatoryVideoOverlay.Instance;
        await rig.Open(strict);
        var page = o.Browser!.Views[0];
        page.View.OnNavigationCompleted(new Uri("about:blank"));
        await Task.Delay(10);
        if (started) Send(page, new { type = "playing" });
        Send(page, new { type = "error", code });
        Dispatcher.UIThread.RunJobs();
        Assert.Null(o.Browser);
        Assert.False(BrowserVideoSurface.SessionActive);
        Assert.Equal(code is >= 1 and <= 4 or 100, BrowserUnsafeVideoCache.Contains(rig.Clip));
        Assert.Equal(code == 0, BrowserVideoSurface.StoodDown);
        Assert.Equal(replays, o.Sink != null);                          // the same clip on LibVLC
        Assert.Equal(replays, o.Windows.Count == 1 && rig.Video.IsPlaying);
        rig.Video.Stop();
        Assert.Empty(o.Windows);
    });

    [Fact]
    public void PageKeysMapToKeyNamesAndABrowserSessionIsNeverDucked() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.Equal(global::Avalonia.Input.Key.D7, MandatoryVideoOverlay.PageKey("7"));
        Assert.Equal(global::Avalonia.Input.Key.Space, MandatoryVideoOverlay.PageKey(" "));
        Assert.Equal(global::Avalonia.Input.Key.Escape, MandatoryVideoOverlay.PageKey("Escape"));
        Assert.Null(MandatoryVideoOverlay.PageKey(""));

        using var rig = new Rig();
        rig.Settings.Current.ExcludeBambiCloudFromDucking = false;
        var sets = new List<string>();
        var audio = new LibVlcAudio(args =>
        {
            if (args.StartsWith("-f json")) return
                "[{\"index\":3,\"volume\":{\"mono\":{\"value\":65536}},\"properties\":{\"application.process.id\":\"2\",\"application.process.binary\":\"WebKitWebProcess\"}},"
                + "{\"index\":4,\"volume\":{\"mono\":{\"value\":65536}},\"properties\":{\"application.process.id\":\"3\",\"application.process.binary\":\"spotify\"}}]";
            lock (sets) sets.Add(args);
            return "";
        });
        var owner = new object();
        try
        {
            Assert.True(BrowserVideoSurface.TryAcquire(owner, rig.Clip));
            audio.Duck(50);
            audio.Drain();
            Assert.Equal(new[] { "set-sink-input-volume 4 32768" }, sets);   // the page's own audio stays up
        }
        finally { audio.Shutdown(); BrowserVideoSurface.Release(owner); }
    });

    private static void Send(BrowserVideoSurface page, object message)
    {
        var json = JsonSerializer.SerializeToElement(message);
        var body = new Dictionary<string, JsonElement> { ["session"] = JsonSerializer.SerializeToElement(page.SessionId) };
        foreach (var p in json.EnumerateObject()) body[p.Name] = p.Value;
        page.View.OnWebMessage(JsonSerializer.Serialize(body));
    }

    private sealed class Unsub(Action? off) : IDisposable { public void Dispose() => off?.Invoke(); }

    private sealed class Rig : IDisposable
    {
        private readonly Func<string>? _assets = CorePaths.EffectiveAssetsProvider;
        private readonly Func<SettingsService?>? _settings = CoreSettings.ServiceProvider;
        private readonly Func<string, string?, bool>? _gate = BrowserVideoGate.EngineAccepts;
        private readonly Func<BrowserVideoSurface?> _create = BrowserVideoSurface.Create;
        private readonly bool _stoodDown = BrowserVideoSurface.StoodDown;
        private readonly (Action<int>?, Action<long>?) _duck = (CoreAudio.DuckProvider, CoreAudio.UnduckProvider);
        private readonly MandatoryVideoScheduler _scheduler = MandatoryVideoOverlay.Instance.Scheduler;
        private readonly Func<bool> _listener = MandatoryVideoOverlay.PanicListenerLive;
        private readonly MandatoryVideoScheduler? _engineVideo = CoreEngine.Video;
        // The singleton's grace dedup stamp outlives a clip (WPF VideoService.cs:367); a later stepped-clock test reads it.
        private static readonly System.Reflection.FieldInfo LastGrace = typeof(MandatoryVideoOverlay).GetField("_lastGraceUtc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        private readonly object? _lastGrace = LastGrace.GetValue(MandatoryVideoOverlay.Instance);
        private readonly string _dir = Directory.CreateTempSubdirectory("ccp-mvideo-browser-").FullName;
        internal readonly SettingsService Settings = new();
        internal readonly MandatoryVideoScheduler Video;
        internal int Ducks, Unducks;
        internal string Clip => Path.Combine(_dir, "videos", "clip.mp4");

        internal Rig()
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            Directory.CreateDirectory(Path.GetDirectoryName(Clip)!);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), Clip);
            CorePaths.EffectiveAssetsProvider = () => _dir;
            CoreSettings.ServiceProvider = () => Settings;
            var s = Settings.Current;
            (s.BrowserVideoEngineEnabled, s.AudioOutputDeviceId, s.DualMonitorEnabled, s.MasterVolume) = (true, "", false, 80);
            (s.AttentionChecksEnabled, s.MandatoryVideoFloating, s.GentleVisuals, s.AudioDuckingEnabled) = (false, false, false, true);
            (s.PanicKeyEnabled, s.PanicKey, s.VideoMaxDurationSeconds) = (true, "F12", 0);
            BrowserVideoSurface.StoodDown = false;
            BrowserVideoGate.EngineAccepts = (p, k) => !BrowserUnsafeVideoCache.Contains(p, k) && BrowserVideoSurface.Map(p) != null;
            BrowserVideoSurface.Create = () => new BrowserVideoSurface(_ => Task.FromResult<string?>("true"));
            (CoreAudio.DuckProvider, CoreAudio.UnduckProvider) = (_ => Ducks++, _ => Unducks++);
            MandatoryVideoOverlay.PanicListenerLive = () => true;
            Video = new MandatoryVideoScheduler(MandatoryVideoOverlay.Instance, library: () => new[] { Clip });
            MandatoryVideoOverlay.Instance.Scheduler = Video;
            CoreEngine.Video = Video;
        }

        internal async Task Open(bool strict)
        {
            Assert.True(Video.Trigger(strict));
            var o = MandatoryVideoOverlay.Instance;
            for (var i = 0; i < 100 && o.Windows.Count == 0; i++) await Task.Delay(50);   // the 1.3 s pre-roll
            Assert.Single(o.Windows);
        }

        public void Dispose()
        {
            Video.Stop();
            LastGrace.SetValue(MandatoryVideoOverlay.Instance, _lastGrace);
            MandatoryVideoOverlay.Instance.Scheduler = _scheduler;
            MandatoryVideoOverlay.PanicListenerLive = _listener;
            CoreEngine.Video = _engineVideo;
            (CoreAudio.DuckProvider, CoreAudio.UnduckProvider) = _duck;
            BrowserVideoSurface.StoodDown = _stoodDown;
            (BrowserVideoSurface.Create, BrowserVideoGate.EngineAccepts, CorePaths.EffectiveAssetsProvider, CoreSettings.ServiceProvider) = (_create, _gate, _assets, _settings);
            try { Directory.Delete(_dir, true); } catch { }
        }
    }
}
