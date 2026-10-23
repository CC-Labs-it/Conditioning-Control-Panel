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
        var sessions = AvApp.Sessions;
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        var s = CoreSettings.Current;
        var slots = s.CornerGifOverlays;
        var (allowed, spiralOn) = (s.SessionCornerGifAllowed, s.SpiralEnabled);
        var (refresh, showSession, hideSession) = (CoreCornerGif.RefreshHandler, CoreCornerGif.ShowSessionHandler, CoreCornerGif.HideSessionHandler);
        var (standalone, spiralVisible, sessionRunning) = (CoreCornerGif.StandaloneActiveProvider, CoreCornerGif.SpiralVisibleProvider, CoreSession.IsSessionRunningProvider);
        s.CornerGifOverlays = new();
        s.SessionCornerGifAllowed = s.SpiralEnabled = true;
        CornerGifOverlay.SkipPlatformChecksForTests = CornerGifOverlay.ManualFramesForTests = true;
        var clock = CornerGifOverlay.Clock;
        CornerGifOverlay.Clock = Clock; // P08: Settle steps the realization stagger
        CornerGifOverlay.Seed(() => shell);
        var spiralUp = true; // the fullscreen spiral holds the corner at start
        CoreCornerGif.SpiralVisibleProvider = () => spiralUp;
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
            tab.FindControl<Slider>("SliderCornerGifOpacity")!.Value = 35;
            tab.FindControl<RadioButton>("RbCornerTR")!.IsChecked = true;
            shell.StartSession((Session)row.Tag!);
            await Settle();
            Assert.False(runner.IsCornerGifShown);
            spiralUp = false; // no art picked yet = the spiral art, which yields to the fullscreen spiral
            SpiralOverlay.CloseAll(); // WPF OverlayService.cs:2068: the minute-0 corner GIF comes back
            await Settle();
            Assert.True(runner.IsCornerGifShown);
            tab.SelectCornerGifPath(gif); // the picker's accepted local file, live
            await Settle();
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
            Assert.True(tab.CornerGifSizeDebounce.IsEnabled); // WPF's 400 ms resting-slider debounce
            Assert.Equal(400, tab.CornerGifSizeDebounce.Interval.TotalMilliseconds);
            tab.FlushCornerGifSize();
            await Settle();
            var resized = CornerGifOverlay.WindowFor(CornerGifOverlay.SessionSlot);
            Assert.NotNull(resized);
            Assert.NotSame(window, resized);
            Assert.Equal(220, resized!.Width, 0);
            Assert.Equal(220, runner.CurrentSession.Settings.CornerGifSize);
            Assert.False(File.Exists(CornerGifOverlay.SentinelPath)); // session art cannot disable user slots next launch
            s.CornerGifOverlays = new() { new CornerGifOverlaySetting { Enabled = true, GifPath = gif } };
            CornerGifOverlay.Refresh(shell); // the user turns a slot on mid-session: it yields
            await Settle();
            Assert.Equal(new[] { CornerGifOverlay.SessionSlot }, CornerGifOverlay.ShownSlots);
            // A program session ends before the panic's corner-gif surface; its posted handback must not undo the panic.
            runner.Stop();
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
            CornerGifOverlay.Clock = clock;
            (CoreCornerGif.RefreshHandler, CoreCornerGif.ShowSessionHandler, CoreCornerGif.HideSessionHandler) = (refresh, showSession, hideSession);
            (CoreCornerGif.StandaloneActiveProvider, CoreCornerGif.SpiralVisibleProvider, CoreSession.IsSessionRunningProvider) = (standalone, spiralVisible, sessionRunning);
            AvApp.Sessions = sessions;
            s.CornerGifOverlays = slots;
            (s.SessionCornerGifAllowed, s.SpiralEnabled) = (allowed, spiralOn);
            shell.Close();
            Directory.Delete(dir, true);
        }
    });

    private static AppBuilder AvAppBuilder() => AppBuilder.Configure<AvApp>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    private static readonly SteppedClock Clock = new();

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static async Task Settle()
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Clock.Now += TimeSpan.TicksPerSecond; // past CornerGifPlanner.StaggerMs
            CornerGifOverlay.Pump();
            if (CornerGifOverlay.LastDecode is { } decode) await decode;
        }
    }
}
