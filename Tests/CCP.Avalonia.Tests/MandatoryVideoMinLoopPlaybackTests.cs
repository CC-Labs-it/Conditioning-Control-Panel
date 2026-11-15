using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

public class MandatoryVideoMinLoopPlaybackTests
{
    [Theory]
    [InlineData(true, false, 3)]
    [InlineData(false, true, 3)]
    [InlineData(false, false, 1)]
    public Task Loop_and_fill_set_one_repeat_run(bool loop, bool fill, int passes) => Run(loop, fill, (o, v, clock, decoder) =>
    {
        Assert.Equal(passes, o.LoopPasses);
        o.GuardTick();
        clock.Advance(15000);
        o.FrameTs = System.Diagnostics.Stopwatch.GetTimestamp();
        o.GuardTick();
        Assert.Equal(passes > 1, v.IsPlaying); // one pass + 5 seconds ends at 15 s
        if (passes > 1)
        {
            clock.Advance(20000);
            o.GuardTick();
            Assert.False(v.IsPlaying); // three passes + 5 seconds
        }
    });

    [Fact]
    public Task Repeats_finish_once_and_cap_cuts_the_run() => Run(true, false, (o, v, clock, decoder) =>
    {
        Assert.Equal(3, o.LoopPasses);
        o.OnNaturalVideoEnd();
        Assert.False(v.IsPlaying);
        Assert.True(decoder.Stopped);
        o.OnNaturalVideoEnd(); // no second verdict
        Assert.False(v.IsPlaying);
    });

    [Fact]
    public Task Maximum_cap_ends_a_repeating_run() => Run(true, false, (o, v, clock, decoder) =>
    {
        Assert.Equal(3, o.LoopPasses);
        CoreSettings.Current.VideoMaxDurationSeconds = 5;
        o.PlayedSkew = 5;
        o.GuardTick();
        Assert.False(v.IsPlaying);
        Assert.True(decoder.Stopped);
    }, cap: 5);

    [Fact]
    public Task Credited_and_enhanced_clips_do_not_repeat() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-loop-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clip.mp4");
        File.WriteAllText(Path.Combine(dir, "clip.ccpenh.json"), "{}");
        var s = CoreSettings.Current;
        var old = (s.MandatoryVideoLoopShortClips, s.VideoMinDurationSeconds, s.VideoEnhanceIfPossible, s.MandatoryVideoFloating);
        var o = new MandatoryVideoOverlay(() => new Decoder());
        o.Scheduler = new MandatoryVideoScheduler(o) { DurationOf = _ => 10 };
        try
        {
            (s.MandatoryVideoLoopShortClips, s.VideoMinDurationSeconds, s.VideoEnhanceIfPossible, s.MandatoryVideoFloating) = (true, 25, true, false);
            o.Show(path, false);
            Assert.Equal(1, o.LoopPasses);
            o.CloseAll();
            s.VideoEnhanceIfPossible = false;
            ScrolllerItemIndex.Remember(path, new ScrolllerCacheMeta("clip", "test"));
            o.Show(path, false);
            Assert.Equal(1, o.LoopPasses);
        }
        finally
        {
            o.CloseAll();
            (s.MandatoryVideoLoopShortClips, s.VideoMinDurationSeconds, s.VideoEnhanceIfPossible, s.MandatoryVideoFloating) = old;
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Loop_window_is_passive_and_platform_failure_hides_it() => Run(true, false, (o, v, clock, decoder) =>
    {
        bool called = false;
        o.MakeLoopPassive = _ => { called = true; return false; };
        var surface = o.Build(null, false);
        try
        {
            Assert.True(called);
            Assert.False(surface.Window.ShowActivated);
            Assert.False(surface.Window.Focusable);
            Assert.False(surface.Window.IsHitTestVisible);
            Assert.False(surface.Window.IsVisible);
        }
        finally { surface.Window.Close(); }
    });

    [Fact]
    public Task Panic_mid_loop_stops_the_decoder_immediately() => Run(true, false, (o, v, clock, decoder) =>
    {
        var singleton = MandatoryVideoOverlay.Instance;
        var previous = singleton.Scheduler;
        try
        {
            singleton.Scheduler = v;
            Assert.Equal(3, o.LoopPasses);
            PanicSurfaces.All.Single(x => x.Id == "mandatory-video").Stop(null);
            Assert.False(v.IsPlaying);
            Assert.True(decoder.Stopped);
        }
        finally { singleton.Scheduler = previous; }
    });

    [Fact]
    public Task Passive_loop_does_not_arm_unclickable_attention_targets() => Run(true, false, (o, v, clock, decoder) =>
    {
        Assert.Equal(3, o.LoopPasses);
        Assert.False(o.AttentionArmed);
    }, attention: true);

    [Fact]
    public Task Controls_load_save_and_are_session_owned() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.MandatoryVideoLoopShortClips, s.MandatoryVideoFillShortWithOthers);
        try
        {
            (s.MandatoryVideoLoopShortClips, s.MandatoryVideoFillShortWithOthers) = (true, false);
            var card = new VideoFeatureControl();
            var loop = card.FindControl<CheckBox>("ChkVideoLoopShort")!;
            var fill = card.FindControl<CheckBox>("ChkVideoFillShort")!;
            Assert.True(loop.IsChecked); Assert.False(fill.IsChecked);
            Assert.True(SessionLock.GetOwned(loop)); Assert.True(SessionLock.GetOwned(fill));
            loop.IsChecked = false; fill.IsChecked = true;
            Assert.False(s.MandatoryVideoLoopShortClips); Assert.True(s.MandatoryVideoFillShortWithOthers);
        }
        finally { (s.MandatoryVideoLoopShortClips, s.MandatoryVideoFillShortWithOthers) = old; }
        return Task.CompletedTask;
    });

    private static Task Run(bool loop, bool fill, Action<MandatoryVideoOverlay, MandatoryVideoScheduler, Clock, Decoder> action, int cap = 0, bool attention = false)
        => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.MandatoryVideoLoopShortClips, s.MandatoryVideoFillShortWithOthers, s.VideoMinDurationSeconds,
            s.VideoMaxDurationSeconds, s.VideoCutRandomStart, s.MandatoryVideoFloating, s.VideoEnhanceIfPossible, s.AttentionChecksEnabled);
        var post = CoreDispatch.PostProvider;
        var clock = new Clock();
        var decoder = new Decoder();
        var o = new MandatoryVideoOverlay(() => decoder) { Time = clock };
        var v = new MandatoryVideoScheduler(o, clock, () => new[] { "/test/loop.mp4" }) { DurationOf = _ => 10 };
        o.Scheduler = v;
        try
        {
            (s.MandatoryVideoLoopShortClips, s.MandatoryVideoFillShortWithOthers, s.VideoMinDurationSeconds,
                s.VideoMaxDurationSeconds, s.VideoCutRandomStart, s.MandatoryVideoFloating, s.VideoEnhanceIfPossible, s.AttentionChecksEnabled) = (loop, fill, 25, cap, false, false, false, attention);
            CoreDispatch.PostProvider = a => a();
            Assert.True(v.Trigger(false, "/test/loop.mp4"));
            clock.Fire();
            action(o, v, clock, decoder);
        }
        finally
        {
            v.Stop(); CoreDispatch.PostProvider = post;
            (s.MandatoryVideoLoopShortClips, s.MandatoryVideoFillShortWithOthers, s.VideoMinDurationSeconds,
                s.VideoMaxDurationSeconds, s.VideoCutRandomStart, s.MandatoryVideoFloating, s.VideoEnhanceIfPossible, s.AttentionChecksEnabled) = old;
        }
        return Task.CompletedTask;
    });

    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 10000;
        public bool IsPlaying => !Stopped;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public bool Stopped;
        public void SetPause(bool paused) { }
        public void Stop() => Stopped = true;
        public void Dispose() { }
    }
    private sealed class Clock : TimeProvider
    {
        private long _ms;
        private ManualTimer? _timer;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _ms;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_ms);
        public void Advance(int ms) => _ms += ms;
        public void Fire() => _timer!.Fire();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => _timer = new ManualTimer(() => callback(state));
        private sealed class ManualTimer(Action fire) : ITimer
        {
            public void Fire() => fire();
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }
}
