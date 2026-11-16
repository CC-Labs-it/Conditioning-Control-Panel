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
        public bool? AudioOff;
        public void SetPause(bool paused) => Paused = paused;
        public void Stop() => Stopped = true;
        public void Dispose() { }
        public readonly List<bool> AudioCalls = new();
        public void SetAudioOff(bool off) { AudioOff = off; AudioCalls.Add(off); }
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

    [Fact]
    public Task AI_play_video_refuses_while_a_floating_run_plays() => Run((o, _) =>
    {
        var s = CoreSettings.Current;
        var saved = (CoreEngine.Video, CoreAccount.HasLabAccessProvider, s.CompanionPrompt.AllowAiToControlEffects);
        var seeded = ConditioningControlPanel.Services.Commands.MediaCommand.VideoSurface;
        (CoreEngine.Video, CoreAccount.HasLabAccessProvider, s.CompanionPrompt.AllowAiToControlEffects) = (o.Scheduler, () => true, true);
        global::ConditioningControlPanel.Avalonia.CompanionEffects.StartEffect = start => start();
        try
        {
            // The surface itself, never Seed(): Seed swaps process-wide command delegates other tests rely on.
            Assert.False(global::ConditioningControlPanel.Avalonia.CompanionEffects.PlayVideo("x"));   // WPF App.AiEffects.cs:69-74
            Assert.Equal(1, o.FloatingUp);
            // Held behind the portal bind while the video starts: the released start still refuses.
            System.Action? held = null;
            global::ConditioningControlPanel.Avalonia.CompanionEffects.StartEffect = start => held = start;
            o.Scheduler.ForceCleanup();
            global::ConditioningControlPanel.Avalonia.CompanionEffects.PlayVideo("x");
            Assert.True(o.Scheduler.Trigger(userInitiated: false));
            o.Show("/v/first.mp4", strict: false);
            held!();
            Assert.Equal(1, o.FloatingUp);
            Assert.Same(seeded, ConditioningControlPanel.Services.Commands.MediaCommand.VideoSurface);   // no process-wide seeding left behind
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.CompanionEffects.StartEffect = global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.StartEffect;
            (CoreEngine.Video, CoreAccount.HasLabAccessProvider, s.CompanionPrompt.AllowAiToControlEffects) = saved;
        }
    });

    // WPF VideoService.FloatingInstances.cs:534-535: a veiled window is muted before it can play; focus comes at the reveal.
    [Fact]
    public Task A_joined_player_is_silent_before_its_reveal() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger();
        Assert.True(decoders[1].AudioCalls[0], "muted before the reveal's focus pass");
        Assert.False(decoders[1].AudioOff, "then the newest takes the sound");
    });

    // WPF VideoService.cs:6444-6456: a Resume card on every screen holding a floating window, kept when its host closes.
    [Fact]
    public Task Grace_shows_a_card_per_screen_and_keeps_one_when_a_window_closes() => Run((o, _) =>
    {
        o.Scheduler.Trigger(userInitiated: true);
        o.Scheduler.Trigger(userInitiated: true);
        o.OnNaturalVideoEnd();   // the first window goes: the joined ones carry the run
        o.Scheduler.Trigger(userInitiated: true);
        var f = o.Floaters;
        Assert.Equal(3, f.Count);
        OnOtherScreen(f[2].Surface);
        Assert.True(o.TryGracePause(fromPanicKey: false));
        Assert.Equal(new[] { true, false, true }, f.Select(x => x.Surface.GraceHost != null));   // one card per screen

        var host = f[0].Window;
        f[0].Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // the card's host closes mid-pause
        Assert.False(host.IsVisible);
        Assert.True(o.GracePaused);
        Assert.True(f[0].Surface.GraceHost?.IsVisible, "its screen keeps a Resume card");
    });

    private static void OnOtherScreen(MandatoryVideoOverlay.Surface x)
    {
        // Screen has no public constructor (ChaosDvdOverlayTests.MakeScreen).
        var other = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(5000), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(other, new PixelRect(5000, 0, 1920, 1080));
        typeof(MandatoryVideoOverlay.Surface).GetField("<Screen>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(x, other);
    }

    // WPF VideoService.cs:6325-6335: the resume that ends a grace pause closes every screen's card.
    [Fact]
    public Task Closing_the_first_window_mid_grace_closes_the_card_on_every_screen() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger(userInitiated: true);
        OnOtherScreen(o.Floaters[0].Surface);
        Assert.True(o.TryGracePause(fromPanicKey: false));
        var card = o.Floaters[0].Surface.GraceHost;
        Assert.True(card?.IsVisible, "the other screen has its own card");
        Assert.NotNull(o.Surfaces[0].GraceHost);

        o.Surfaces[0].Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(o.GracePaused);
        Assert.False(card!.IsVisible, "no Resume card is left behind");
        Assert.Null(o.Floaters[0].Surface.GraceHost);
        Assert.False(o.Floaters[0].Surface.Grace.IsVisible);
        Assert.False(decoders[1].Paused, "the joined window plays on");
    });

    // WPF FloatingInstances.cs:534-535: a veiled player stays silent; LibVLC chooses its audio track only after Play.
    [Fact]
    public void A_silenced_player_drops_an_audio_track_chosen_after_the_mute()
    {
        var track = -1;
        var focus = new AudioTrackFocus(() => track, id => { track = id; return true; }, () => new[] { -1, 2 });
        focus.Set(true);   // before Play: no track yet
        track = 2;         // LibVLC selects the clip's track
        focus.Selected();
        Assert.Equal(-1, track);
        focus.Set(false);  // it takes the sound
        Assert.Equal(2, track);
        focus.Selected();
        Assert.Equal(2, track);
    }
}
