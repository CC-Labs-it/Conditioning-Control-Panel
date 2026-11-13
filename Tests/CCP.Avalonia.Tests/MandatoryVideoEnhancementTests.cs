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
    [Fact]
    public void SidecarBindsOnlyWhenEnabledAndStopsOnEveryCleanup() => AvaloniaTestDispatcher.Run(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-enh-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clip.webm");
        var e = new Enhancement { MediaType = MediaTypes.Video, Metadata = new EnhancementMetadata { Name = "test", Creator = "test" } };
        e.TimelineItems.Add(new TimelineItem { Kind = TimelineItemKind.Rule, Trigger = new TimeReachedTrigger { Time = 1 },
            Action = new TriggerEffectAction { EffectType = EffectTypes.Flash, DurationMs = 500 } });
        e.TimelineItems.Add(new TimelineItem { Kind = TimelineItemKind.Rule, Trigger = new TimeReachedTrigger { Time = 2 },
            Action = new TriggerEffectAction { EffectType = EffectTypes.Flash, DurationMs = 500 } });
        File.WriteAllText(Path.Combine(dir, "clip.ccpenh.json"), EnhancementSerializer.Save(e));
        var settings = CoreSettings.Current;
        var enabled = settings.VideoEnhanceIfPossible;
        var oldDispatcher = EnhancementHostService.DispatcherFactory;
        var oldWebcam = EnhancementHostService.WebcamProvider;
        var oldPost = CoreDispatch.PostProvider;
        var oldFreeze = CoreSubliminal.BambiFreezeProvider;
        var attention = settings.AttentionChecksEnabled;
        var clock = new Clock();
        var recorder = new Counter();
        var overlay = new MandatoryVideoOverlay(() => new Decoder());
        overlay.Scheduler = new MandatoryVideoScheduler(overlay, clock, () => new[] { path });
        try
        {
            EnhancementHostService.DispatcherFactory = () => recorder;
            EnhancementHostService.WebcamProvider = null;
            CoreDispatch.PostProvider = action => action();
            CoreSubliminal.BambiFreezeProvider = null;
            settings.AttentionChecksEnabled = false;
            settings.VideoEnhanceIfPossible = false;
            overlay.Show(path, false);
            Assert.False(overlay.EnhancementRunning);
            overlay.ReportPlaybackTime(1500);
            Assert.Equal(0, recorder.Fired);
            overlay.CloseAll();

            settings.VideoEnhanceIfPossible = true;
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
        }
        finally
        {
            overlay.CloseAll();
            CoreDispatch.PostProvider = oldPost;
            CoreSubliminal.BambiFreezeProvider = oldFreeze;
            settings.AttentionChecksEnabled = attention;
            EnhancementHostService.DispatcherFactory = oldDispatcher;
            EnhancementHostService.WebcamProvider = oldWebcam;
            settings.VideoEnhanceIfPossible = enabled;
            Directory.Delete(dir, true);
        }
    });

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
