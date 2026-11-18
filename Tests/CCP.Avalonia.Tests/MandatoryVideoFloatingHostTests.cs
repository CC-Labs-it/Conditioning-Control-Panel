using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF VideoService.FloatingInstances.cs: the multi-window floating host, behind the decoder seam.</summary>
public sealed class MandatoryVideoFloatingHostTests
{
    private sealed class StillTime : TimeProvider, ITimer
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => this;
        public bool Change(TimeSpan due, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 120000;
        public bool IsPlaying => !Paused && !Stopped;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public bool Paused, Stopped;
        public void SetPause(bool paused) => Paused = paused;
        public void Stop() => Stopped = true;
        public void Dispose() { }
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
        Assert.True(o.Scheduler.IsPlaying);

        var second = o.Floaters[0];
        second.Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Single(Up(o));
        Assert.True(o.FloatingUp > 0);

        o.Floaters[0].Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(Up(o));
        Assert.Equal(0, o.FloatingUp);
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
    });

    [Fact]
    public Task Closing_the_first_window_in_a_grace_pause_resumes_the_others() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger(userInitiated: true);
        Assert.True(o.TryGracePause(fromPanicKey: false));
        o.Surfaces[0].Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Single(Up(o));
        Assert.False(decoders[1].Paused, "the joined window plays on");
        Assert.Equal(GraceDecision.FallThrough, o.EvaluateGraceRequest(true, double.MaxValue));   // the grace pause stays spent
    });

    [Fact]
    public Task A_Lockdown_hides_and_refuses_the_joined_windows_close_button() => Run((o, _) =>
    {
        var (previous, lockdown) = (LockdownService.Current, LockdownService.Current = new LockdownService());
        try
        {
            o.Scheduler.Trigger(userInitiated: true);
            var close = o.Floaters[0].Surface.Buttons.Close;
            Assert.True(close.IsVisible);
            lockdown.Activate(TimeSpan.FromMinutes(30));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(close.IsVisible);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // refused: the window stays
            Assert.Equal(2, Up(o).Count);
        }
        finally { lockdown.Deactivate(); lockdown.Dispose(); LockdownService.Current = previous; }
    });
}
