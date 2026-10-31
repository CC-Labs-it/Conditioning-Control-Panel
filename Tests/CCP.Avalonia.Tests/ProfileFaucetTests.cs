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
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.MasterVolume = 50;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
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
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task PointerReleaseExitAndCaptureLossKeepTheWatermark() => AvaloniaTestDispatcher.RunAsync(() =>
    {
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
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task PourCrossingTheLipAddsOneBrimCueAndHonorsTheHapticRow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
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
        finally { haptics.PanicStop(); shell.Close(); }
        return Task.CompletedTask;
    });

}
