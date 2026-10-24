using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashCeilingTests
{
    [Theory]
    [InlineData(1, false, false, false, 1)]
    [InlineData(7, true, true, false, 7)]
    [InlineData(7, true, false, true, 7)]
    [InlineData(1, true, true, true, 40)]
    [InlineData(20, true, true, true, 40)]
    [InlineData(99, false, false, false, 20)]
    [InlineData(0, false, false, false, 1)]
    public void Every_spawn_uses_the_slider_ceiling_except_ambient_staying_flashes(
        int slider, bool clickable, bool stay, bool ambient, int expected)
    {
        var s = new AppSettings { HydraLimit = slider, FlashClickable = clickable, FlashStayUntilPopped = stay };
        Assert.Equal(expected, FlashOverlay.Cap(s, ambient));
    }

    [Fact]
    public Task Direct_and_hydra_spawns_recheck_the_live_ceiling_before_presenting_or_paying() =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.HydraLimit, s.FlashClickable, s.FlashStayUntilPopped);
            var present = FlashOverlay.Present;
            var pay = CoreProgression.AddXPProvider;
            var count = 0;
            var payments = 0;
            var host = new Window();
            var screen = host.Screens.All[0];
            Bitmap Picture() => new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            bool Spawn(int generation = 0, bool ambient = false) => FlashOverlay.Show(Picture(),
                new PixelRect(100, 100, 2, 2), "ceiling.png", screen, 1, TimeSpan.Zero,
                TimeSpan.FromMinutes(10), clickable: false, ambient, generation);
            try
            {
                FlashOverlay.Present = (_, _, _) => { count++; return true; };
                CoreProgression.AddXPProvider = (_, _) => payments++;
                (s.HydraLimit, s.FlashClickable, s.FlashStayUntilPopped) = (1, false, false);
                Assert.True(Spawn());
                Assert.False(Spawn());
                Assert.False(Spawn(generation: 1));
                Assert.Equal(1, count);
                Assert.Equal(1, payments);
                Assert.Single(FlashOverlay.Active);

                // Changing the slider applies to an already queued burst's next spawn.
                s.HydraLimit = 2;
                Assert.True(Spawn(generation: 1));
                Assert.False(Spawn());
                s.HydraLimit = 1;
                Assert.False(Spawn());
                Assert.Equal(2, count);
                Assert.Equal(2, payments);

                // The same shared gate honours the stay-until-popped exception.
                (s.FlashClickable, s.FlashStayUntilPopped) = (true, true);
                Assert.True(Spawn(ambient: true));
                Assert.False(Spawn(ambient: false));
            }
            finally
            {
                host.Close();
                FlashOverlay.CloseAll(final: false);
                FlashOverlay.Present = present;
                CoreProgression.AddXPProvider = pay;
                (s.HydraLimit, s.FlashClickable, s.FlashStayUntilPopped) = saved;
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task A_full_screen_skips_one_burst_picture_and_a_later_one_still_spawns() =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.HydraLimit, s.FlashClickable, s.FlashStayUntilPopped);
            var present = FlashOverlay.Present;
            var runLater = FlashOverlay.RunLater;
            var pay = CoreProgression.AddXPProvider;
            var count = 0;
            var done = false;
            var ticks = new System.Collections.Generic.List<Action>();
            var host = new Window();
            try
            {
                FlashOverlay.Present = (_, _, _) => { count++; return true; };
                FlashOverlay.RunLater = (a, _) => ticks.Add(a);
                CoreProgression.AddXPProvider = (_, _) => { };
                (s.HydraLimit, s.FlashClickable, s.FlashStayUntilPopped) = (1, false, false);
                var screen = host.Screens.All[0];
                var flashes = new System.Collections.Generic.List<(Bitmap, PixelRect, string, Screen)>();
                for (var i = 0; i < 3; i++)
                    flashes.Add((new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888,
                        AlphaFormat.Premul), new PixelRect(100, 100, 2, 2), "burst.png", screen));
                FlashOverlay.Schedule(flashes, 0, 0, FlashOverlay.Generation, 1, TimeSpan.Zero, TimeSpan.FromMinutes(10),
                    clickable: false, ambient: false, hydraGeneration: 0, done: () => done = true);
                Assert.Equal(3, ticks.Count);

                ticks[0]();
                ticks[1](); // screen full: this picture is skipped, the burst is not stopped
                Assert.Equal(1, count);
                s.HydraLimit = 2; // room frees up before the third picture
                ticks[2]();
                Assert.Equal(2, count);
                Assert.True(done);
            }
            finally
            {
                host.Close();
                FlashOverlay.CloseAll(final: false);
                FlashOverlay.Present = present;
                FlashOverlay.RunLater = runLater;
                CoreProgression.AddXPProvider = pay;
                (s.HydraLimit, s.FlashClickable, s.FlashStayUntilPopped) = saved;
            }
            return Task.CompletedTask;
        });
}
