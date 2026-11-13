using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
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
}
