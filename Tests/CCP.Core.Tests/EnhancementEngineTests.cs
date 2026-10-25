using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Deeper engine and its host now live in Core: timeline firing, the webcam seam,
/// the Stop-time cleanup seam (through the recorder the host always wraps around the dispatcher)
/// and the host's dispatcher/webcam providers.</summary>
public class EnhancementEngineTests
{
    [Fact]
    public void TimeReachedRule_FiresOnceWhenPlaybackCrossesIt()
    {
        var source = new FakeSource();
        var dispatcher = new FakeDispatcher();
        using var engine = new EnhancementEngine(Enh(Rule(new TimeReachedTrigger { Time = 1.0 }, Flash())), source, dispatcher);
        engine.Start();

        source.Advance(0.5);
        Assert.Empty(dispatcher.Fired);
        source.Advance(1.2);
        source.Advance(1.4);

        var fired = Assert.Single(dispatcher.Fired);
        Assert.Equal(EffectTypes.Flash, ((TriggerEffectAction)fired).EffectType);
    }

    [Fact]
    public void BlinkFromTheWebcamSeam_FiresTheBlinkRule_AndStopUnsubscribes()
    {
        var source = new FakeSource();
        var dispatcher = new FakeDispatcher();
        var webcam = new FakeWebcam();
        var engine = new EnhancementEngine(Enh(Rule(new BlinkDetectedTrigger(), Subliminal())), source, dispatcher, webcam);
        engine.Start();

        webcam.Blink();
        Assert.Single(dispatcher.Fired);

        engine.Stop();
        Assert.False(webcam.HasBlinkListener);
    }

    [Fact]
    public void Stop_RetiresOneShotsThroughTheRecorderWrappedDispatcher()
    {
        var source = new FakeSource();
        var inner = new FakeDispatcher();
        var engine = new EnhancementEngine(
            Enh(Rule(new TimeReachedTrigger { Time = 1.0 }, Flash()), Rule(new TimeReachedTrigger { Time = 1.1 }, Subliminal())),
            source, new RecordingActionDispatcher(inner));
        engine.Start();
        source.Advance(1.5);

        engine.Stop();

        Assert.Equal(new[] { "reset-bands", "stop-flashes", "stop-subliminals" }, inner.Cleanups);
    }

    [Fact]
    public void Stop_WithoutOneShots_OnlyResetsOverlayBands()
    {
        var inner = new FakeDispatcher();
        var engine = new EnhancementEngine(Enh(), new FakeSource(), new RecordingActionDispatcher(inner));
        engine.Start();
        engine.Stop();
        Assert.Equal(new[] { "reset-bands" }, inner.Cleanups);
    }

    [Fact]
    public void HostBind_UsesTheHeadsDispatcherAndWebcam()
    {
        var inner = new FakeDispatcher();
        var webcam = new FakeWebcam();
        EnhancementHostService.DispatcherFactory = () => inner;
        EnhancementHostService.WebcamProvider = () => webcam;
        try
        {
            using var host = new EnhancementHostService();
            var logged = new List<string>();
            host.ActionLogged += logged.Add;
            string? failed = null;
            host.LoadFailed += m => failed = m;
            Assert.True(host.LoadFromMemory(Enh(Rule(new BlinkDetectedTrigger(), Subliminal())), "memory"), failed);
            Assert.True(host.Bind(new FakeSource()));

            webcam.Blink();

            Assert.Single(inner.Fired);
            Assert.Single(logged);
        }
        finally
        {
            EnhancementHostService.DispatcherFactory = null;
            EnhancementHostService.WebcamProvider = null;
        }
    }

    [Fact]
    public void HostRebindAndUnload_DisposeEachAdapterAfterUnsubscribing()
    {
        var adapters = new List<FakeWebcam>();
        EnhancementHostService.WebcamProvider = () =>
        {
            var webcam = new FakeWebcam();
            adapters.Add(webcam);
            return webcam;
        };
        try
        {
            using var host = new EnhancementHostService();
            Assert.True(host.LoadFromMemory(Enh(Rule(new BlinkDetectedTrigger(), Subliminal())), "memory"));
            Assert.True(host.Bind(new FakeSource()));
            Assert.True(adapters[0].HasBlinkListener);
            Assert.True(host.Bind(new FakeSource()));
            Assert.Equal(1, adapters[0].DisposeCalls);
            Assert.False(adapters[0].HadListenersAtDispose);
            Assert.True(adapters[1].HasBlinkListener);

            host.Unload();
            host.UnbindEngine();
            Assert.Equal(1, adapters[1].DisposeCalls);
            Assert.False(adapters[1].HadListenersAtDispose);
            Assert.False(host.IsRunning);
        }
        finally
        {
            EnhancementHostService.WebcamProvider = null;
        }
    }

    [Fact]
    public void GazeTarget_MapsTheNormalizedRectOntoTheVideoRect_HitFiresMissDoesNot()
    {
        // Video drawn at (100,100) 200x100; target = right half -> screen x 200..300, y 100..200.
        var source = new FakeSource { VideoRect = new PlaybackRect(100, 100, 200, 100) };
        var dispatcher = new FakeDispatcher();
        var target = new GazeTargetTrigger { Rect = new[] { 0.5, 0.0, 0.5, 1.0 }, MinDwellMs = 0 };
        using var engine = new EnhancementEngine(Enh(Rule(target, Flash())), source, dispatcher);
        engine.Start();

        engine.InjectGaze(new PlaybackPoint(150, 150)); // left half: miss
        engine.InjectGaze(new PlaybackPoint(150, 150));
        Assert.Empty(dispatcher.Fired);

        engine.InjectGaze(new PlaybackPoint(250, 150)); // dwell starts
        engine.InjectGaze(new PlaybackPoint(250, 150)); // dwell (0 ms) met: fires
        Assert.Single(dispatcher.Fired);
    }

    [Fact]
    public void HostBind_Unseeded_DryRunsWithoutAWebcam()
    {
        EnhancementHostService.DispatcherFactory = null;
        EnhancementHostService.WebcamProvider = null;
        using var host = new EnhancementHostService();
        var diag = new List<string>();
        host.Diagnostic += diag.Add;
        Assert.True(host.LoadFromMemory(Enh(Rule(new BlinkDetectedTrigger(), Subliminal())), "memory"));
        Assert.True(host.Bind(new FakeSource()));
        Assert.Contains(diag, l => l.Contains("no webcam tracker is attached"));
    }

    private static Enhancement Enh(params TimelineItem[] items)
    {
        var e = new Enhancement { MediaType = MediaTypes.Video, Metadata = new EnhancementMetadata { Name = "t", Creator = "c" } };
        e.TimelineItems.AddRange(items);
        return e;
    }

    private static TimelineItem Rule(EnhancementTrigger trigger, EnhancementAction action) =>
        new() { Kind = TimelineItemKind.Rule, Trigger = trigger, Action = action, CooldownMs = 0 };

    private static TriggerEffectAction Flash() => new() { EffectType = EffectTypes.Flash, DurationMs = 500 };
    private static TriggerEffectAction Subliminal() => new() { EffectType = EffectTypes.Subliminal, Text = "x", DurationMs = 500 };

    private sealed class FakeSource : IPlaybackTimeSource
    {
        private double _t;
        public event Action<double>? PlaybackTimeChanged;
        public void Advance(double t) { _t = t; PlaybackTimeChanged?.Invoke(t); }
        public double GetCurrentTimeSeconds() => _t;
        public double GetDurationSeconds() => 60;
        public bool IsPlaying => true;
        public void Seek(double seconds) => _t = seconds;
        public void Pause() { }
        public void Play() { }
        public PlaybackRect VideoRect { get; set; } = PlaybackRect.Empty;
        public PlaybackRect GetVideoRect() => VideoRect;
    }

    private sealed class FakeDispatcher : IActionDispatcher, IEnhancementRunCleanup
    {
        public List<EnhancementAction> Fired { get; } = new();
        public List<string> Cleanups { get; } = new();

        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        {
            Fired.Add(action);
            return Task.CompletedTask;
        }

        public void ResetOverlayBands() => Cleanups.Add("reset-bands");
        public void StopOneShotFlashes() => Cleanups.Add("stop-flashes");
        public void StopOneShotSubliminals() => Cleanups.Add("stop-subliminals");
    }

    private sealed class FakeWebcam : IEnhancementWebcam, IDisposable
    {
        public event Action? OnBlink;
        public event Action? OnMouthOpen;
        public event Action<double, double>? OnGazeMove;
        public event Action? OnFaceLost;
        public event Action? OnFaceFound;

        public int DisposeCalls { get; private set; }
        public bool HadListenersAtDispose { get; private set; }
        public void Dispose()
        {
            DisposeCalls++;
            HadListenersAtDispose = OnBlink != null || OnMouthOpen != null || OnGazeMove != null
                || OnFaceLost != null || OnFaceFound != null;
        }

        public bool HasBlinkListener => OnBlink != null;
        public void Blink() => OnBlink?.Invoke();
    }
}
