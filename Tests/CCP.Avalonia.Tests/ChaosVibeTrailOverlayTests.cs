using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The VibePopping trail window on a stepped clock and a played pointer: glow follows,
/// dots drop and fade like WPF, nothing ticks once hidden, panic closes it.</summary>
public sealed class ChaosVibeTrailOverlayTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task Glow_follows_the_pointer_dots_fade_and_hidden_stops_the_tick()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var w = new ChaosVibeTrailOverlay();
            try
            {
                long now = 5000;
                var at = new PixelPoint(200, 150);
                w.NowMs = () => now;
                w.ReadPointer = () => at;
                w.Position = new PixelPoint(0, 0);
                w.Show();
                w.BeginFollow();
                Assert.True(w.IsFollowing);

                var shapes = ((Canvas)w.Content!).Children.OfType<Ellipse>().ToList();
                var glow = shapes[^1];
                var dots = shapes.Take(ChaosVibeTrail.Dots).ToList();
                double s = w.RenderScaling;
                Assert.Equal(1, glow.Opacity);
                Assert.Equal(200 / s - 29, Canvas.GetLeft(glow), 3);
                Assert.Single(dots, d => d.Opacity > 0);   // the render sample was cleared; one dot at the snap

                now += 170; at = new PixelPoint(260, 150);
                w.Tick();
                Assert.Equal(260 / s - 29, Canvas.GetLeft(glow), 3);
                var lit = dots.Select(d => d.Opacity).Where(o => o > 0).OrderBy(o => o).ToList();
                Assert.Equal(2, lit.Count);
                Assert.Equal(0.25, lit[0], 3);             // the snap dot, half its 340 ms life
                Assert.Equal(0.5, lit[1], 3);              // the new one, 60 DIPs on

                now += 1000;
                w.Tick();
                Assert.All(dots, d => Assert.Equal(0, d.Opacity));

                w.EndFollow();
                Assert.False(w.IsVisible);
                Assert.False(w.IsFollowing);
                Assert.Equal(0, glow.Opacity);
                at = new PixelPoint(900, 900);
                w.Tick();                                  // hidden: no work (P01)
                Assert.Equal(260 / s - 29, Canvas.GetLeft(glow), 3);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Panic_closes_the_trail_window()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                ChaosVibeTrailOverlay.EnsureCreated();
                Dispatcher.UIThread.RunJobs();
                var w = ChaosVibeTrailOverlay.Active;
                Assert.NotNull(w);
                w!.Show();
                PanicSurfaces.All.Single(x => x.Id == "chaos-vibe-trail").Stop(null);
                Assert.Null(ChaosVibeTrailOverlay.Active);
                Assert.False(w.IsVisible);
            }
            finally { ChaosVibeTrailOverlay.CloseActive(); }
            return Task.CompletedTask;
        });
    }
}
