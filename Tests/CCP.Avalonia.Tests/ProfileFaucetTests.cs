using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using System.Collections.Concurrent;
using System.Threading;
using ConditioningControlPanel.Services.Haptics.Core;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using AvApp = ConditioningControlPanel.Avalonia.App;
using Xunit;
namespace CCP.Avalonia.Tests;
[Collection(RunsAloneCollection.Name)]
public sealed class ProfileFaucetTests
{
    private static Action? _restoreFaucetService;

    private static void CloseFaucetTest(MainShellWindow? shell)
    {
        try { shell?.Close(); }
        finally { _restoreFaucetService?.Invoke(); _restoreFaucetService = null; }
    }

    private static MainShellWindow Create()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.MasterVolume = 50;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        CoreSettings.Current.UnifiedId = "faucet-test";
        CoreSettings.Current.OfflineMode = false;
        CoreSettings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        var previous = (AvApp.Descent, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        CoreSecrets.RetrieveProvider = _ => "test-token"; CoreSecrets.StoreProvider = (_, _) => { };
        var reply = DescentReader.ParseWire("{\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":1000,\"today_xp\":400,\"fill_lip_pct\":120}}}")!;
        var service = new DescentService(_ => Task.FromResult<JObject?>(reply));
        _restoreFaucetService = () =>
        {
            CoreSettings.Current.OfflineMode = true; service.Reset();
            (AvApp.Descent, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = previous;
        };
        AvApp.Descent = service; service.RefreshAsync("seed", force: true).GetAwaiter().GetResult();
        var shell = new MainShellWindow();
        shell.Show(); shell.ShowTab("discord"); shell.Activate();
        shell.ProfilePage!.FindControl<Grid>("ProfileCardWrapper")!.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        var step = shell.FaucetHold.Fold(new VatRead { Kind = VatReadKind.Seed, Fill = .4, TodayXp = 400, Cap = 1000 }, false);
        Assert.Equal(0, step.Fill); // Unpoured server XP never auto-pours on arm.
        shell.ArmVat(shell.ProfilePage.FindControl<VatGlassCanvas>("ProfileVatGlass")!, 1000);
        return shell;
    }
    [Theory]
    [InlineData(MotionLevel.Off)]
    [InlineData(MotionLevel.Full)]
    public Task KeyboardChargeCancelsSilentlyForPanicAndTabExitThenPoursExactlyOnce(MotionLevel motion) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previousAudio = CoreAudio.PlayOneShotProvider;
        var cues = new List<(string File, float Volume)>();
        CoreAudio.PlayOneShotProvider = (path, volume, _, _, _) => cues.Add((System.IO.Path.GetFileName(path), volume));
        var shell = Create();
        try
        {
            CoreSettings.Current.MotionLevel = motion;
            var page = shell.ProfilePage!;
            var tap = page.FindControl<Grid>("ProfileVatFaucet")!;
            var ring = page.FindControl<Grid>("ProfileVatChargeRing")!;
            var glass = page.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            void Key(global::Avalonia.Interactivity.RoutedEvent e, Key key) => tap.RaiseEvent(new KeyEventArgs { RoutedEvent = e, Key = key });
            Assert.True(tap.IsVisible);
            Assert.NotNull(glass.ExternalSpoutXFraction);
            Assert.Equal(Math.Round(glass.Width * .1 - 8), tap.Margin.Left);
            Assert.True(page.FindControl<Border>("ProfileVatChip")!.IsVisible);
            Assert.True(page.FindControl<Ellipse>("ProfileVatFaucetBadge")!.IsVisible);
            Key(InputElement.KeyDownEvent, global::Avalonia.Input.Key.Space);
            Assert.True(ring.IsVisible);
            if (motion == MotionLevel.Off) Assert.IsType<EllipseGeometry>(page.FindControl<Path>("ProfileVatChargeArc")!.Data);
            Key(InputElement.KeyDownEvent, global::Avalonia.Input.Key.Space); // repeat must not restart
            Assert.Single(cues);
            Key(InputElement.KeyUpEvent, global::Avalonia.Input.Key.Space);
            Assert.False(ring.IsVisible);
            Assert.Equal("faucet_charge_drop.wav", cues.Last().File);
            Assert.Equal(400, shell.FaucetHeldXp);
            Key(InputElement.KeyDownEvent, global::Avalonia.Input.Key.Enter);
            PanicSurfaces.All.Single(s => s.Id == "profile-faucet").Stop(shell);
            Assert.False(shell.FaucetCharging);
            Assert.Equal("faucet_charge_1.wav", cues.Last().File); // panic is silent
            Key(InputElement.KeyUpEvent, global::Avalonia.Input.Key.Enter);
            Key(InputElement.KeyDownEvent, global::Avalonia.Input.Key.Space);
            shell.ShowTab("settings");
            shell.AdvanceFaucetCharge(1);
            Assert.False(ring.IsVisible);
            Assert.Equal(0, CoreSettings.Current.VatPouredTodayXp);
            shell.ShowTab("discord"); shell.Activate(); Dispatcher.UIThread.RunJobs();
            cues.Clear();
            Key(InputElement.KeyDownEvent, global::Avalonia.Input.Key.Enter);
            shell.AdvanceFaucetCharge(.5);
            if (motion == MotionLevel.Full) Assert.IsType<StreamGeometry>(page.FindControl<Path>("ProfileVatChargeArc")!.Data);
            Assert.Equal(0, CoreSettings.Current.VatPouredTodayXp);
            shell.AdvanceFaucetCharge(1);
            Assert.False(ring.IsVisible);
            Assert.Equal(400, CoreSettings.Current.VatPouredTodayXp);
            Assert.Equal(0, shell.FaucetHeldXp);
            Assert.Equal(Enumerable.Range(1, 6).Select(i => $"faucet_charge_{i}.wav").Append("faucet_pour.wav"), cues.Select(c => c.File));
            Assert.All(cues.Take(6), c => Assert.Equal(.075f, c.Volume));
            Assert.Equal(.175f, cues.Last().Volume);
            shell.AdvanceFaucetCharge(1);
            Assert.Equal(7, cues.Count);
            shell.DisarmVat();
            Assert.False(tap.IsVisible);
            Assert.Null(glass.ExternalSpoutXFraction);
        }
        finally { try { CloseFaucetTest(shell); } finally { CoreAudio.PlayOneShotProvider = previousAudio; } }
        return Task.CompletedTask;
    });
    [Fact]
    public Task PointerReleaseExitAndCaptureLossKeepTheWatermark() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previousAudio = CoreAudio.PlayOneShotProvider;
        var cues = new List<string>();
        CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileName(path));
        var shell = Create();
        try
        {
            var tap = shell.ProfilePage!.FindControl<Grid>("ProfileVatFaucet")!;
            using (shell.CaptureRenderedFrame()) { }
            var p = tap.TranslatePoint(new Point(tap.Bounds.Width / 2, tap.Bounds.Height / 2), shell)!.Value;
            shell.MouseMove(p); shell.MouseDown(p, MouseButton.Left);
            Assert.True(shell.FaucetCharging);
            shell.MouseUp(p, MouseButton.Left);
            Assert.False(shell.FaucetCharging);
            Assert.Equal("faucet_charge_drop.wav", cues.Last());
            shell.MouseDown(p, MouseButton.Left);
            Assert.True(shell.FaucetCharging);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            tap.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, tap, pointer, shell, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Assert.False(shell.FaucetCharging);
            shell.MouseUp(p, MouseButton.Left);
            shell.MouseDown(p, MouseButton.Left);
            Assert.True(shell.FaucetCharging);
            tap.RaiseEvent(new PointerCaptureLostEventArgs(tap, pointer));
            Assert.False(shell.FaucetCharging);
            Assert.Equal("faucet_charge_1.wav", cues.Last());
            Assert.Equal(400, shell.FaucetHeldXp);
            Assert.Equal(0, CoreSettings.Current.VatPouredTodayXp);
        }
        finally { try { CloseFaucetTest(shell); } finally { CoreAudio.PlayOneShotProvider = previousAudio; } }
        return Task.CompletedTask;
    });
    [Fact]
    public Task PourCrossingTheLipAddsOneBrimCueAndHonorsTheHapticRow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previous = (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider, MockProviderV2.Toast, CoreAudio.PlayOneShotProvider);
        var cues = new List<string>();
        CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileName(path));
        var shell = Create();
        var toasts = new ConcurrentQueue<string>();
        CoreEntitlement.HasPremiumProvider = () => true;
        MockProviderV2.Toast = toasts.Enqueue;
        var settings = CoreSettings.Current.Haptics;
        settings.EnsureV2Migrated(); settings.Enabled = true;
        settings.V2.Provider("mock").Enabled = true;
        settings.V2.Provider("buttplug").Enabled = settings.V2.Provider("lovense").Enabled = false;
        using var haptics = new HapticService(settings);
        CoreHaptics.Service = haptics;
        try
        {
            Assert.True(haptics.ConnectAsync().Result);
            var rule = settings.V2.Rule(HapticEventKind.LevelUp);
            int xp = 0;
            foreach (var (enabled, intensity) in new[] { (false, 1.0), (true, 0.0), (true, 1.0) })
            {
                rule.Enabled = enabled; rule.Intensity = intensity; xp += 1300;
                var glass = shell.ProfilePage!.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
                shell.FaucetHold.Fold(new VatRead { Kind = VatReadKind.Seed, Fill = 1.3, TodayXp = xp, Cap = 1000 }, false);
                glass.SnapTo(0); glass.SetLip(1.2); cues.Clear(); toasts.Clear();
                shell.BeginFaucetCharge(); shell.AdvanceFaucetCharge(1);
                Assert.Single(cues, c => c == "faucet_brim.wav");
                if (enabled && intensity > 0) Assert.True(SpinWait.SpinUntil(() => toasts.Any(t => t.Contains('%')), 3000));
                else { Thread.Sleep(100); Assert.DoesNotContain(toasts, t => t.Contains('%')); }
            }
        }
        finally
        {
            try { haptics.PanicStop(); CloseFaucetTest(shell); }
            finally { (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider, MockProviderV2.Toast, CoreAudio.PlayOneShotProvider) = previous; }
        }
        return Task.CompletedTask;
    });
    private sealed class FaucetClock : TimeProvider
    {
        public double Seconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => (long)(Seconds * 1000);
    }

    [Fact]
    public Task WaitingMotionFollowsTheHoldAndParksWithTheCard() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldClock = BeatLoop.Time;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var clock = new FaucetClock();
        var cues = new List<string>();
        BeatLoop.Time = clock;
        CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileName(path));
        MainShellWindow? shell = null;
        try
        {
            shell = Create();
            var page = shell.ProfilePage!;
            var tap = page.FindControl<Grid>("ProfileVatFaucet")!;
            var chip = page.FindControl<Border>("ProfileVatChip")!;
            var drop = page.FindControl<Ellipse>("ProfileVatFaucetDrop")!;
            var badge = page.FindControl<Ellipse>("ProfileVatFaucetBadge")!;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(badge.IsVisible);
            var tilt = Assert.IsType<RotateTransform>(Assert.IsType<TransformGroup>(tap.RenderTransform).Children[2]);
            var scale = Assert.IsType<ScaleTransform>(chip.RenderTransform);
            var fall = Assert.IsType<TranslateTransform>(drop.RenderTransform);
            Assert.Equal(-2.2, tilt.Angle, 3);
            clock.Seconds = .45; BeatLoop.StepAll();
            Assert.Equal(.85, drop.Opacity, 3);
            Assert.Equal(1.75, fall.Y, 3);
            clock.Seconds = .55; BeatLoop.StepAll();
            Assert.Equal(2.2, tilt.Angle, 3);
            clock.Seconds = 2.8; BeatLoop.StepAll();
            Assert.Equal(1.05, scale.ScaleX, 3);
            Assert.Equal(scale.ScaleX, scale.ScaleY);
            Assert.Empty(cues);
            clock.Seconds = 3.8; BeatLoop.StepAll();
            Assert.Equal(new[] { "faucet_tick.wav" }, cues);
            // Headless windows do not deactivate each other, so deliver the platform callback.
            typeof(WindowBase).GetMethod("HandleDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(shell, null);
            Assert.False(shell.IsActive);
            Assert.Contains(BeatLoop.Running, loop => loop.Owner == tap);
            cues.Clear(); clock.Seconds = 7.61; BeatLoop.StepAll();
            Assert.Empty(cues); // WPF keeps the wobble but suppresses ticks in the background.
            typeof(WindowBase).GetMethod("HandleActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(shell, null);

            shell.BeginFaucetCharge();
            Assert.Equal(0, tilt.Angle);
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal(0, drop.Opacity);
            shell.CancelFaucetCharge(silent: true);
            Assert.NotEqual(0, tilt.Angle);
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(badge.IsVisible);
            Assert.Equal(0, tilt.Angle);
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal(0, drop.Opacity);
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            CoreSettings.Current.PerformanceMode = true;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(badge.IsVisible);
            Assert.Equal(0, tilt.Angle);
            CoreSettings.Current.PerformanceMode = false;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.NotEqual(0, tilt.Angle);
            shell.ShowTab("settings");
            Assert.Equal(0, tilt.Angle);
            Assert.Equal(1, scale.ScaleX);
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == tap);
            shell.ShowTab("discord"); shell.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(0, tilt.Angle);
            shell.WindowState = WindowState.Minimized; Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, tilt.Angle);
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == tap);
            shell.WindowState = WindowState.Normal; shell.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(0, tilt.Angle);
            shell.DisarmVat();
            Assert.Equal(0, tilt.Angle);
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == tap);
        }
        finally
        {
            try { CloseFaucetTest(shell); }
            finally { BeatLoop.Time = oldClock; CoreAudio.PlayOneShotProvider = oldAudio; }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WaitingMotionRestartsWhenSearchHidesAndReopensTheCard() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldClock = BeatLoop.Time;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var clock = new FaucetClock(); BeatLoop.Time = clock;
        var cues = new List<string>();
        CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileName(path));
        MainShellWindow? shell = null;
        try
        {
            shell = Create();
            var page = shell.ProfilePage!;
            var wrapper = page.FindControl<Grid>("ProfileCardWrapper")!;
            var tap = page.FindControl<Grid>("ProfileVatFaucet")!;
            var chip = page.FindControl<Border>("ProfileVatChip")!;
            var drop = page.FindControl<Ellipse>("ProfileVatFaucetDrop")!;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            CoreSettings.Current.PerformanceMode = false;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            var tilt = Assert.IsType<RotateTransform>(Assert.IsType<TransformGroup>(tap.RenderTransform).Children[2]);
            var breath = Assert.IsType<ScaleTransform>(chip.RenderTransform);
            var fall = Assert.IsType<TranslateTransform>(drop.RenderTransform);
            Assert.Contains(BeatLoop.Running, loop => loop.Owner == tap && loop.OnFrameBeat);
            CoreSettings.Current.OfflineMode = true;
            await page.SearchAsync("no-such-faucet-test-user"); Dispatcher.UIThread.RunJobs();
            Assert.False(wrapper.IsVisible);
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == tap);
            Assert.Equal(0, tilt.Angle); Assert.Equal(1, breath.ScaleX); Assert.Equal(0, drop.Opacity);
            clock.Seconds = 10; BeatLoop.StepAll(); Assert.Empty(cues);
            await page.ViewMyProfileAsync(); Dispatcher.UIThread.RunJobs();
            Assert.True(wrapper.IsVisible);
            Assert.Contains(BeatLoop.Running, loop => loop.Owner == tap && loop.OnFrameBeat);
            Assert.Equal(-2.2, tilt.Angle, 3);
            clock.Seconds = 10.45; BeatLoop.StepAll();
            Assert.Equal(.85, drop.Opacity, 3); Assert.Equal(1.75, fall.Y, 3);
            clock.Seconds = 12.8; BeatLoop.StepAll(); Assert.Equal(1.05, breath.ScaleX, 3);
            clock.Seconds = 13.8; BeatLoop.StepAll(); Assert.Equal(new[] { "faucet_tick.wav" }, cues);
            Assert.Equal(400, shell.FaucetHeldXp); Assert.Equal(0, CoreSettings.Current.VatPouredTodayXp);
        }
        finally
        {
            try { CloseFaucetTest(shell); }
            finally { BeatLoop.Time = oldClock; CoreAudio.PlayOneShotProvider = oldAudio; }
        }
    });

    [Fact]
    public Task HidingTheCardDuringAChargeCancelsWithoutPouring() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var cues = new List<string>();
        CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileName(path));
        MainShellWindow? shell = null;
        try
        {
            shell = Create();
            var page = shell.ProfilePage!;
            var ring = page.FindControl<Grid>("ProfileVatChargeRing")!;
            shell.BeginFaucetCharge(); Assert.True(shell.FaucetCharging);
            CoreSettings.Current.OfflineMode = true;
            await page.SearchAsync("no-such-faucet-test-user"); Dispatcher.UIThread.RunJobs();
            Assert.False(shell.FaucetCharging); Assert.False(ring.IsVisible);
            shell.AdvanceFaucetCharge(1);
            Assert.Equal(400, shell.FaucetHeldXp); Assert.Equal(0, CoreSettings.Current.VatPouredTodayXp);
            Assert.DoesNotContain("faucet_charge_drop.wav", cues);
            await page.ViewMyProfileAsync(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(400, shell.FaucetHeldXp); Assert.False(shell.FaucetCharging);
        }
        finally
        {
            try { CloseFaucetTest(shell); }
            finally { CoreAudio.PlayOneShotProvider = oldAudio; }
        }
    });

    [Fact]
    public Task HoverPressReleaseAndPourFeedbackShareTheWaitingTransforms() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldClock = BeatLoop.Time;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var clock = new FaucetClock(); BeatLoop.Time = clock;
        CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => { };
        MainShellWindow? shell = null;
        try
        {
            shell = Create();
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            CoreSettings.Current.PerformanceMode = false;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            var page = shell.ProfilePage!;
            var tap = page.FindControl<Grid>("ProfileVatFaucet")!;
            var group = Assert.IsType<TransformGroup>(tap.RenderTransform);
            var rock = Assert.IsType<RotateTransform>(group.Children[2]);
            var scale = Assert.IsType<ScaleTransform>(group.Children[1]);
            var lean = Assert.IsType<RotateTransform>(group.Children[3]);
            var shake = Assert.IsType<TranslateTransform>(group.Children[0]);
            var drop = page.FindControl<Ellipse>("ProfileVatFaucetDrop")!;
            var handle = Assert.IsType<ScaleTransform>(page.FindControl<Grid>("ProfileVatHandle")!.RenderTransform);
            var sparks = page.FindControl<Canvas>("ProfileVatSparks")!;
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            tap.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, tap, pointer, shell, default, 0, new PointerPointProperties(), KeyModifiers.None));
            clock.Seconds = .15; BeatLoop.StepAll(); Assert.Equal(1.16, scale.ScaleX, 3);
            shell.BeginFaucetCharge();
            clock.Seconds = .24; BeatLoop.StepAll(); Assert.Equal(.94, scale.ScaleX, 3);
            Assert.Equal(0, rock.Angle);
            shell.CancelFaucetCharge(silent: false);
            clock.Seconds = .30; BeatLoop.StepAll();
            Assert.True(shake.X < 0); Assert.True(drop.Opacity > 0);
            Assert.NotEqual(0, rock.Angle); // waiting resumes without replacing the shiver
            clock.Seconds = .77; BeatLoop.StepAll();
            Assert.Equal(0, shake.X); Assert.Empty(sparks.Children);
            shell.BeginFaucetCharge(); shell.AdvanceFaucetCharge(1);
            Assert.Equal(2.1, handle.ScaleX, 3); Assert.InRange(sparks.Children.Count, 5, 9);
            clock.Seconds = .89; BeatLoop.StepAll();
            Assert.InRange(lean.Angle, 1, 14); Assert.InRange(handle.ScaleX, .86, 1);
            var glass = page.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            glass.SnapTo(glass.Fill); // stream ended; the watch rights the tap
            shell.FaucetHold.Fold(new VatRead { Kind = VatReadKind.Pour, Fill = .5, TodayXp = 500, DeltaXp = 100, Cap = 1000 }, false);
            var chip = page.FindControl<Border>("ProfileVatChip")!;
            Assert.False(chip.IsVisible);
            clock.Seconds = 1.4; BeatLoop.StepAll();
            Assert.True(chip.IsVisible); // the 250 ms settle watch refreshes the waiting state
            clock.Seconds = 1.65; BeatLoop.StepAll();
            Assert.Equal(0, lean.Angle, 3); Assert.Equal(1, handle.ScaleX, 3); Assert.Empty(sparks.Children);
            shell.ShowTab("settings");
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == tap);
        }
        finally { CloseFaucetTest(shell); BeatLoop.Time = oldClock; CoreAudio.PlayOneShotProvider = oldAudio; }
        return Task.CompletedTask;
    });

}
