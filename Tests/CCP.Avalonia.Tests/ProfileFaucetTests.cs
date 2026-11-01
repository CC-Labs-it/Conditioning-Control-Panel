using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileFaucetTests
{
    private static MainShellWindow Create()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.MasterVolume = 0;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        return new MainShellWindow();
    }

    // A fake server read folded into the hold, then arm (the real fold, ApplyDescentToVat, is ProfileVatPollTests).
    private static void Seed(MainShellWindow shell)
    {
        var step = shell.FaucetHold.Fold(new VatRead { Kind = VatReadKind.Seed, Fill = .4, TodayXp = 400, Cap = 1000, Lip = 1.2 }, pouring: false);
        Assert.Equal(0, step.Fill); // The entire unpoured server total is held, never auto-poured.
        shell.ArmVat(shell.ProfilePage!.FindControl<VatGlassCanvas>("ProfileVatGlass")!, 1000);
    }

    // WPF MainWindow.ProfileFaucet.cs: rock -2.2..2.2 over .55 s, chip 1..1.05 over 2.8 s, droplet keys over
    // 1.8 s, tick every 3.8 s, hover 1.16, ring fade 120 ms, shiver keys over 420 ms, thud 2.1 -> 1, tilt 14.
    [Fact]
    public Task FaucetMotionRidesTheSharedBeatAndHonoursReducedMotion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previous = CoreSettings.ServiceProvider;
        var previousAudio = CoreAudio.PlayOneShotProvider;
        var previousTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var shell = Create();
        try
        {
            shell.Show();
            shell.ShowTab("discord");
            shell.Activate();
            var page = shell.ProfilePage!;
            page.FindControl<Grid>("ProfileCardWrapper")!.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            var cues = new List<string>();
            CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileName(path));
            CoreSettings.Current.MasterVolume = 50;
            CoreSettings.Current.PerformanceMode = false;
            var tap = page.FindControl<Grid>("ProfileVatFaucet")!;
            var ring = page.FindControl<Grid>("ProfileVatChargeRing")!;
            var drop = page.FindControl<Ellipse>("ProfileVatFaucetDrop")!;
            var sparks = page.FindControl<Canvas>("ProfileVatSparks")!;
            void Step(double ms) { time.Advance(TimeSpan.FromMilliseconds(ms)); BeatLoop.StepAll(); }
            void Press(global::Avalonia.Interactivity.RoutedEvent e, Key key) => tap.RaiseEvent(new KeyEventArgs { RoutedEvent = e, Key = key });

            CoreSettings.Current.MotionLevel = MotionLevel.Reduced; // ambient loops off: the still badge says "XP waiting"
            Seed(shell);
            Assert.False(shell.FaucetWobbling);
            Assert.True(page.FindControl<Ellipse>("ProfileVatFaucetBadge")!.IsVisible);
            Press(InputElement.KeyDownEvent, Key.Space); Press(InputElement.KeyUpEvent, Key.Space); Step(104);
            Assert.True(drop.Opacity > 0); Assert.True(shell.FaucetDropFall.Y > 0); Assert.NotEqual(0, shell.FaucetShake.X);
            Step(520); Assert.Equal(0, drop.Opacity); Assert.Equal(0, shell.FaucetDropFall.Y);
            shell.DisarmVat();

            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            Seed(shell);
            Assert.True(shell.FaucetWobbling);
            Assert.Equal(-2.2, shell.FaucetTilt.Angle, 3);
            Step(450);
            Assert.Equal(.85, drop.Opacity, 3);
            Step(100);
            Assert.Equal(2.2, shell.FaucetTilt.Angle, 3);
            Step(2250);
            Assert.Equal(1.05, shell.FaucetChipScale.ScaleX, 3);
            Assert.DoesNotContain("faucet_tick.wav", cues);
            Step(1000);
            Assert.Single(cues, c => c == "faucet_tick.wav");
            CoreSettings.Current.MotionLevel = MotionLevel.Off; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(shell.FaucetWobbling); Assert.Equal(0, drop.Opacity);
            CoreSettings.Current.MotionLevel = MotionLevel.Full; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(shell.FaucetWobbling);
            shell.WindowState = WindowState.Minimized; Assert.False(shell.FaucetWobbling); Assert.Equal(0, drop.Opacity);
            shell.WindowState = WindowState.Normal; shell.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.True(shell.FaucetWobbling);

            tap.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, tap, new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true), shell, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Step(150);
            Assert.Equal(1.16, shell.FaucetScale.ScaleX, 3);

            Press(InputElement.KeyDownEvent, Key.Space);
            Assert.False(shell.FaucetWobbling);
            Assert.Equal(0, ring.Opacity);
            Step(60);
            Assert.Equal(.5, ring.Opacity, 3);
            Step(60);
            Assert.Equal(1, ring.Opacity, 3);
            Assert.Equal(.94, shell.FaucetScale.ScaleX, 3);
            Press(InputElement.KeyUpEvent, Key.Space);
            Step(60);
            Assert.Equal(-2.6, shell.FaucetShake.X, 3);
            Step(360);
            Assert.Equal(0, shell.FaucetShake.X);
            Assert.True(shell.FaucetWobbling); // the hold is kept, so the wobble comes back

            Press(InputElement.KeyDownEvent, Key.Enter);
            shell.AdvanceFaucetCharge(1.0);
            Step(0);
            Assert.Equal(2.1, shell.FaucetHandleScale.ScaleX, 3);
            Assert.InRange(sparks.Children.Count, 5, 9);
            Step(240);
            Assert.Equal(14, shell.FaucetTilt.Angle, 3);
            Step(400);
            Assert.Equal(1, shell.FaucetHandleScale.ScaleX, 3);
            Assert.Empty(sparks.Children);
            shell.DisarmVat();
            Assert.Equal(0, shell.FaucetTilt.Angle);
        }
        finally { shell.Close(); BeatLoop.Time = previousTime; CoreSettings.ServiceProvider = previous; CoreAudio.PlayOneShotProvider = previousAudio; }
        return Task.CompletedTask;
    });
}
