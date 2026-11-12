using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class MandatoryVideoEnhancementTests
{
    private static TimelineItem Rule(EnhancementTrigger trigger) => new() { Kind = TimelineItemKind.Rule, Trigger = trigger, CooldownMs = 0,
        Action = new TriggerEffectAction { EffectType = EffectTypes.Flash, DurationMs = 500 } };

    // One sidecar-enhanced clip on the injected decoder; every static it sets is restored.
    private static void WithClip(Action<MandatoryVideoOverlay, string, Clock, Counter, Decoder> body, params TimelineItem[] items) => AvaloniaTestDispatcher.Run(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-enh-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clip.webm");
        var e = new Enhancement { MediaType = MediaTypes.Video, Metadata = new EnhancementMetadata { Name = "test", Creator = "test" } };
        e.TimelineItems.AddRange(items);
        File.WriteAllText(Path.Combine(dir, "clip.ccpenh.json"), EnhancementSerializer.Save(e));
        var settings = CoreSettings.Current;
        var saved = (settings.VideoEnhanceIfPossible, settings.AttentionChecksEnabled, settings.VideoMaxDurationSeconds);
        var oldDispatcher = EnhancementHostService.DispatcherFactory;
        var oldWebcam = EnhancementHostService.WebcamProvider;
        var oldPost = CoreDispatch.PostProvider;
        var oldFreeze = CoreSubliminal.BambiFreezeProvider;
        var clock = new Clock();
        var recorder = new Counter();
        var decoder = new Decoder();
        var overlay = new MandatoryVideoOverlay(() => decoder);
        overlay.Scheduler = new MandatoryVideoScheduler(overlay, clock, () => new[] { path });
        try
        {
            EnhancementHostService.DispatcherFactory = () => recorder;
            EnhancementHostService.WebcamProvider = null;
            CoreDispatch.PostProvider = action => action();
            CoreSubliminal.BambiFreezeProvider = null;
            (settings.VideoEnhanceIfPossible, settings.AttentionChecksEnabled, settings.VideoMaxDurationSeconds) = (true, false, 0);
            body(overlay, path, clock, recorder, decoder);
        }
        finally
        {
            overlay.CloseAll();
            CoreDispatch.PostProvider = oldPost;
            CoreSubliminal.BambiFreezeProvider = oldFreeze;
            EnhancementHostService.DispatcherFactory = oldDispatcher;
            EnhancementHostService.WebcamProvider = oldWebcam;
            (settings.VideoEnhanceIfPossible, settings.AttentionChecksEnabled, settings.VideoMaxDurationSeconds) = saved;
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public void SidecarBindsOnlyWhenEnabledAndStopsOnEveryCleanup() => WithClip((overlay, path, clock, recorder, _) =>
    {
        CoreSettings.Current.VideoEnhanceIfPossible = false;
        overlay.Show(path, false);
        Assert.False(overlay.EnhancementRunning);
        overlay.ReportPlaybackTime(1500);
        Assert.Equal(0, recorder.Fired);
        overlay.CloseAll();

        CoreSettings.Current.VideoEnhanceIfPossible = true;
        Assert.True(overlay.Scheduler.Trigger(strictOverride: false, path: path));
        clock.Fire(); // pre-roll completes and starts the fake decoder
        Assert.True(overlay.EnhancementRunning);
        overlay.ReportPlaybackTime(1500);
        Assert.Equal(1, recorder.Fired);
        overlay.OnNaturalVideoEnd();
        overlay.ReportPlaybackTime(2500);
        Assert.Equal(1, recorder.Fired);

        overlay.Show(path, false);
        overlay.ReportPlaybackTime(1500);
        Assert.Equal(2, recorder.Fired);
        overlay.Scheduler.Stop(); // what CoreEngine.Stop (CoreEngine.cs:94) runs on Stop and panic
        overlay.ReportPlaybackTime(2500);
        Assert.Equal(2, recorder.Fired);

        EnhancementHostService.DispatcherFactory = null;
        overlay.Show(path, false);
        Assert.False(overlay.EnhancementRunning);
        overlay.ReportPlaybackTime(1500);
        Assert.Equal(2, recorder.Fired);
    }, Rule(new TimeReachedTrigger { Time = 1 }), Rule(new TimeReachedTrigger { Time = 2 }));

    [Fact]
    public void BoundEnhancementTurnsTheLengthGuillotineIntoAStallWatch() => WithClip((overlay, path, clock, _, decoder) =>
    {
        void Play(bool enhance)
        {
            CoreSettings.Current.VideoEnhanceIfPossible = enhance;
            Assert.True(overlay.Scheduler.Trigger(strictOverride: false, path: path));
            clock.Fire();
            Assert.Equal(enhance, overlay.EnhancementRunning);
        }
        bool At(double played, long ms) { overlay.PlayedSkew = played; decoder.Time = ms; overlay.GuardTick(); return overlay.Scheduler.IsPlaying; }

        // WPF VideoService.cs:6596: a looping enhancement outlives length + 5 s while the playhead moves.
        Play(enhance: true);
        Assert.True(At(66, 1000));
        Assert.True(At(150, 2000));
        Assert.True(At(150 + MandatoryVideoOverlay.EnhancementStallGraceSeconds - 1, 2000));
        Assert.False(At(150 + MandatoryVideoOverlay.EnhancementStallGraceSeconds, 2000)); // 90 s without progress

        // No enhancement bound: the plain guillotine at length + 5 s (the Decoder is 60 s long).
        Play(enhance: false);
        Assert.True(At(64, 1000));
        Assert.False(At(66, 2000));
    });

    [Fact]
    public void GazeRulesHitTheContainFitVideoBoxOnScreen() => WithClip((overlay, path, clock, recorder, _) =>
    {
        var webcam = new Webcam();
        EnhancementHostService.WebcamProvider = () => webcam;
        Assert.True(overlay.Scheduler.Trigger(strictOverride: false, path: path));
        clock.Fire();
        var surface = overlay.Build(null, false);
        ((System.Collections.Generic.List<MandatoryVideoOverlay.Surface>)overlay.Surfaces).Add(surface);
        surface.Window.Show();
        surface.Video.Source = new global::Avalonia.Media.Imaging.WriteableBitmap(new global::Avalonia.PixelSize(160, 90),
            new global::Avalonia.Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Opaque);
        surface.Window.UpdateLayout();

        // A 16:9 picture in a 4:3 window: full width, letterboxed top and bottom (WPF FitContain).
        var origin = global::Avalonia.VisualExtensions.PointToScreen(surface.Window, default);
        var (w, h) = (surface.Window.ClientSize.Width, surface.Window.ClientSize.Height);
        Assert.True(w / h < 16.0 / 9);
        var bar = (h - w * 9 / 16) / 2;
        Assert.Equal(new PlaybackRect(origin.X, origin.Y + bar, w, w * 9 / 16), MandatoryVideoOverlay.ScreenRect(surface.Video));
        void Look(double x, double y) { webcam.Look(origin.X + x, origin.Y + y); webcam.Look(origin.X + x, origin.Y + y); }
        Look(w / 2, bar / 2);   // the bar above the picture
        Assert.Equal(0, recorder.Fired);
        Look(w / 2, h / 2);     // inside the picture
        Assert.Equal(1, recorder.Fired);
    }, Rule(new GazeTargetTrigger { MinDwellMs = 0 }));

    private sealed class Webcam : IEnhancementWebcam
    {
        public event Action? OnBlink { add { } remove { } }
        public event Action? OnMouthOpen { add { } remove { } }
        public event Action? OnFaceLost { add { } remove { } }
        public event Action? OnFaceFound { add { } remove { } }
        public event Action<double, double>? OnGazeMove;
        internal void Look(double x, double y) => OnGazeMove?.Invoke(x, y);
    }

    [Fact]
    public Task EngineStartNudgeHonoursGatesConsentAndDontAskAgain() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = CoreSettings.Current;
        var saved = (s.MandatoryVideosEnabled, s.SkipMandatoryVideoEnhanceNudge, s.VideoEnhanceIfPossible);
        var (shown, scan, dialog, lockdown) = (MainShellWindow.VideoEnhanceNudgeShown, MainShellWindow.VideoEnhanceScan,
            MainShellWindow.VideoEnhanceNudgeDialog, LockdownService.Current);
        var asked = new System.Collections.Generic.List<(string Body, string Yes)>();
        (bool, bool) answer = default;
        var running = true;
        var webcamStarts = 0;
        var result = new MandatoryVideoEnhancementScanner.ScanResult(true, false);
        Task Nudge() => MainShellWindow.MaybePromptMandatoryVideoEnhancement(null, () => running, () => false, () => webcamStarts++);
        void Reset(bool webcam) { MainShellWindow.VideoEnhanceNudgeShown = false; result = new(true, webcam); asked.Clear(); }
        try
        {
            MainShellWindow.VideoEnhanceScan = _ => result;
            MainShellWindow.VideoEnhanceNudgeDialog = (_, body, yes) => { asked.Add((body, yes)); return Task.FromResult(answer); };
            (s.MandatoryVideosEnabled, s.SkipMandatoryVideoEnhanceNudge, s.VideoEnhanceIfPossible) = (true, false, false);

            // Not now leaves enhancement off; the nudge is once per launch.
            Reset(webcam: false);
            await Nudge();
            Assert.Equal(Loc.Get("deeper_nudge_yes_enhance"), Assert.Single(asked).Yes);
            Assert.Contains(Loc.Get("deeper_nudge_enhance_off"), asked[0].Body);
            Assert.False(s.VideoEnhanceIfPossible);
            await Nudge();
            Assert.Single(asked);

            // Lockdown and an engine that stopped during the scan never ask.
            Reset(webcam: false);
            var ld = LockdownService.Current = new LockdownService();
            ld.Activate(TimeSpan.FromMinutes(5));
            try { await Nudge(); } finally { ld.Deactivate(); }
            running = false;
            await Nudge();
            Assert.Empty(asked);
            running = true;

            // Yes enables enhancement and routes the webcam through the consent flow.
            Reset(webcam: true);
            answer = (true, false);
            await Nudge();
            Assert.Equal(Loc.Get("deeper_nudge_yes_both"), Assert.Single(asked).Yes);
            Assert.True(s.VideoEnhanceIfPossible);
            Assert.Equal(1, webcamStarts);

            // Don't ask again on Not now stops the question without starting anything.
            Reset(webcam: true);
            answer = (false, true);
            await Nudge();
            Assert.Equal(Loc.Get("deeper_nudge_yes_webcam"), Assert.Single(asked).Yes);
            Assert.True(s.SkipMandatoryVideoEnhanceNudge);
            Assert.Equal(1, webcamStarts);
            Reset(webcam: true);
            await Nudge();
            Assert.Empty(asked);
        }
        finally
        {
            (s.MandatoryVideosEnabled, s.SkipMandatoryVideoEnhanceNudge, s.VideoEnhanceIfPossible) = saved;
            (MainShellWindow.VideoEnhanceNudgeShown, MainShellWindow.VideoEnhanceScan,
                MainShellWindow.VideoEnhanceNudgeDialog, LockdownService.Current) = (shown, scan, dialog, lockdown);
        }
    });

    private sealed class Counter : IActionDispatcher
    {
        internal int Fired;
        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        { Fired++; return Task.CompletedTask; }
    }
    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 60000;
        public bool IsPlaying => true;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public void SetPause(bool paused) { }
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Clock : TimeProvider
    {
        private ManualTimer? _timer;
        public void Fire() => _timer!.Fire();
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
            => _timer = new ManualTimer(() => cb(state));
        private sealed class ManualTimer(Action fire) : ITimer
        {
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }

}
