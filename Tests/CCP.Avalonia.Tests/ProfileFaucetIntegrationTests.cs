using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileFaucetIntegrationTests
{
    [Fact]
    public Task AcceptedReadingsRestoreHoldAndFollowTheUserPour() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, AvApp.Descent, CoreAudio.PlayOneShotProvider);
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        s.UnifiedId = "faucet-integration"; s.OfflineMode = false; s.MotionLevel = MotionLevel.Off;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        CoreSecrets.RetrieveProvider = _ => "test-token"; CoreSecrets.StoreProvider = (_, _) => { };
        CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => { };
        var reply = DescentReader.ParseWire("{\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":4000,\"today_xp\":1000,\"fill_lip_pct\":120}}}")!;
        var service = new DescentService(_ => Task.FromResult<JObject?>(reply)); AvApp.Descent = service;
        MainShellWindow? shell = null;
        try
        {
            new AppSettingsVatPourLedger().Record(400);
            await service.RefreshAsync("seed", force: true);
            shell = new MainShellWindow(); shell.Show(); shell.Activate(); shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();
            var page = shell.ProfilePage!;
            var glass = page.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            var tap = page.FindControl<Grid>("ProfileVatFaucet")!;
            Assert.True(glass.IsVisible); Assert.True(tap.IsVisible);
            Assert.Equal(.1, glass.Fill, 3); Assert.Equal(600, shell.FaucetHeldXp);
            var capBefore = page.FindControl<IconGlyph>("ProfileVatTickCap")!.Margin.Top;
            shell.ShowTab("settings");
            reply["descent"]!["vat"]!["today_xp"] = 1200;
            await service.RefreshAsync("earn away", force: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(800, shell.FaucetHeldXp);
            glass.SnapTo(.3); // Tab entry restores the held display if the visual drifted.
            shell.ShowTab("discord"); shell.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(.1, glass.Fill, 3);
            reply["descent"]!["vat"]!["cap"] = 5000;
            reply["descent"]!["vat"]!["fill_lip_pct"] = 130;
            await service.RefreshAsync("retune", force: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(.08, glass.Fill, 3);
            var cap = page.FindControl<IconGlyph>("ProfileVatTickCap")!;
            Assert.Equal(Math.Round(glass.TickCenterY(VatGlassCanvas.VatTickMark.Cap)!.Value - cap.Size / 2), cap.Margin.Top);
            Assert.NotEqual(capBefore, cap.Margin.Top);
            var max = page.FindControl<IconGlyph>("ProfileVatTickMax")!;
            Assert.True(max.IsVisible);
            Assert.Equal(Math.Round(glass.TickCenterY(VatGlassCanvas.VatTickMark.Max)!.Value - max.Size / 2), max.Margin.Top);
            Assert.Equal(LocReadout(400, 5000, 8), page.FindControl<TextBlock>("ProfileVatReadout")!.Text);
            tap.Focus();
            tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
            Assert.True(shell.FaucetCharging); Assert.Equal(400, s.VatPouredTodayXp);
            shell.AdvanceFaucetCharge(1);
            Assert.Equal(1200, s.VatPouredTodayXp); Assert.Equal(0, shell.FaucetHeldXp);
            Assert.True(glass.IsPouring);
            var remaining = typeof(VatGlassCanvas).GetField("_pourT", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            remaining.SetValue(glass, .05); // An extension must replenish the remaining stream time.
            reply["descent"]!["vat"]!["today_xp"] = 1400;
            await service.RefreshAsync("mid-pour earn", force: true); Dispatcher.UIThread.RunJobs();
            Assert.True(glass.IsPouring); Assert.True((double)remaining.GetValue(glass)! > .05);
            Assert.Equal(1400, s.VatPouredTodayXp);
            shell.Close(); shell = new MainShellWindow(); shell.Show(); shell.Activate(); shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(.28, shell.ProfilePage!.FindControl<VatGlassCanvas>("ProfileVatGlass")!.Fill, 3);
            s.OfflineMode = true; service.Reset(); Dispatcher.UIThread.RunJobs();
            Assert.False(shell.ProfilePage.FindControl<VatGlassCanvas>("ProfileVatGlass")!.IsVisible);
        }
        finally
        {
            shell?.Close(); s.OfflineMode = true; service.Reset();
            (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, AvApp.Descent, CoreAudio.PlayOneShotProvider) = old;
        }
    });

    private sealed class ChargeClock : TimeProvider
    {
        public double Seconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => (long)(Seconds * 1000);
    }

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Off)]
    public Task CorrectionDuringKeyboardChargeRestoresTheTap(MotionLevel motion) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, AvApp.Descent, CoreAudio.PlayOneShotProvider, BeatLoop.Time);
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current; s.UnifiedId = "faucet-correction"; s.OfflineMode = false; s.MotionLevel = motion;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        CoreSecrets.RetrieveProvider = _ => "test-token"; CoreSecrets.StoreProvider = (_, _) => { };
        var sounds = new System.Collections.Generic.List<string>();
        CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => sounds.Add(System.IO.Path.GetFileName(path));
        var clock = new ChargeClock(); BeatLoop.Time = clock;
        var reply = DescentReader.ParseWire("{\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":4000,\"today_xp\":1000,\"fill_lip_pct\":120}}}")!;
        var service = new DescentService(_ => Task.FromResult<JObject?>(reply)); AvApp.Descent = service;
        MainShellWindow? shell = null;
        try
        {
            await service.RefreshAsync("seed", force: true);
            shell = new MainShellWindow(); shell.Show(); shell.Activate(); shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();
            var tap = shell.ProfilePage!.FindControl<Grid>("ProfileVatFaucet")!;
            var scale = (ScaleTransform)((TransformGroup)tap.RenderTransform!).Children[1];
            tap.Focus(); tap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
            clock.Seconds = .1; BeatLoop.StepAll();
            Assert.True(shell.FaucetCharging); Assert.Equal(.94, scale.ScaleX, 3);
            reply["descent"]!["vat"]!["today_xp"] = 0;
            await service.RefreshAsync("correction", force: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, shell.FaucetHeldXp); Assert.True(shell.FaucetCharging);
            shell.AdvanceFaucetCharge(1); clock.Seconds = .3; BeatLoop.StepAll();
            Assert.False(shell.FaucetCharging); Assert.Equal(1, scale.ScaleX, 3); Assert.Equal(1, scale.ScaleY, 3);
            Assert.Equal(0, s.VatPouredTodayXp); Assert.DoesNotContain("faucet_pour.wav", sounds);
        }
        finally
        {
            try { shell?.Close(); s.OfflineMode = true; service.Reset(); }
            finally { (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, AvApp.Descent, CoreAudio.PlayOneShotProvider, BeatLoop.Time) = old; }
        }
    });

    private static string LocReadout(int shown, int cap, int pct) => ConditioningControlPanel.Localization.Loc.GetF("profile_vat_readout", shown.ToString("N0"), cap.ToString("N0"), pct);

    [Fact]
    public Task IntroWaitsUntilTheArmedCardIsVisible() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, AvApp.Descent);
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current; s.UnifiedId = "vat-intro"; s.OfflineMode = false;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys); s.SeenFeatureIntros.Remove("descent-vat");
        CoreSecrets.RetrieveProvider = _ => "test-token"; CoreSecrets.StoreProvider = (_, _) => { };
        StartupLadder.ResetForTests(); FeatureIntroPopup.ResetForTests(); MainShellWindow.IsStartupDialogShowing = true;
        var reply = DescentReader.ParseWire("{\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":4000,\"today_xp\":1000,\"fill_lip_pct\":120}}}")!;
        var service = new DescentService(_ => Task.FromResult<JObject?>(reply)); AvApp.Descent = service;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow(); shell.Show(); Dispatcher.UIThread.RunJobs();
            await service.RefreshAsync("offscreen", force: true); Dispatcher.UIThread.RunJobs();
            Assert.False(StartupLadder.Inbox.Contains("intro:descent-vat"));
            shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();
            Assert.True(StartupLadder.Inbox.Contains("intro:descent-vat"));
        }
        finally
        {
            shell?.Close(); s.OfflineMode = true; service.Reset(); MainShellWindow.IsStartupDialogShowing = false;
            StartupLadder.ResetForTests(); FeatureIntroPopup.ResetForTests();
            (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, AvApp.Descent) = old;
        }
    });
}
