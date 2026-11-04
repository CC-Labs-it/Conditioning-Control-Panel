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
            double t = S.GreetAfter + .1; stage.Clock = () => t; stage.Frame();
            stage.Start(); t += S.Length + .1; stage.Frame();
            Click(stage, 0); Click(stage, 0);
            await Until(() => { t += .05; stage.Frame(); return stage.HighlightTarget != null; });
            Assert.Equal(first.Bounds.Position, stage.Position);
            shell.Position = new PixelPoint(-1800, -100);
            Dispatcher.UIThread.RunJobs();
            t += .05; stage.Frame(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(second.Bounds.Position, stage.Position);
            Assert.Equal(1280, stage.Width);
            Assert.Equal(800, stage.Height);
            Assert.NotNull(stage.HighlightTarget);
            Assert.InRange(stage.GuideBodyCenter.X, -1920, 0);
            Assert.InRange(stage.GuideBodyCenter.Y, -200, 1000);
            t += 1; stage.Frame(); Dispatcher.UIThread.RunJobs();
            Assert.True(stage.GuideSpeechWindow.IsVisible);
            Assert.InRange(stage.GuideSpeechWindow.Position.X, -1920, 0);
            Assert.InRange(stage.GuideSpeechWindow.Position.Y, -200, 1000);
            Click(stage, stage.ChoiceButtons.Count - 1);
            Assert.Equal(3, stage.ChoiceButtons.Count);
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
