using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FloatingVideoRevealTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Theory]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Full, true)]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Reduced, false)]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Off, false)]
    public async Task Added_window_reveals_from_its_centre_only_at_full_motion(ConditioningControlPanel.Models.MotionLevel level, bool animates)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var settings = ConditioningControlPanel.CoreSettings.Current;
            var saved = (settings.MotionLevel, settings.GentleVisuals);
            var probe = ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe;
            var savedTime = BeatLoop.Time;
            var time = new SteppedTime();
            BeatLoop.Time = time;
            settings.MotionLevel = level;
            settings.GentleVisuals = false;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe = () => true;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.CacheStamp = long.MinValue;
            var picture = new Border { Background = Brushes.Black };
            var frame = new Border { Child = picture };
            var window = new Window { Width = 400, Height = 200, Position = new PixelPoint(100, 200), ShowActivated = false, Content = frame };
            var requests = new List<(PixelRect[]? Rects, bool BeforeShow, bool Sync)>();
            var reveal = new FloatingVideoReveal(window, animate: true)
            {
                InputRegionWriter = (w, rects, sync) => { requests.Add((rects, !w.IsVisible, sync)); return true; }
            };
            try
            {
                reveal.Present();
                Assert.Equal(animates, reveal.IsAnimating);
                window.UpdateLayout();
                Assert.Equal(400, window.Width, 3);
                Assert.Equal(200, window.Height, 3);
                Assert.Equal(new PixelPoint(100, 200), window.Position);
                var pictureBounds = picture.Bounds;
                Assert.Equal(new Size(400, 200), pictureBounds.Size);
                if (animates)
                {
                    Assert.Equal(new Rect(60, 30, 280, 140), Assert.IsType<RectangleGeometry>(frame.Clip).Rect);
                    Assert.Single(requests);
                    Assert.True(requests[0].BeforeShow);
                    Assert.True(requests[0].Sync);
                    Assert.Equal(new PixelRect(60, 30, 280, 140), Assert.Single(requests[0].Rects!));
                    // The reveal runs on the shared beat (AmbientBeatLoopsTests ratchet), not its own timer.
                    Assert.Contains(BeatLoop.Running, b => ReferenceEquals(b.Owner, frame));
                    time.Advance(TimeSpan.FromMilliseconds(130));
                    BeatLoop.StepAll();
                    window.UpdateLayout();
                    var midway = Assert.IsType<RectangleGeometry>(frame.Clip).Rect;
                    Assert.Equal(7.5, midway.X, 3);
                    Assert.Equal(3.75, midway.Y, 3);
                    Assert.Equal(385, midway.Width, 3);
                    Assert.Equal(192.5, midway.Height, 3);
                    Assert.Equal(new PixelRect(8, 4, 384, 192), Assert.Single(requests[^1].Rects!));
                    Assert.False(requests[^1].Sync);
                    Assert.Equal(pictureBounds, picture.Bounds);
                    Assert.Equal(400, window.Width, 3);
                    Assert.Equal(200, window.Height, 3);
                    Assert.Equal(new PixelPoint(100, 200), window.Position);
                    time.Advance(TimeSpan.FromMilliseconds(130));
                    BeatLoop.StepAll();
                    Assert.Null(frame.Clip);
                    Assert.DoesNotContain(BeatLoop.Running, b => ReferenceEquals(b.Owner, frame));
                    Assert.Null(requests[^1].Rects);
                    Assert.Equal(pictureBounds, picture.Bounds);
                    Assert.False(reveal.IsAnimating);
                }
                else { Assert.Null(frame.Clip); Assert.Empty(requests); }
            }
            finally
            {
                reveal.Close();
                BeatLoop.Time = savedTime;
                (settings.MotionLevel, settings.GentleVisuals) = saved;
                ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe = probe;
                ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.CacheStamp = long.MinValue;
            }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(1, 8, 4, 384, 192)]
    [InlineData(1.5, 12, 6, 576, 288)]
    [InlineData(2, 15, 8, 770, 384)]
    public void Input_region_uses_physical_pixels(double scale, int x, int y, int width, int height)
    {
        var clip = new Rect(7.5, 3.75, 385, 192.5);
        Assert.Equal(new PixelRect(x, y, width, height), FloatingVideoReveal.InputRect(clip, scale));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsupported_input_region_shows_the_whole_window(bool failAfterStart)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var settings = ConditioningControlPanel.CoreSettings.Current;
            var saved = (settings.MotionLevel, settings.GentleVisuals);
            var probe = ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe;
            settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            settings.GentleVisuals = false;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe = () => true;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.CacheStamp = long.MinValue;
            var frame = new Border();
            var window = new Window { Width = 400, Height = 200, Content = frame };
            var calls = new List<PixelRect[]?>();
            var reveal = new FloatingVideoReveal(window, animate: true)
            {
                InputRegionWriter = (_, rects, _) =>
                {
                    calls.Add(rects);
                    return rects == null || (failAfterStart && calls.Count == 1);
                }
            };
            try
            {
                reveal.Present();
                if (failAfterStart) { Assert.True(reveal.IsAnimating); reveal.RevealAt(130); }
                Assert.False(reveal.IsAnimating);
                Assert.True(window.IsVisible);
                Assert.Null(frame.Clip);
                Assert.Null(calls[^1]);
                var count = calls.Count;
                reveal.RevealAt(200);
                Assert.Equal(count, calls.Count);
            }
            finally
            {
                reveal.Close();
                (settings.MotionLevel, settings.GentleVisuals) = saved;
                ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe = probe;
                ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.CacheStamp = long.MinValue;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task No_pixels_or_clicks_until_a_frame_and_stop_cannot_be_undone_by_a_late_frame()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var window = new Window { ShowActivated = false };
            var reveal = new FloatingVideoReveal(window);
            try
            {
                Assert.False(window.IsVisible);
                reveal.Present();
                Assert.True(window.IsVisible);
                Assert.False(window.ShowActivated);
                reveal.Present();
                Assert.True(window.IsVisible);
                reveal.Close();
                reveal.Present();
                Assert.False(window.IsVisible);

                var stopped = new Window();
                var hidden = new FloatingVideoReveal(stopped);
                hidden.Close();
                hidden.Present();
                Assert.False(stopped.IsVisible);
            }
            finally { reveal.Close(); }
            return Task.CompletedTask;
        });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Veiled_audio_stays_silent_and_only_a_visible_frame_restores_current_volume(bool stopBeforeFrame)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var window = new Window { ShowActivated = false };
            var overlay = (MandatoryVideoOverlay)System.Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
            var settings = ConditioningControlPanel.CoreSettings.Current;
            var saved = (settings.MasterVolume, settings.VideoVolume);
            (settings.MasterVolume, settings.VideoVolume) = (80, 100);
            var reveal = new FloatingVideoReveal(window, hold =>
            {
                if (!hold) Assert.True(window.IsVisible);
                overlay.HoldRevealAudio(hold);
            });
            try
            {
                Assert.Equal(0, overlay.EffectivePlayerVolume);
                settings.MasterVolume = 35;   // a settings change while the first frame is pending
                Assert.Equal(0, overlay.EffectivePlayerVolume);
                if (stopBeforeFrame) reveal.Close();
                reveal.Present();
                Assert.Equal(stopBeforeFrame ? 0 : 35, overlay.EffectivePlayerVolume);
                reveal.Close();
                reveal.Present();
                Assert.Equal(0, overlay.EffectivePlayerVolume);
            }
            finally
            {
                reveal.Close();
                (settings.MasterVolume, settings.VideoVolume) = saved;
            }
            return Task.CompletedTask;
        });
    }

    private static (MandatoryVideoOverlay Overlay, Window Window, FloatingVideoReveal Reveal) FloatingSurface()
    {
        EnsureApp();
        var overlay = (MandatoryVideoOverlay)System.Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(MandatoryVideoOverlay).GetField("<Floating>k__BackingField", flags)!.SetValue(overlay, true);
        typeof(ConditioningControlPanel.Services.MandatoryVideoScheduler).GetField("_playing", flags)!.SetValue(overlay.Scheduler, true);
        ConditioningControlPanel.CoreSettings.Current.MasterVolume = 80;
        ConditioningControlPanel.CoreSettings.Current.VideoVolume = 100;
        var window = new Window { ShowActivated = false };
        var reveal = new FloatingVideoReveal(window, overlay.HoldRevealAudio);
        var surfaces = (System.Collections.Generic.List<MandatoryVideoOverlay.Surface>)typeof(MandatoryVideoOverlay)
            .GetField("_surfaces", flags)!.GetValue(overlay)!;
        surfaces.Add(new(window, new Image(), new Border(), new Image(), new Canvas(), new Border(), new TextBlock(),
            new MandatoryVideoOverlay.VideoButtons(() => { }, () => { }), new Panel(), reveal));
        return (overlay, window, reveal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hidden_clips_never_capture_escape_or_grace_and_both_reveal_paths_enable_them(bool fallback)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (overlay, window, reveal) = FloatingSurface();
            try
            {
                Assert.False(overlay.OnScreen);
                Assert.False(overlay.WantsGlobalEscape);
                Assert.False(overlay.TryEscapeFromGlobalKey());
                Assert.False(overlay.TryGracePause(fromPanicKey: true));
                Assert.False(overlay.GracePaused);
                Assert.Equal(ConditioningControlPanel.Services.GraceDecision.FallThrough, overlay.EvaluateGraceRequest(true, double.MaxValue));
                if (fallback)
                    Assert.Equal(ConditioningControlPanel.Services.Video.MandatoryVideoPresentation.FirstFrameVerdict.Wait, overlay.UpdateFloatingReveal(3.999, true));
                else
                    typeof(MandatoryVideoOverlay).GetField("<FirstFrameMs>k__BackingField",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(overlay, 0L);
                Assert.Equal(ConditioningControlPanel.Services.Video.MandatoryVideoPresentation.FirstFrameVerdict.Reveal,
                    overlay.UpdateFloatingReveal(fallback ? 4 : 0, true));
                Assert.True(window.IsVisible);
                Assert.True(overlay.Scheduler.IsPlaying);
                Assert.True(overlay.OnScreen);
                // WPF main 642a040e9e (#1594 #1595): a floating clip hears Escape only when Escape is the panic key.
                var s = ConditioningControlPanel.CoreSettings.Current;
                var panic = (s.PanicKeyEnabled, s.PanicKey);
                (s.PanicKeyEnabled, s.PanicKey) = (true, "F12");
                Assert.False(overlay.WantsGlobalEscape);
                Assert.False(overlay.TryEscapeFromGlobalKey());
                (s.PanicKeyEnabled, s.PanicKey) = (true, "Escape");
                Assert.True(overlay.WantsGlobalEscape);
                (s.PanicKeyEnabled, s.PanicKey) = panic;
                Assert.Equal(ConditioningControlPanel.Services.GraceDecision.Pause, overlay.EvaluateGraceRequest(true, double.MaxValue));
                Assert.True(overlay.Surfaces[0].Buttons.Row.IsVisible);
                Assert.True(overlay.EffectivePlayerVolume > 0);
                overlay.CloseAll();
                Assert.False(overlay.OnScreen);
                Assert.False(overlay.WantsGlobalEscape);
            }
            finally { reveal.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Playback_clocks_freeze_while_a_visible_clip_is_paused()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (overlay, _, reveal) = FloatingSurface();
            var ready = (System.Diagnostics.Stopwatch)typeof(MandatoryVideoOverlay)
                .GetField("_sinceReady", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
            try
            {
                overlay.UpdateFloatingReveal(4, true);
                Assert.True(overlay.OnScreen);
                overlay.ResumePlaybackClocks();
                Assert.True(ready.IsRunning);
                await Task.Delay(25);
                overlay.PausePlaybackClocks();
                Assert.False(ready.IsRunning);
                var budget = ready.Elapsed;
                await Task.Delay(80);
                Assert.Equal(budget, ready.Elapsed);
                overlay.ResumePlaybackClocks();
                Assert.True(ready.IsRunning);
            }
            finally { overlay.PausePlaybackClocks(); reveal.Close(); }
        });
    }

    // WPF VideoService.FloatingInstances.cs:882-896: a single LibVLC window makes room on Test.
    [Fact]
    public async Task Test_trigger_during_grace_replaces_the_single_floating_clip()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var settings = ConditioningControlPanel.CoreSettings.Current;
            var saved = (settings.MasterVolume, settings.VideoVolume, settings.StrictLockEnabled);
            var (overlay, window, reveal) = FloatingSurface();
            try
            {
                settings.StrictLockEnabled = false;
                overlay.Scheduler = new ConditioningControlPanel.Services.MandatoryVideoScheduler(overlay,
                    library: () => new[] { "/test/video.mp4" });
                typeof(ConditioningControlPanel.Services.MandatoryVideoScheduler).GetField("_playing",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(overlay.Scheduler, true);
                reveal.Present();
                var surface = overlay.Surfaces[0];
                MandatoryVideoOverlay.ShowGraceHost(surface);
                var card = surface.GraceHost!;
                typeof(MandatoryVideoOverlay).GetField("_gracePaused",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(overlay, true);

                Assert.True(overlay.TryFloatingTrigger(userInitiated: true));
                Assert.True(overlay.Scheduler.IsPlaying); // replacement was accepted
                Assert.False(overlay.GracePaused);
                Assert.False(window.IsVisible);
                Assert.False(card.IsVisible);
                Assert.Empty(overlay.Surfaces);
            }
            finally
            {
                overlay.Scheduler.ForceCleanup();
                reveal.Close();
                (settings.MasterVolume, settings.VideoVolume, settings.StrictLockEnabled) = saved;
            }
            return Task.CompletedTask;
        });
    }

    // WPF GracePauseOverlayWindow: a floating clip's card is its own 360x200 window, so a small clip never clips Resume.
    [Fact]
    public async Task A_floating_grace_card_opens_in_its_own_window_and_closes_with_the_clip()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (overlay, window, reveal) = FloatingSurface();
            try
            {
                var x = overlay.Surfaces[0];
                MandatoryVideoOverlay.ShowGraceHost(x);
                var card = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(x.Grace));
                Assert.NotSame(window, card);
                Assert.Same(card, x.GraceHost);
                Assert.True(card.IsVisible && card.Topmost && !card.ShowActivated);
                Assert.Equal(new Size(360, 200), card.ClientSize);
                MandatoryVideoOverlay.ShowGraceHost(x);   // a second press never stacks a card
                Assert.Same(card, x.GraceHost);
                MandatoryVideoOverlay.CloseGraceHost(x);
                Assert.Null(x.GraceHost);
                Assert.False(card.IsVisible);
                Assert.Null(x.Grace.Parent);
                // The clip's close takes a pending card with it (panic reaches the card through CloseAll).
                MandatoryVideoOverlay.ShowGraceHost(x);
                card = x.GraceHost!;
                overlay.CloseAll();
                Assert.False(card.IsVisible);
            }
            finally { reveal.Close(); }
            return Task.CompletedTask;
        });
    }
}
