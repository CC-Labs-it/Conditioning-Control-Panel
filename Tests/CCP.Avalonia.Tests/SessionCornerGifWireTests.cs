using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class SessionCornerGifWireTests
{
    [Fact]
    public Task RackOptionStartsAnimatedCornerLiveEditsReuseWindowAndPanicClosesIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AvAppBuilder().SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-session-corner-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String("R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7"));
        var shell = new MainShellWindow();
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        var s = CoreSettings.Current;
        var slots = s.CornerGifOverlays;
        s.CornerGifOverlays = new();
        s.SessionCornerGifAllowed = true;
        CornerGifOverlay.SkipPlatformChecksForTests = CornerGifOverlay.ManualFramesForTests = true;
        CornerGifOverlay.Seed(() => shell);
        shell.Show();
        try
        {
            var tab = shell.Named<PresetsTabView>("PresetsTab")!;
            var row = tab.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>()
                .First(r => r.Tag is Session { Id: "gamer_girl" });
            row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.True(tab.FindControl<Border>("CornerGifOptionPanel")!.IsVisible);
            tab.FindControl<CheckBox>("ChkCornerGifEnabled")!.IsChecked = true;
            Assert.True(tab.FindControl<StackPanel>("CornerGifSettings")!.IsVisible);
            tab.SelectCornerGifPath(gif); // the picker's accepted local file
            tab.FindControl<Slider>("SliderCornerGifOpacity")!.Value = 35;
            tab.FindControl<RadioButton>("RbCornerTR")!.IsChecked = true;
            shell.StartSession((Session)row.Tag!);
            await Settle();
            Assert.True(runner.IsCornerGifShown);
            var window = CornerGifOverlay.WindowFor(CornerGifOverlay.SessionSlot);
            Assert.NotNull(window);
            Assert.Equal(.35, window!.Spiral.Opacity, 3);
            var frame = window.Spiral.Source;
            CornerGifOverlay.Advance(CornerGifOverlay.SessionSlot);
            Assert.NotSame(frame, window.Spiral.Source);
            tab.FindControl<Slider>("SliderCornerGifOpacity")!.Value = 60;
            tab.FindControl<RadioButton>("RbCornerBL")!.IsChecked = true;
            await Settle();
            Assert.Same(window, CornerGifOverlay.WindowFor(CornerGifOverlay.SessionSlot));
            Assert.Equal(.60, window.Spiral.Opacity, 3);
            Assert.Equal(60, runner.CurrentSession!.Settings.CornerGifOpacity);
            var size = tab.FindControl<Slider>("SliderCornerGifSize")!;
            size.Value = 180;
            size.Value = 220;
            Assert.Same(window, CornerGifOverlay.WindowFor(CornerGifOverlay.SessionSlot));
            await Task.Delay(500); // WPF's 400 ms resting-slider debounce
            await Settle();
            var resized = CornerGifOverlay.WindowFor(CornerGifOverlay.SessionSlot);
            Assert.NotNull(resized);
            Assert.NotSame(window, resized);
            Assert.Equal(220, resized!.Width, 0);
            Assert.Equal(220, runner.CurrentSession.Settings.CornerGifSize);
            Assert.False(File.Exists(CornerGifOverlay.SentinelPath)); // session art cannot disable user slots next launch
            PanicSurfaces.StopAll("session-corner-test", shell);
            await Settle();
            Assert.False(CornerGifOverlay.HasActiveOverlays || CoreCornerGif.SessionActive);
        }
        finally
        {
            runner.Stop();
            await Settle();
            CornerGifOverlay.StopAll();
            CornerGifOverlay.SkipPlatformChecksForTests = CornerGifOverlay.ManualFramesForTests = false;
            CoreCornerGif.RefreshHandler = null;
            CoreCornerGif.ShowSessionHandler = null;
            CoreCornerGif.HideSessionHandler = null;
            CoreCornerGif.StandaloneActiveProvider = CoreCornerGif.SpiralVisibleProvider = null;
            CoreSession.IsSessionRunningProvider = null;
            AvApp.Sessions = null;
            s.CornerGifOverlays = slots;
            shell.Close();
            Directory.Delete(dir, true);
        }
    });

    private static AppBuilder AvAppBuilder() => AppBuilder.Configure<AvApp>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    private static async Task Settle()
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            CornerGifOverlay.Pump();
            if (CornerGifOverlay.LastDecode is { } decode) await decode;
        }
    }
}
