using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Deeper engine and dispatchers moved from WPF to Core (deeper-core-e), driven through
/// the IPlaybackTimeSource / IActionDispatcher / IEnhancementWebcam seams with fakes.</summary>
[Collection(SessionStatics.Name)] // the webcam path posts through the process-global CoreDispatch
public sealed class EnhancementEngineCoreTests
{
    private sealed class FakeSource : IPlaybackTimeSource
    {
        public event Action<double>? PlaybackTimeChanged;
        public double Now;
        public PlaybackRect Rect = new(100, 100, 200, 100);
        public int Pauses;
        public bool HasSubscriber => PlaybackTimeChanged != null;
        public void Tick(double t) { Now = t; PlaybackTimeChanged?.Invoke(t); }
        public double GetCurrentTimeSeconds() => Now;
        public double GetDurationSeconds() => 0;
        public bool IsPlaying => true;
        public void Seek(double seconds) => Now = seconds;
        public void Pause() => Pauses++;
        public void Play() { }
        public PlaybackRect GetVideoRect() => Rect;
    }

    private sealed class FakeDispatcher : IActionDispatcher, IEnhancementRunCleanup
    {
        public readonly List<EnhancementAction> Fired = new();
        public readonly List<string> Cleanups = new();
        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        {
            Fired.Add(action);
            return Task.CompletedTask;
        }
        public void ResetOverlayBands() => Cleanups.Add("overlay");
        public void StopOneShotFlashes() => Cleanups.Add("flash");
        public void StopOneShotSubliminals() => Cleanups.Add("subliminal");
    }

    private sealed class FakeWebcam : IEnhancementWebcam
    {
        public event Action? OnBlink;
        public event Action? OnMouthOpen;
        public event Action<double, double>? OnGazeMove;
        public event Action? OnFaceLost;
        public event Action? OnFaceFound;
        public bool GazeSubscribed => OnGazeMove != null;
        public void Gaze(double x, double y) => OnGazeMove?.Invoke(x, y);
        public void Unused() { OnBlink?.Invoke(); OnMouthOpen?.Invoke(); OnFaceLost?.Invoke(); OnFaceFound?.Invoke(); }
    }

    private static Enhancement With(params TimelineItem[] items)
    {
        var e = new Enhancement { MediaType = MediaTypes.Video };
        e.TimelineItems.AddRange(items);
        return e;
    }

    private static TimelineItem RuleAt(double t, EnhancementAction action) => new()
    {
        Kind = TimelineItemKind.Rule,
        Trigger = new TimeReachedTrigger { Time = t },
        Action = action,
        CooldownMs = 0,
    };

    [Fact]
    public void TimeReachedRuleFiresOnTheTickThatPassesIt()
    {
        var src = new FakeSource();
        var disp = new FakeDispatcher();
        var pause = new PauseAction();
        using var engine = new EnhancementEngine(With(RuleAt(2.0, pause)), src, disp);
        engine.Start();

        src.Tick(1.0);
        Assert.Empty(disp.Fired);
        src.Tick(2.1);

        Assert.Same(pause, Assert.Single(disp.Fired));
    }

    [Fact]
    public void StopRetiresOverlaysAndOnlyTheOneShotKindsThisRunFired()
    {
        var src = new FakeSource();
        var inner = new FakeDispatcher();
        // The host hands the engine the recording decorator, so the cleanup must pass through it.
        var recorder = new RecordingActionDispatcher(inner);
        var engine = new EnhancementEngine(
            With(RuleAt(1.0, new TriggerEffectAction { EffectType = EffectTypes.Flash })), src, recorder);
        engine.Start();
        src.Tick(1.5);

        engine.Stop();

        Assert.Equal(new[] { "overlay", "flash" }, inner.Cleanups);
        Assert.Single(recorder.RecentActions);
        Assert.False(src.HasSubscriber);
    }

    [Fact]
    public void GazeRuleMapsTheNormalizedRectOntoTheSourcesPlaybackRect()
    {
        var saved = CoreDispatch.PostProvider;
        CoreDispatch.PostProvider = null; // run Post inline so the test stays on one thread
        try
        {
            var src = new FakeSource(); // video at (100,100) 200x100
            var disp = new FakeDispatcher();
            var cam = new FakeWebcam();
            var shake = new ScreenShakeAction();
            var rule = new TimelineItem
            {
                Kind = TimelineItemKind.Rule,
                Duration = double.MaxValue,
                CooldownMs = 0,
                // right half of the frame: x 200..300, y 100..200
                Trigger = new GazeTargetTrigger { Rect = new[] { 0.5, 0.0, 0.5, 1.0 }, MinDwellMs = 0 },
                Action = shake,
            };
            var engine = new EnhancementEngine(With(rule), src, disp, cam);
            engine.Start();
            Assert.True(cam.GazeSubscribed);

            cam.Gaze(150, 150); // left half: outside
            cam.Gaze(150, 150);
            Assert.Empty(disp.Fired);

            cam.Gaze(250, 150); // dwell starts
            cam.Gaze(250, 150); // dwell (0 ms) met
            Assert.Same(shake, Assert.Single(disp.Fired));

            src.Rect = PlaybackRect.Empty; // audio-only / no window: gaze is skipped
            disp.Fired.Clear();
            cam.Gaze(250, 150);
            cam.Gaze(250, 150);
            Assert.Empty(disp.Fired);

            engine.Stop();
            Assert.False(cam.GazeSubscribed);
        }
        finally { CoreDispatch.PostProvider = saved; }
    }

    [Fact]
    public void LoggingDispatcherRecordsADryRunLineAndSkipsRampUpdates()
    {
        var log = new LoggingActionDispatcher();
        var ctx = new EnhancementDispatchContext(new Enhancement(), new FakeSource(), 3.25, null);

        log.DispatchAsync(new PauseAction(), ctx);
        log.DispatchAsync(new TriggerEffectAction { EffectType = EffectTypes.Overlay, Phase = EffectPhase.Update }, ctx);

        Assert.Equal("t=3.25s  pause", Assert.Single(log.RecentActions));
    }
}
