using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;

namespace CCP.Avalonia.Tests;

public sealed partial class FirstShowStageTests
{
    [Fact]
    public Task GuideFollowsShellBetweenMonitors() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.Current.Welcomed = true;
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            CoreSettings.Current.MasterVolume = 0;
            FirstShowService.RequireClickThrough = false;
            shell = new MainShellWindow { OpenWelcomeShow = _ => false, Position = new PixelPoint(100, 100) };
            shell.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(FirstShowService.Open(shell));
            var stage = FirstShowService.Window!;
            var template = stage.Screens.All.First();
            Screen Display(PixelRect bounds, double scale)
            {
                var screen = (Screen)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(template, null)!;
                typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
                typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
                typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, scale);
                return screen;
            }
            var first = Display(new PixelRect(0, 0, 1920, 1080), 1);
            var second = Display(new PixelRect(-1920, -200, 1920, 1200), 1.5);
            stage.GuideScreenOverride = () => shell.Position.X < 0 ? second : first;
            stage.GuideScreenFromPointOverride = _ => shell.Position.X < 0 ? second : first;
            double t = S.GreetAfter + .1; stage.Clock = () => t; stage.Frame();
            async Task WaitForSpeech()
            {
                // Showing the card can itself change layout. Require four settled frames.
                var deadline = DateTime.UtcNow.AddSeconds(10);
                int visible = 0;
                while (visible < 4 && DateTime.UtcNow < deadline)
                {
                    t += .05; stage.Frame(); Dispatcher.UIThread.RunJobs();
                    visible = stage.GuideSpeechWindow.IsVisible ? visible + 1 : 0;
                    await Task.Yield();
                }
                Assert.Equal(4, visible);
            }
            stage.Start(); t += S.Length + .1; stage.Frame();
            Click(stage, 0); Click(stage, 0);
            await Until(() => { t += .05; stage.Frame(); return stage.HighlightTarget != null; });
            Assert.Equal(first.Bounds.Position, stage.Position);
            // The shell's chosen screen is to the right, but its help target is across the boundary.
            var target = stage.GuideTarget!;
            var targetCenter = target.PointToScreen(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2));
            int boundary = targetCenter.X + 40;
            var shellScreen = Display(new PixelRect(boundary, -200, 2500, 1400), 1);
            var targetScreen = Display(new PixelRect(-1920, -200, boundary + 1920, 1400), 1);
            Assert.True(shell.Position.X + shell.Bounds.Width / 2 > boundary);
            stage.GuideScreenOverride = () => shellScreen;
            stage.GuideScreenFromPointOverride = point => point.X < boundary ? targetScreen : shellScreen;
            t += .05; stage.Frame(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(targetScreen.Bounds.Position, stage.Position);
            Assert.NotNull(stage.HighlightTarget);
            Assert.InRange(stage.GuideBodyCenter.X, targetScreen.Bounds.X, targetScreen.Bounds.Right);
            // A target can finish layout after the screen changes. Simulate that late movement.
            stage.GuidePointToScreenOverride = (control, point) =>
            {
                var pixel = control.PointToScreen(point);
                return new PixelPoint(pixel.X, pixel.Y + 100);
            };
            // Each frame can discover movement; allow the speech card's settle interval to elapse.
            await WaitForSpeech();
            Assert.True(stage.GuideSpeechWindow.IsVisible);
            Assert.InRange(stage.GuideSpeechWindow.Position.X, targetScreen.Bounds.X, targetScreen.Bounds.Right);
            stage.GuideScreenFromPointOverride = _ => shell.Position.X < 0 ? second : first;
            stage.GuideScreenOverride = () => shell.Position.X < 0 ? second : first;
            // Map through the shell's own Position: headless backends differ on whether PointToScreen
            // follows a window move (Avalonia 12 does), so the test assumes neither.
            stage.GuidePointToScreenOverride = (control, point) =>
            {
                var local = control.TranslatePoint(point, shell) ?? point;
                return new PixelPoint((int)Math.Round(shell.Position.X + local.X), (int)Math.Round(shell.Position.Y + local.Y));
            };
            shell.Position = new PixelPoint(-1800, -100);
            Dispatcher.UIThread.RunJobs();
            t += .05; stage.Frame(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(second.Bounds.Position, stage.Position);
            Assert.Equal(1280, stage.Width);
            Assert.Equal(800, stage.Height);
            Assert.NotNull(stage.HighlightTarget);
            Assert.InRange(stage.GuideBodyCenter.X, -1920, 0);
            Assert.InRange(stage.GuideBodyCenter.Y, -200, 1000);
            await WaitForSpeech();
            Assert.True(stage.GuideSpeechWindow.IsVisible);
            Assert.InRange(stage.GuideSpeechWindow.Position.X, -1920, 0);
            Assert.InRange(stage.GuideSpeechWindow.Position.Y, -200, 1000);
            Click(stage, stage.ChoiceButtons.Count - 1);
            Assert.Equal(3, stage.ChoiceButtons.Count);
            stage.GuidePointToScreenOverride = null;
            shell.Position = new PixelPoint(100, 100);
            t += .05; stage.Frame();
            Assert.Equal(first.Bounds.Position, stage.Position);
            Assert.Equal(1920, stage.Width);
        }
        finally
        {
            FirstShowService.Stop(); FirstShowService.RequireClickThrough = true;
            EmiDeskService.Instance.Window?.ShutDown();
            foreach (var o in shell?.OwnedWindows.ToList() ?? new()) o.Close();
            shell?.RequestExit(); Dispatcher.UIThread.RunJobs();
            service.SaveImmediate(); CoreSettings.ServiceProvider = old;
        }
    });
}
