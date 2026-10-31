using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BillboardCtaPressTests
{
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public Task Press_and_release_follow_WPF_depth_timing_and_static_off(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.MotionLevel = level;
        service.Current.PerformanceMode = false;
        var deck = new BillboardDeck(() => new IBillboardProvider[] { new HouseProvider() },
            () => new BillboardContext(BillboardTier.Free, System.DateTime.UtcNow, System.DateTime.Now),
            new Dictionary<string, System.DateTime>(), () => { });
        var host = new BillboardCardHost(deck) { Width = 640, Height = 300 };
        var window = new Window { Content = host, Width = 640, Height = 300 };
        var oldTime = BeatLoop.Time;
        var clock = new SteppedTime();
        BeatLoop.Time = clock;
        try
        {
            host.Begin();
            // Press travel is independent of the Windows font families on the card.
            foreach (var text in host.GetLogicalDescendants().OfType<TextBlock>()) text.FontFamily = new FontFamily("DejaVu Sans");
            window.Show(); Dispatcher.UIThread.RunJobs();
            var face = (Border)host.Cta.Content!;
            var point = host.Cta.TranslatePoint(new Point(20, 10), window)!.Value;
            double Y() => Assert.IsType<TranslateTransform>(face.RenderTransform).Y;
            void Step(double ms) { clock.Advance(System.TimeSpan.FromMilliseconds(ms)); BeatLoop.StepAll(); }
            bool moving = level != MotionLevel.Off;
            int press = DepthLaw.Ms(DepthLaw.PressMs, level), release = DepthLaw.Ms(DepthLaw.ReleaseMs, level);
            window.MouseDown(point, MouseButton.Left);
            Assert.True(host.Cta.IsPressed);
            Assert.Equal(moving ? 0 : DepthLaw.PressTravelPx, Y(), 5);
            Step(press * 0.5);
            Assert.Equal(moving ? DepthLaw.PressTravelPx / 2 : DepthLaw.PressTravelPx, Y(), 5);
            Step(press * 0.5);
            Assert.Equal(DepthLaw.PressTravelPx, Y(), 5);
            window.MouseUp(point, MouseButton.Left);
            Assert.False(host.Cta.IsPressed);
            Step(release * 0.6);
            Assert.Equal(moving ? -DepthLaw.ReleaseOvershootPx : 0, Y(), 5);
            Step(release * 0.2);
            Assert.Equal(moving ? -DepthLaw.ReleaseOvershootPx / 2 : 0, Y(), 5);
            Step(release * 0.2);
            Assert.Equal(0, Y(), 5);
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == face);
            service.Current.MotionLevel = MotionLevel.Full;
            window.MouseDown(point, MouseButton.Left);
            service.Current.MotionLevel = MotionLevel.Off; host.RefreshMotion();
            Assert.Equal(DepthLaw.PressTravelPx, Y(), 5);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(0, Y(), 5);
        }
        finally { window.Close(); host.ReleaseArt(); BeatLoop.Time = oldTime; }
        return Task.CompletedTask;
    });
}
