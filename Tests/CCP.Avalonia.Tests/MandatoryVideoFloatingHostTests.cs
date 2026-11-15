using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF VideoService.FloatingInstances.cs: the multi-window floating host, behind the decoder seam.</summary>
public sealed class MandatoryVideoFloatingHostTests
{
    private sealed class StillTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => new Never();
        private sealed class Never : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 120000;
        public bool IsPlaying => !Paused && !Stopped;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public bool Paused, Stopped;
        public bool? AudioOff;
        public void SetPause(bool paused) => Paused = paused;
        public void Stop() => Stopped = true;
        public void Dispose() { }
        public void SetAudioOff(bool off) => AudioOff = off;
    }

    /// <summary>Always the same spot: only the "taken" rule keeps windows apart.</summary>
    private sealed class SameSpot : Random
    {
        public override int Next(int minValue, int maxValue) => minValue;
        public override int Next(int maxValue) => 0;
        public override double NextDouble() => 0;
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Task Run(Action<MandatoryVideoOverlay, List<Decoder>> body, bool together = false, bool keepSound = false) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var saved = (s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether, s.MandatoryVideoFloatingKeepSound,
                s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled);
            (s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether, s.MandatoryVideoFloatingKeepSound,
                s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled)
                = (true, 3, together, keepSound, false, false, false, false, 0, false);
            var decoders = new List<Decoder>();
            var o = new MandatoryVideoOverlay(() => { var d = new Decoder(); decoders.Add(d); return d; });
            o.Scheduler = new MandatoryVideoScheduler(o, new StillTime(), () => new[] { "/v/a.mp4", "/v/b.mp4", "/v/c.mp4", "/v/d.mp4", "/v/e.mp4" });
            try
            {
                // The run's first window: the scheduler accepts, its pre-roll then shows the clip.
                Assert.True(o.Scheduler.Trigger(userInitiated: false));
                o.Show("/v/first.mp4", strict: false);
                body(o, decoders);
            }
            finally
            {
                o.Scheduler.ForceCleanup();
                o.CloseAll();
                (s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether, s.MandatoryVideoFloatingKeepSound,
                    s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled) = saved;
            }
            return Task.CompletedTask;
        });

    private static List<Window> Up(MandatoryVideoOverlay o) => o.Windows.Where(w => w.IsVisible).ToList();

    /// <summary>Timers wait for <see cref="FireLast"/>: the most recently armed one fires.</summary>
    private sealed class Clock : TimeProvider
    {
        public readonly List<Timer> Armed = new();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => new Timer(this, () => callback(state));
        public void FireLast() { var t = Armed[^1]; Armed.Remove(t); t.Fire(); }
        public sealed class Timer(Clock clock, Action fire) : ITimer
        {
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period)
            {
                clock.Armed.Remove(this);
                if (due != Timeout.InfiniteTimeSpan) clock.Armed.Add(this);
                return true;
            }
            public void Dispose() => clock.Armed.Remove(this);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    /// <summary>WPF VideoService.cs:2940-2946 + VideoService.FloatingInstances.cs:117,250 (SchedulerMayTrigger):
    /// a scheduler tick during an open floating run with room opens one more window through the real host.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Scheduler_ticks_join_the_open_run_until_the_limit_and_panic_closes_all(bool together) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var saved = (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether,
                s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled);
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether,
                s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled)
                = (true, 20, true, 3, together, false, false, false, false, 0, false);
            var post = CoreDispatch.PostProvider;
            var clock = new Clock();
            var decoders = new List<Decoder>();
            var o = new MandatoryVideoOverlay(() => { var d = new Decoder(); decoders.Add(d); return d; });
            o.Scheduler = new MandatoryVideoScheduler(o, clock, () => new[] { "/v/a.mp4", "/v/b.mp4", "/v/c.mp4", "/v/d.mp4" });
            try
            {
                CoreDispatch.PostProvider = a => a();
                o.Scheduler.Start();
                clock.FireLast();   // the schedule fires: pre-roll
                clock.FireLast();   // pre-roll ends: the run's first window
                Assert.True(o.FloatingRunOpen);
                if (together)
                {
                    Assert.Equal(3, Up(o).Count);
                    o.Floaters[0].Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // room again
                    Assert.Equal(2, Up(o).Count);
                    clock.FireLast();   // the next tick
                    Assert.Equal(2, Up(o).Count);   // Pop up together: the run waits to end, no join
                }
                else
                {
                    Assert.Single(Up(o));
                    clock.FireLast();   // join tick
                    Assert.Equal(2, Up(o).Count);
                    clock.FireLast();
                    Assert.Equal(3, Up(o).Count);
                    clock.FireLast();   // at the limit: skipped
                    Assert.Equal(3, Up(o).Count);
                    Assert.Equal(3, decoders.Count);
                }
                o.Scheduler.ForceCleanup();   // the engine stop a panic runs
                Assert.Empty(Up(o));
                Assert.Equal(0, o.FloatingUp);
                Assert.Empty(clock.Armed);
            }
            finally
            {
                o.Scheduler.Stop();
                o.CloseAll();
                CoreDispatch.PostProvider = post;
                (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether,
                    s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled) = saved;
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Test_presses_add_windows_up_to_the_limit_then_replace_the_oldest() => Run((o, decoders) =>
    {
        Assert.Single(Up(o));
        var first = o.Windows[0];
        Assert.True(o.Scheduler.Trigger(userInitiated: true));
        Assert.True(o.Scheduler.Trigger(userInitiated: true));
        Assert.Equal(3, Up(o).Count);
        Assert.Equal(3, o.FloatingUp);

        Assert.True(o.Scheduler.Trigger(userInitiated: true));   // Test at the limit: the oldest makes room
        var up = Up(o);
        Assert.Equal(3, up.Count);
        Assert.DoesNotContain(first, up);
        Assert.False(first.IsVisible);
        Assert.True(decoders[0].Stopped, "the replaced window's player is stopped");
        Assert.Equal(4, decoders.Count);
        Assert.True(o.Scheduler.IsPlaying);
    });

    [Fact]
    public Task Automatic_triggers_join_up_to_the_limit_and_a_fourth_is_skipped() => Run((o, decoders) =>
    {
        Assert.True(o.FloatingRunOpen);
        Assert.True(o.Scheduler.Trigger());
        Assert.True(o.Scheduler.Trigger());
        Assert.Equal(3, Up(o).Count);
        Assert.True(o.Scheduler.Trigger());   // automatic at the limit: a quiet skip
        Assert.Equal(3, Up(o).Count);
        Assert.Equal(3, decoders.Count);
        Assert.False(decoders.Any(d => d.Stopped));
    });

    [Fact]
    public Task Pop_up_together_opens_every_free_slot_at_once() => Run((o, _) =>
    {
        Assert.Equal(3, Up(o).Count);
        Assert.Equal(2, o.Floaters.Select(f => f.Clip).Distinct().Count());
    }, together: true);

    [Fact]
    public Task No_window_lands_exactly_on_another() => Run((o, _) =>
        {
            typeof(MandatoryVideoOverlay).GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, new SameSpot());
            o.Scheduler.Trigger(userInitiated: true);
            o.Scheduler.Trigger(userInitiated: true);
            var spots = o.Windows.Select(w => w.Position).ToList();
            Assert.Equal(3, spots.Count);
            for (var i = 0; i < spots.Count; i++)
                for (var j = i + 1; j < spots.Count; j++)
                    Assert.False(Math.Abs(spots[i].X - spots[j].X) < MandatoryVideoPresentation.PileOffset
                        && Math.Abs(spots[i].Y - spots[j].Y) < MandatoryVideoPresentation.PileOffset,
                        $"window {j} is on top of window {i}: {spots[i]} / {spots[j]}");
        });

    [Fact]
    public Task Closing_one_clip_leaves_the_others_and_the_run_ends_with_the_last() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger(userInitiated: true);
        o.Scheduler.Trigger(userInitiated: true);
        var first = o.Windows[0];

        o.OnNaturalVideoEnd();   // the first clip ends: only its window goes
        Assert.False(first.IsVisible);
        Assert.Equal(2, Up(o).Count);
        Assert.True(decoders[0].Stopped);
        Assert.False(decoders[1].Stopped || decoders[2].Stopped);
        Assert.True(o.FloatingUp > 0);
        Assert.True(o.FloatingRunOpen);
        Assert.True(o.Scheduler.IsPlaying);

        var second = o.Floaters[0];
        second.Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Single(Up(o));
        Assert.True(o.FloatingUp > 0);

        o.Floaters[0].Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(Up(o));
        Assert.Equal(0, o.FloatingUp);
        Assert.False(o.FloatingRunOpen);
        Assert.False(o.Scheduler.IsPlaying, "the last window ends the run");
    });

    [Fact]
    public Task Grace_holds_every_window_and_panic_closes_them_all_with_the_card() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger(userInitiated: true);
        o.Scheduler.Trigger(userInitiated: true);
        Assert.True(o.TryGracePause(fromPanicKey: false));
        Assert.All(decoders, d => Assert.True(d.Paused));
        var card = o.Surfaces[0].GraceHost;
        Assert.NotNull(card);
        o.Scheduler.Trigger(userInitiated: true);   // no window joins or replaces while the run is held
        Assert.Equal(3, decoders.Count);

        o.Scheduler.ForceCleanup();   // the engine stop a panic runs
        Assert.Empty(o.Windows);
        Assert.False(card!.IsVisible);
        Assert.All(decoders, d => Assert.True(d.Stopped));
        Assert.Equal(0, o.FloatingUp);
        Assert.False(o.FloatingRunOpen);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Exactly_one_window_has_the_sound_and_a_click_takes_it(bool keepSound) => Run((o, decoders) =>
    {
        o.Scheduler.Trigger();
        o.Scheduler.Trigger();
        Assert.Single(decoders, d => d.AudioOff != true);
        // The newest takes the sound; with Keep sound the first window keeps it.
        Assert.Same(keepSound ? decoders[0] : decoders[2], decoders.Single(d => d.AudioOff != true));

        var middle = o.Floaters[0].Window;
        middle.MouseDown(new Point(5, 5), MouseButton.Left);
        middle.MouseUp(new Point(5, 5), MouseButton.Left);
        Assert.Same(decoders[1], decoders.Single(d => d.AudioOff != true));
        Assert.True(o.HasFloatingSound(middle));

        o.Floaters[0].Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // the holder goes
        Assert.Single(decoders.Where(d => !d.Stopped), d => d.AudioOff != true);
    }, keepSound: keepSound);
}
