using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosVideoRestackTests
{
    [Fact]
    public async Task Cursor_halo_is_restacked_between_announcer_and_estim_when_pinned()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var pin = settings.ChaosPinOnTop;
            var announcerEnabled = settings.ChaosAnnouncerEnabled;
            var oldRaise = ChaosRunHost.RaiseGameWindow;
            var oldMake = ChaosCursorGlowOverlay.MakeClickThrough;
            var oldRead = ChaosCursorGlowOverlay.ReadPointer;
            var oldCanShow = ChaosEStimOverlay.CanShow;
            var oldSkip = ChaosEStimOverlay.SkipPlatformChecksForTests;
            var raises = new List<Window>();
            try
            {
                settings.ChaosPinOnTop = true;
                settings.ChaosAnnouncerEnabled = true;
                ChaosRunHost.RaiseGameWindow = raises.Add;
                ChaosCursorGlowOverlay.MakeClickThrough = _ => true;
                ChaosCursorGlowOverlay.ReadPointer = () => (new PixelPoint(100, 100), false, false);
                ChaosEStimOverlay.CanShow = () => true;
                ChaosEStimOverlay.SkipPlatformChecksForTests = true;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                ChaosAnnouncerOverlay.Announce("test", ChaosAnnounceKind.Mantra);
                ChaosCursorGlowOverlay.Arm();
                ChaosEStimOverlay.Strike(new[] { (new PixelPoint(0, 0), new PixelPoint(10, 10)) });
                var announcer = Assert.IsType<ChaosAnnouncerOverlay>(ChaosAnnouncerOverlay.Active);
                var halo = Assert.IsType<ChaosCursorGlowOverlay>(ChaosCursorGlowOverlay.Active);
                var estim = Assert.IsType<ChaosEStimOverlay>(ChaosEStimOverlay.Active);
                ChaosRunHost.RaiseGameLayerAboveVideo();
                Assert.Contains(announcer, raises);
                Assert.Contains(halo, raises);
                Assert.Contains(estim, raises);
                Assert.True(raises.IndexOf(announcer) < raises.IndexOf(halo));
                Assert.True(raises.IndexOf(halo) < raises.IndexOf(estim));
                raises.Clear();
                settings.ChaosPinOnTop = false;
                ChaosRunHost.RaiseGameLayerAboveVideo();
                Assert.Empty(raises);
                settings.ChaosPinOnTop = true;
                ChaosCursorGlowOverlay.CloseActive();
                ChaosRunHost.RaiseGameLayerAboveVideo();
                Assert.DoesNotContain(halo, raises);
                Assert.Contains(announcer, raises);
                Assert.Contains(estim, raises);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosAnnouncerOverlay.CloseActive();
                ChaosCursorGlowOverlay.CloseActive();
                ChaosEStimOverlay.CloseActive();
                settings.ChaosPinOnTop = pin;
                settings.ChaosAnnouncerEnabled = announcerEnabled;
                ChaosRunHost.RaiseGameWindow = oldRaise;
                ChaosCursorGlowOverlay.MakeClickThrough = oldMake;
                ChaosCursorGlowOverlay.ReadPointer = oldRead;
                ChaosEStimOverlay.CanShow = oldCanShow;
                ChaosEStimOverlay.SkipPlatformChecksForTests = oldSkip;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Video_start_settle_and_click_restack_only_a_pinned_run_in_order()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var pin = settings.ChaosPinOnTop;
            var oldRaise = ChaosRunHost.RaiseGameWindow;
            var oldSchedule = MandatoryVideoOverlay.ScheduleGameRestack;
            var raises = new List<Window>();
            var scheduled = new List<(Action Action, TimeSpan Delay)>();
            try
            {
                settings.ChaosPinOnTop = true;
                ChaosRunHost.RaiseGameWindow = raises.Add;
                MandatoryVideoOverlay.ScheduleGameRestack = (a, d) => scheduled.Add((a, d));
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 20, 500);
                MandatoryVideoOverlay.RestackOnVideoStart();
                Assert.Empty(raises); // even a visible field is not raised outside a run
                scheduled.Clear();
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 20, 500);
                var field = ChaosFieldFxOverlay.Active!;
                var hud = ChaosRunHost.Hud!;
                var boon = ChaosRunHost.BoonBar!;
                MandatoryVideoOverlay.RestackOnVideoStart();
                Assert.Contains(field, raises);
                Assert.True(raises.IndexOf(field) < raises.IndexOf(boon));
                Assert.True(raises.IndexOf(boon) < raises.IndexOf(hud));
                var first = raises.ToArray();
                Assert.Equal(TimeSpan.FromMilliseconds(60), scheduled[0].Delay);
                raises.Clear(); scheduled[0].Action();
                Assert.Equal(first, raises);
                scheduled.Clear(); raises.Clear();
                MandatoryVideoOverlay.RestackOnVideoClick();
                Assert.Empty(raises);
                Assert.Equal(TimeSpan.FromMilliseconds(50), scheduled[0].Delay);
                scheduled[0].Action();
                Assert.Equal(first, raises);
                raises.Clear(); settings.ChaosPinOnTop = false;
                scheduled[0].Action();
                MandatoryVideoOverlay.RestackOnVideoStart();
                Assert.Empty(raises);
                settings.ChaosPinOnTop = true;
                ChaosRunHost.ForceShutdown();
                scheduled[0].Action();
                Assert.Empty(raises);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                settings.ChaosPinOnTop = pin;
                ChaosRunHost.RaiseGameWindow = oldRaise;
                MandatoryVideoOverlay.ScheduleGameRestack = oldSchedule;
            }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task Video_show_and_pointer_press_raise_attention_after_gameplay()
    {
        // No skip: CI installs libvlc on Linux (build.yml:330) and the Windows head bundles it.
        _ = new LibVlcAudio("--aout=dummy");
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var pin = settings.ChaosPinOnTop;
            var oldRaise = ChaosRunHost.RaiseGameWindow;
            var oldSchedule = MandatoryVideoOverlay.ScheduleGameRestack;
            var oldPrepare = MandatoryVideoOverlay.PrepareAttentionWindow;
            var oldRegion = MandatoryVideoOverlay.SetAttentionRegion;
            var oldAttentionRaise = MandatoryVideoOverlay.RaiseAttentionWindow;
            var raises = new List<Window>();
            var scheduled = new List<(Action Action, TimeSpan Delay)>();
            var regions = new List<PixelRect[]>();
            var o = MandatoryVideoOverlay.Instance;
            try
            {
                settings.ChaosPinOnTop = true;
                ChaosRunHost.RaiseGameWindow = raises.Add;
                MandatoryVideoOverlay.RaiseAttentionWindow = raises.Add;
                MandatoryVideoOverlay.ScheduleGameRestack = (a, d) => scheduled.Add((a, d));
                MandatoryVideoOverlay.PrepareAttentionWindow = (_, _) => true;
                MandatoryVideoOverlay.SetAttentionRegion = (_, rects) => { regions.Add(rects); return true; };
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 20, 500);
                o.Show(Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), false);
                Assert.Contains(ChaosFieldFxOverlay.Active!, raises);
                Assert.Contains(scheduled, a => a.Delay == TimeSpan.FromMilliseconds(60));
                var video = Assert.Single(o.Windows);
                o.Spawn(0);
                var surface = Assert.Single(o.Surfaces);
                var attention = Assert.IsType<Window>(surface.AttentionWindow);
                Assert.Same(surface.Layer, attention.Content);
                Assert.False(attention.ShowActivated);
                var region = Assert.Single(regions[^1]);
                var firstTarget = Assert.IsType<Border>(Assert.Single(surface.Layer.Children));
                Assert.True(region.Right >= Math.Ceiling(Canvas.GetLeft(firstTarget) + firstTarget.Width));
                Assert.True(region.Bottom >= Math.Ceiling(Canvas.GetTop(firstTarget) + firstTarget.Height));
                Assert.Equal(attention, raises[^1]);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var paused = typeof(MandatoryVideoOverlay).GetField("_gracePaused", flags)!;
                var sync = typeof(MandatoryVideoOverlay).GetMethod("SyncAttentionRegion", flags)!;
                paused.SetValue(o, true); sync.Invoke(o, new object[] { surface });
                Assert.False(surface.Layer.IsVisible);
                Assert.Empty(regions[^1]);
                paused.SetValue(o, false); sync.Invoke(o, new object[] { surface });
                Assert.True(surface.Layer.IsVisible);
                Assert.Single(regions[^1]);
                var settle = Assert.Single(scheduled);
                raises.Clear(); settle.Action();
                Assert.Equal(attention, raises[^1]);
                Assert.Contains(ChaosRunHost.Hud!, raises);
                raises.Clear(); scheduled.Clear();
                video.MouseDown(new Point(2, 2), MouseButton.Left);
                video.MouseUp(new Point(2, 2), MouseButton.Left);
                var click = Assert.Single(scheduled);
                Assert.Equal(TimeSpan.FromMilliseconds(50), click.Delay);
                click.Action();
                Assert.Contains(ChaosRunHost.Hud!, raises);
                Assert.Equal(attention, raises[^1]);
                Assert.True(raises.IndexOf(ChaosRunHost.Hud!) < raises.IndexOf(attention));
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var target = Assert.IsType<Border>(Assert.Single(surface.Layer.Children));
                var at = new Point(Canvas.GetLeft(target) + target.Width / 2, Canvas.GetTop(target) + target.Height / 2);
                attention.MouseDown(at, MouseButton.Left);
                attention.MouseUp(at, MouseButton.Left);
                Assert.Equal(0, o.LiveTargets);
                o.Step(settings.AttentionLifespan + 1, 0);
                // A caught target remains in the input region until its 320 ms fade completes.
                for (var i = 0; i < 100 && regions[^1].Length != 0; i++) await Task.Delay(20);
                Assert.Empty(regions[^1]);
                MandatoryVideoOverlay.SetAttentionRegion = (_, _) => false;
                paused.SetValue(o, true);
                o.Spawn(0);
                Assert.Null(surface.AttentionWindow);
                Assert.False(attention.IsVisible);
                Assert.True(surface.Layer.IsVisible);
                Assert.Contains(surface.Layer, ((Panel)video.Content!).Children);
                paused.SetValue(o, false);
                MandatoryVideoOverlay.PrepareAttentionWindow = (_, _) => false;
                o.Spawn(0);
                Assert.Null(surface.AttentionWindow);
                Assert.Contains(surface.Layer, ((Panel)video.Content!).Children);
                o.CloseAll();
                Assert.False(attention.IsVisible);
            }
            finally
            {
                o.CloseAll(); ChaosRunHost.ForceShutdown();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                settings.ChaosPinOnTop = pin;
                ChaosRunHost.RaiseGameWindow = oldRaise;
                MandatoryVideoOverlay.ScheduleGameRestack = oldSchedule;
                MandatoryVideoOverlay.PrepareAttentionWindow = oldPrepare;
                MandatoryVideoOverlay.SetAttentionRegion = oldRegion;
                MandatoryVideoOverlay.RaiseAttentionWindow = oldAttentionRaise;
            }
        });
    }

    [Theory]
    [InlineData(false, false)] // a target arrives after an ordinary video started
    [InlineData(false, true)]  // attention setup is refused before gameplay is raised
    [InlineData(true, true)]   // an input region fails after creation
    public async Task Video_entry_preserves_fallback_target_order_and_cancels_fades(bool regionFails, bool attentionEnabled)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.ChaosPinOnTop, s.AudioDuckingEnabled, s.AttentionChecksEnabled);
            var oldRaise = ChaosRunHost.RaiseGameWindow;
            var oldAttentionRaise = MandatoryVideoOverlay.RaiseAttentionWindow;
            var oldSchedule = MandatoryVideoOverlay.ScheduleGameRestack;
            var oldPrepare = MandatoryVideoOverlay.PrepareAttentionWindow;
            var oldRegion = MandatoryVideoOverlay.SetAttentionRegion;
            var oldPlayback = MandatoryVideoOverlay.StartPlaybackForTests;
            var raises = new List<Window>();
            var scheduled = new List<(Action Action, TimeSpan Delay)>();
            var o = MandatoryVideoOverlay.Instance;
            var regionCalls = 0;
            var starts = 0;
            try
            {
                (s.ChaosPinOnTop, s.AudioDuckingEnabled, s.AttentionChecksEnabled) = (true, false, attentionEnabled);
                ChaosRunHost.RaiseGameWindow = raises.Add;
                MandatoryVideoOverlay.RaiseAttentionWindow = raises.Add;
                MandatoryVideoOverlay.ScheduleGameRestack = (a, d) => scheduled.Add((a, d));
                MandatoryVideoOverlay.StartPlaybackForTests = () => starts++;
                MandatoryVideoOverlay.PrepareAttentionWindow = (_, _) => regionFails;
                MandatoryVideoOverlay.SetAttentionRegion = (_, _) => { regionCalls++; return regionFails; };
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                o.Show("fake-video", false);  // same public host entry used by the scheduler
                Assert.Equal(1, starts);
                Assert.Contains(ChaosRunHost.Hud!, raises); // no fallback target yet: gameplay above video
                Assert.Equal(TimeSpan.FromMilliseconds(60), Assert.Single(scheduled).Delay);
                var surface = Assert.Single(o.Surfaces);
                var video = surface.Window;
                raises.Clear(); scheduled.Clear();
                o.Spawn(0);
                if (regionFails)
                {
                    Assert.NotNull(surface.AttentionWindow);
                    raises.Clear();
                    MandatoryVideoOverlay.SetAttentionRegion = (_, _) => { regionCalls++; return false; };
                    o.Step(0, 0); // region fails after a target is already visible
                }
                Assert.Null(surface.AttentionWindow);
                Assert.Equal(video, raises[^1]);
                Assert.Contains(surface.Layer, ((Panel)video.Content!).Children);
                raises.Clear();
                video.MouseDown(new Point(2, 2), MouseButton.Left);
                video.MouseUp(new Point(2, 2), MouseButton.Left);
                var click = Assert.Single(scheduled);
                Assert.Equal(TimeSpan.FromMilliseconds(50), click.Delay);
                click.Action();
                Assert.DoesNotContain(ChaosRunHost.Hud!, raises);
                Assert.Equal(video, raises[^1]);
                raises.Clear();
                o.Step(s.AttentionLifespan + 1, 0);
                Assert.Contains(ChaosRunHost.Hud!, raises); // last fallback target retired
                Assert.DoesNotContain(video, raises);

                // Exercise a pending fade from a separate surface, then tear down immediately.
                o.CloseAll();
                MandatoryVideoOverlay.PrepareAttentionWindow = (_, _) => true;
                MandatoryVideoOverlay.SetAttentionRegion = (_, _) => { regionCalls++; return true; };
                o.Show("fake-video", false);
                o.Spawn(0);
                surface = Assert.Single(o.Surfaces);
                var attention = Assert.IsType<Window>(surface.AttentionWindow);
                raises.Clear(); scheduled.Clear();
                surface.Window.MouseDown(new Point(2, 2), MouseButton.Left);
                surface.Window.MouseUp(new Point(2, 2), MouseButton.Left);
                Assert.Single(scheduled).Action();
                Assert.Contains(ChaosRunHost.Hud!, raises);
                Assert.Equal(attention, raises[^1]);
                var target = Assert.Single(o.GazeTargets);
                target.Hit();
                Assert.Equal(0, o.LiveTargets);
                Assert.NotNull(target.OnRemoved);
                o.CloseAll();
                Assert.Null(target.OnRemoved);
                var callsAfterClose = regionCalls;
                var childrenAfterClose = surface.Layer.Children.Count;
                target.Remove(); // simulates a timer callback already queued during shutdown
                await Task.Delay(400);
                Assert.Equal(callsAfterClose, regionCalls);
                Assert.Equal(childrenAfterClose, surface.Layer.Children.Count);
            }
            finally
            {
                o.CloseAll(); ChaosRunHost.ForceShutdown();
                (s.ChaosPinOnTop, s.AudioDuckingEnabled, s.AttentionChecksEnabled) = saved;
                ChaosRunHost.RaiseGameWindow = oldRaise;
                MandatoryVideoOverlay.RaiseAttentionWindow = oldAttentionRaise;
                MandatoryVideoOverlay.ScheduleGameRestack = oldSchedule;
                MandatoryVideoOverlay.PrepareAttentionWindow = oldPrepare;
                MandatoryVideoOverlay.SetAttentionRegion = oldRegion;
                MandatoryVideoOverlay.StartPlaybackForTests = oldPlayback;
            }
        });
    }

}
