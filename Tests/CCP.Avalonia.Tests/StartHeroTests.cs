using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StartHeroTests
{
    [Fact]
    public void StartPressIgnitesThenHeartbeatsAndStopReturnsToChargedIdle() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldTime = BeatLoop.Time;
        var oldStop = CoreEngine.StoppedHook;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.FlashEnabled = s.SubliminalEnabled = s.BouncingTextEnabled = s.BubblesEnabled = false;
        s.LockCardEnabled = s.BubbleCountEnabled = s.MandatoryVideosEnabled = s.PopQuizEnabled = s.MindWipeEnabled = false;
        s.MotionLevel = MotionLevel.Full;
        var time = new Clock();
        BeatLoop.Time = time;
        var shell = new MainShellWindow();
        var button = shell.Named<Button>("BtnStart")!;
        Border Ring(string name) => shell.Named<Border>(name)!;
        ScaleTransform Scale(Control c) => Assert.IsType<ScaleTransform>(c.RenderTransform);
        void Advance(double seconds) { time.Advance(seconds); BeatLoop.StepAll(); }
        try
        {
            shell.Show(); shell.Activate(); Dispatcher.UIThread.RunJobs();
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            var charge = Assert.IsType<LinearGradientBrush>(button.Background);
            Advance(.4725);
            Assert.Equal(.7, Ring("StartRingExhale").Opacity, 5);
            Assert.True(Scale(Ring("StartRingExhale")).ScaleY > 1);
            Assert.NotEqual(.5, charge.GradientStops[1].Offset);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(CoreEngine.IsRunning);
            Assert.Equal(.55, Ring("StartIgnitionFlash").Opacity, 5);
            Advance(.04);
            Assert.Equal(.97, Scale(button).ScaleX, 5);
            Assert.True(Ring("StartRingBurstA").Opacity > 0);
            Assert.Equal(0, Ring("StartRingBurstB").Opacity);
            Advance(.06);
            Assert.True(Ring("StartRingBurstB").Opacity > 0);
            Advance(.74);
            Assert.Equal(0, Ring("StartIgnitionFlash").Opacity);
            Assert.Equal(0, Ring("StartRingBurstA").Opacity);
            Assert.Equal(0, Ring("StartRingBurstB").Opacity);
            Advance(1.66); // 2.4s after dip completion: heartbeat peak
            Assert.Equal(1.012, Scale(button).ScaleY, 5);
            Assert.Equal(Color.FromRgb(255, 107, 107), Assert.IsType<SolidColorBrush>(button.Background).Color);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(CoreEngine.IsRunning);
            Assert.Same(charge, button.Background);
            Advance(.84);

            s.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Equal(0, Ring("StartRingExhale").Opacity);
            Assert.Equal(1, Scale(button).ScaleX);
            shell.FlashStartIgnition();
            Assert.Equal(.55, Ring("StartIgnitionFlash").Opacity, 5); // reduced keeps interaction FX
            s.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            shell.FlashStartIgnition();
            Assert.Equal(0, Ring("StartIgnitionFlash").Opacity);
            Assert.DoesNotContain(BeatLoop.Running, l => ReferenceEquals(l.Owner, button));

            s.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            shell.UpdateStartButtonForRemoteControl(true);
            Assert.Equal(Color.FromRgb(0, 255, 136), Assert.IsType<SolidColorBrush>(button.Background).Color);
            Assert.DoesNotContain(BeatLoop.Running, l => ReferenceEquals(l.Owner, button));
            shell.UpdateStartButtonForRemoteControl(false);
            shell.WindowState = WindowState.Minimized;
            Assert.Equal(0, Ring("StartRingExhale").Opacity);
            Assert.DoesNotContain(BeatLoop.Running, l => ReferenceEquals(l.Owner, button));
        }
        finally
        {
            CoreEngine.Stop(); CoreEngine.StoppedHook = oldStop;
            shell.Close(); BeatLoop.Time = oldTime; CoreSettings.ServiceProvider = oldSettings;
        }
    });

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(double seconds) => _ticks += (long)(seconds * TimeSpan.TicksPerSecond);
    }
}
