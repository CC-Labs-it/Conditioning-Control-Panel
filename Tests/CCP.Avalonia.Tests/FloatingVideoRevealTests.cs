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

    [Fact]
    public async Task Grace_pause_before_the_first_frame_freezes_the_ready_budget_until_resume()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var overlay = (MandatoryVideoOverlay)System.Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
            var ready = (System.Diagnostics.Stopwatch)typeof(MandatoryVideoOverlay)
                .GetField("_sinceReady", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
            try
            {
                Assert.Equal(-1, overlay.FirstFrameMs);
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
            finally { overlay.PausePlaybackClocks(); }
        });
    }

}
