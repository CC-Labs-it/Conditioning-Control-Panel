using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileVatPathTests
{
    [Fact]
    public Task ServerHoldCanBeChargedAndWithdrawnOnTheRealCard() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new global::ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        s.UnifiedId = "vat-path"; s.OfflineMode = false; s.MasterVolume = 50;
        s.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
        s.VatPouredAccount = s.UnifiedId; s.VatPouredDayUtc = DateTime.UtcNow.ToString("yyyy-MM-dd");
        s.VatPouredTodayXp = 600;
        s.SeenFeatureIntros.Add("profile-hub"); s.SeenFeatureIntros.Add("descent-vat");
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "test-token" : null;
        CoreSecrets.StoreProvider = (_, _) => { };
        JObject? reply = Wire(1000);
        var service = new DescentService(_ => Task.FromResult(reply));
        global::ConditioningControlPanel.Avalonia.App.Descent = service;
        MainShellWindow? shell = null;
        var oldTime = BeatLoop.Time; var time = new SteppedTime(); BeatLoop.Time = time;
        var audio = CoreAudio.PlayOneShotProvider;
        var cues = new List<string>();
        CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileName(path));
        try
        {
            await service.RefreshAsync("seed", force: true);
            shell = new MainShellWindow(); shell.Show(); shell.ShowTab("discord"); shell.Activate();
            Dispatcher.UIThread.RunJobs();
            var page = shell.ProfilePage!;
            var glass = page.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            var tap = page.FindControl<Grid>("ProfileVatFaucet")!;
            Assert.True(glass.IsVisible); Assert.True(tap.IsVisible);
            Assert.Equal(400, shell.VatHeldXp); Assert.Equal(.15, glass.Fill, 6);
            Assert.False(glass.IsPouring); Assert.True(shell.VatPollRunning);
            Assert.NotNull(glass.ExternalSpoutXFraction);
            var tick = page.FindControl<IconGlyph>("ProfileVatTickMax")!;
            Assert.True(tick.IsVisible);
            Assert.Equal(Math.Round(glass.TickCenterY(VatGlassCanvas.VatTickMark.Max)!.Value - tick.Size / 2), tick.Margin.Top);
            void Key(global::Avalonia.Interactivity.RoutedEvent e) => tap.RaiseEvent(new KeyEventArgs { RoutedEvent = e, Key = global::Avalonia.Input.Key.Space });
            async Task Frame(int ms) { time.Advance(TimeSpan.FromMilliseconds(ms)); await Task.Delay(45); AvaloniaHeadlessPlatform.ForceRenderTimerTick(5); Dispatcher.UIThread.RunJobs(); await Task.Delay(45); }
            Key(InputElement.KeyDownEvent); Assert.True(shell.FaucetCharging);
            Assert.Equal(800d, (double)typeof(MainShellWindow).GetField("_faucetChargeBudgetMs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(shell)!);
            Assert.True(page.FindControl<Grid>("ProfileVatChargeRing")!.IsVisible);
            await Frame(400); Assert.Equal(600, s.VatPouredTodayXp);
            Key(InputElement.KeyUpEvent); Assert.False(shell.FaucetCharging);
            await Frame(500);
            Assert.Equal(600, s.VatPouredTodayXp); Assert.Equal(400, shell.VatHeldXp);
            using (shell.CaptureRenderedFrame()) { }
            var point = tap.TranslatePoint(new Point(tap.Bounds.Width / 2, tap.Bounds.Height / 2), shell)!.Value;
            shell.MouseMove(point); shell.MouseDown(point, MouseButton.Left);
            Assert.True(shell.FaucetCharging);
            shell.MouseUp(point, MouseButton.Left);
            Assert.False(shell.FaucetCharging); Assert.Equal(600, s.VatPouredTodayXp);
            Key(InputElement.KeyDownEvent); Assert.True(shell.FaucetCharging);
            PanicSurfaces.All.Single(surface => surface.Id == "profile-faucet").Stop(shell);
            Assert.False(shell.FaucetCharging); Assert.Equal(600, s.VatPouredTodayXp);
            Key(InputElement.KeyUpEvent);
            Key(InputElement.KeyDownEvent); Assert.True(shell.FaucetCharging);
            shell.ShowTab("settings"); Dispatcher.UIThread.RunJobs();
            await Frame(1000);
            Assert.False(shell.FaucetCharging); Assert.Equal(600, s.VatPouredTodayXp);
            shell.ShowTab("discord"); shell.Activate(); Dispatcher.UIThread.RunJobs();
            Key(InputElement.KeyDownEvent); Assert.True(shell.FaucetCharging);
            cues.Clear();
            var charge = Assert.Single(BeatLoop.Running, loop => loop.Owner == page.FindControl<Grid>("ProfileVatChargeRing"));
            Assert.True(charge.OnFrameBeat);
            await Frame(400); Assert.Equal(600, s.VatPouredTodayXp); Assert.True(shell.FaucetCharging);
            await Frame(500);
            Assert.Contains("faucet_charge_6.wav", cues); Assert.Contains("faucet_pour.wav", cues);
            Assert.Equal(1000, s.VatPouredTodayXp); Assert.Equal(0, shell.VatHeldXp);
            Assert.True(glass.IsPouring);
            reply = new JObject();
            await service.RefreshAsync("withdraw", force: true); Dispatcher.UIThread.RunJobs();
            Assert.False(glass.IsVisible); Assert.False(tap.IsVisible);
            Assert.False(shell.VatPollRunning); Assert.Null(glass.ExternalSpoutXFraction);
            Assert.Equal(104, page.FindControl<global::ConditioningControlPanel.Avalonia.Views.Controls.AdornedAvatar>("ProfileHeroAvatar")!.AvatarSize);
        }
        finally { try { s.OfflineMode = true; service.Reset(); shell?.Close(); } finally { BeatLoop.Time = oldTime; CoreAudio.PlayOneShotProvider = audio; } }
    });

    [Fact]
    public Task IntroWaitsForAVisibleVat() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
        FeatureIntroPopup.ResetForTests();
        var s = CoreSettings.Current;
        foreach (var key in FeatureIntros.All.Keys) if (!s.SeenFeatureIntros.Contains(key)) s.SeenFeatureIntros.Add(key);
        s.SeenFeatureIntros.Remove("descent-vat");
        s.UnifiedId = "vat-intro"; s.OfflineMode = false;
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "test-token" : null;
        CoreSecrets.StoreProvider = (_, _) => { };
        var service = new DescentService(_ => Task.FromResult<JObject?>(Wire(1000)));
        global::ConditioningControlPanel.Avalonia.App.Descent = service;
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            await service.RefreshAsync("off screen", force: true); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("descent-vat", s.SeenFeatureIntros);
            Assert.Empty(opened.OfType<FeatureIntroPopup>());
            shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();
            Assert.Contains("descent-vat", s.SeenFeatureIntros);
            Assert.Single(opened.OfType<FeatureIntroPopup>());
        }
        finally
        {
            s.OfflineMode = true; service.Reset();
            foreach (var window in opened.Where(w => w != shell).ToArray()) window.Close();
            shell.Close();
            global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
        }
    });

    private static JObject Wire(int xp) => JObject.Parse(
        "{\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":4000,\"today_xp\":" + xp + ",\"fill_lip_pct\":120}}}");
}
