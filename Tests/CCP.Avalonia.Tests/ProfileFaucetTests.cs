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

    // Stands in for wire-shell-profile-vat-b's ApplyDescentToVat: fold a fake server read into the hold, then arm.
    private static void Seed(MainShellWindow shell)
    {
        var step = shell.FaucetHold.Fold(new VatRead { Kind = VatReadKind.Seed, Fill = .4, TodayXp = 400, Cap = 1000, Lip = 1.2 }, pouring: false);
        Assert.Equal(0, step.Fill); // The entire unpoured server total is held, never auto-poured.
        shell.ArmVat(shell.ProfilePage!.FindControl<VatGlassCanvas>("ProfileVatGlass")!, 1000);
    }

    [Fact]
    public Task ArmingTheVatPositionsAndWiresTheFaucetAndDisarmingHidesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previous = CoreSettings.ServiceProvider;
        var shell = Create();
        try
        {
            var glass = shell.ProfilePage!.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            var tap = shell.ProfilePage.FindControl<Grid>("ProfileVatFaucet")!;
            shell.ArmVat(glass, 1000);
            Assert.True(tap.IsVisible);
            Assert.NotNull(glass.ExternalSpoutXFraction);
            Assert.Equal(Math.Round(glass.Width * .10 - 8), tap.Margin.Left);
            shell.DisarmVat();
            Assert.False(tap.IsVisible);
            Assert.Null(glass.ExternalSpoutXFraction);
        }
        finally { shell.Close(); CoreSettings.ServiceProvider = previous; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PointerPressChargesAndReleaseOrExitCancelsWithoutPouring() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previous = CoreSettings.ServiceProvider;
        var previousAudio = CoreAudio.PlayOneShotProvider;
        var shell = Create();
        try
        {
            shell.Show();
            shell.ShowTab("discord");
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            Seed(shell);
            var cues = new List<string>();
            CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileName(path));
            CoreSettings.Current.MasterVolume = 50;
            var page = shell.ProfilePage!;
            page.FindControl<Grid>("ProfileCardWrapper")!.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            using (shell.CaptureRenderedFrame()) { } // hit testing reads the last rendered frame
            var tap = page.FindControl<Grid>("ProfileVatFaucet")!;
            var ring = page.FindControl<Grid>("ProfileVatChargeRing")!;
            var p = tap.TranslatePoint(new Point(tap.Bounds.Width / 2, tap.Bounds.Height / 2), shell)!.Value;

            shell.MouseMove(p);
            shell.MouseDown(p, MouseButton.Left);
            Assert.True(ring.IsVisible);
            shell.MouseUp(p, MouseButton.Left);
            Assert.False(ring.IsVisible);
            Assert.Equal("faucet_charge_drop.wav", cues.Last());

            cues.Clear();
            shell.MouseDown(p, MouseButton.Left);
            Assert.True(ring.IsVisible);
            tap.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, tap, new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true), shell, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Assert.False(ring.IsVisible);
            Assert.Equal("faucet_charge_drop.wav", cues.Last());
            shell.MouseUp(p, MouseButton.Left);

            cues.Clear();
            shell.MouseDown(p, MouseButton.Left);
            Assert.True(ring.IsVisible);
            tap.RaiseEvent(new PointerCaptureLostEventArgs(tap, new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true)));
            Assert.False(ring.IsVisible);
            Assert.DoesNotContain("faucet_charge_drop.wav", cues); // capture loss is silent
            Assert.Equal(0, CoreSettings.Current.VatPouredTodayXp);
            shell.DisarmVat();
        }
        finally { shell.Close(); CoreSettings.ServiceProvider = previous; CoreAudio.PlayOneShotProvider = previousAudio; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task KeyboardCancelKeepsTheHoldAndACompletedChargePoursOnlyOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previous = CoreSettings.ServiceProvider;
        var previousAudio = CoreAudio.PlayOneShotProvider;
        var shell = Create();
        try
        {
            shell.Show();
            shell.ShowTab("discord");
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            Seed(shell);
            var cues = new List<(string File, float Volume, string Tag)>();
            CoreAudio.PlayOneShotProvider = (path, volume, tag, _, _) => cues.Add((System.IO.Path.GetFileName(path), volume, tag));
            CoreSettings.Current.MasterVolume = 50;
            var page = shell.ProfilePage!;
            page.FindControl<Grid>("ProfileCardWrapper")!.IsVisible = true; // A fake server reading stands in for the missing profile feed.
            Dispatcher.UIThread.RunJobs();
            var tap = page.FindControl<Grid>("ProfileVatFaucet")!;
            var ring = page.FindControl<Grid>("ProfileVatChargeRing")!;
            var glass = page.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            if (Environment.GetEnvironmentVariable("CCP_TEST_EVIDENCE_DIR") is { } evidence)
            {
                System.IO.Directory.CreateDirectory(evidence);
                using var frame = shell.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(System.IO.Path.Combine(evidence, "ProfileFaucet.png"));
            }
            Assert.Equal(0, glass.Fill); // The entire unpoured server total is held, never auto-poured.
            tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
            Assert.True(ring.IsVisible);
            tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
            Assert.False(ring.IsVisible);
            Assert.Equal(0, CoreSettings.Current.VatPouredTodayXp);
            Assert.Equal("faucet_charge_drop.wav", cues.Last().File);
            tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            PanicSurfaces.All.Single(s => s.Id == "profile-faucet").Stop(shell); // StopAll resolves MainShellWindow.Current for the tray route.
            Assert.False(ring.IsVisible);
            Assert.Equal(0, CoreSettings.Current.VatPouredTodayXp);
            tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Enter });
            tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
            page.IsVisible = false; // The shell's actual tab-switch flag cancels the clock.
            Assert.False(ring.IsVisible);
            Assert.Equal(0, CoreSettings.Current.VatPouredTodayXp);
            page.IsVisible = true;
            cues.Clear();
            tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            shell.AdvanceFaucetCharge(1.0);
            Assert.False(ring.IsVisible);
            Assert.Equal(400, CoreSettings.Current.VatPouredTodayXp);
            Assert.True(glass.IsPouring);
            Assert.Equal(Enumerable.Range(1, 6).Select(i => $"faucet_charge_{i}.wav").Concat(new[] { "faucet_pour.wav" }), cues.Select(c => c.File));
            Assert.All(cues.Take(6), cue => Assert.Equal(.075f, cue.Volume));
            Assert.Equal(.175f, cues.Last().Volume);
            Assert.All(cues, cue => Assert.Equal("vat-faucet", cue.Tag));
            int count = cues.Count;
            shell.AdvanceFaucetCharge(1.0);
            Assert.Equal(400, CoreSettings.Current.VatPouredTodayXp);
            Assert.Equal(count, cues.Count);
            shell.DisarmVat();
        }
        finally { shell.Close(); CoreSettings.ServiceProvider = previous; CoreAudio.PlayOneShotProvider = previousAudio; }
        return Task.CompletedTask;
    });

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
