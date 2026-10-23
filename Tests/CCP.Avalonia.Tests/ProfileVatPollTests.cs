using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileVatPollTests
{
    [Fact]
    public Task AnAcceptedBlockArmsTheJarHoldsItsXpAndGatesThePoll() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.UnifiedId, s.OfflineMode, global::ConditioningControlPanel.Avalonia.App.Descent, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider,
            s.VatPouredAccount, s.VatPouredDayUtc, s.VatPouredTodayXp);
        bool introWasSeen = s.SeenFeatureIntros.Contains("descent-vat");
        if (!introWasSeen) s.SeenFeatureIntros.Add("descent-vat");   // no modal over the test (P76)
        string? token = "test-token";
        int todayXp = 1000;
        var service = new DescentService(_ => Task.FromResult<JObject?>(DescentReader.ParseWire(
            "{\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":4000,\"today_xp\":" + todayXp + ",\"fill_lip_pct\":120}}}")));
        global::ConditioningControlPanel.Avalonia.App.Descent = service;
        MainShellWindow? shell = null;
        try
        {
            CoreSecrets.RetrieveProvider = name => name == CoreSecrets.AuthToken ? token : null;
            CoreSecrets.StoreProvider = (name, value) => { if (name == CoreSecrets.AuthToken) token = value; };
            (s.UnifiedId, s.OfflineMode, s.VatPouredAccount) = ("vat-test", false, null);
            await service.RefreshAsync("accepted", force: true);
            shell = new MainShellWindow();
            shell.Show(); shell.Activate(); Dispatcher.UIThread.RunJobs();
            var page = shell.ProfilePage!;
            var glass = page.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            Assert.False(glass.IsVisible);
            shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();   // the user's door to the Trainer Card
            Assert.True(page.IsVisible);
            // WPF ApplyDescentToVat: the block arms the jar and its tap; today's XP waits in the tap
            // (seeded, never auto-poured), so the drawn level stays at truth - held.
            Assert.True(glass.IsVisible);
            Assert.True(page.FindControl<Grid>("ProfileVatFaucet")!.IsVisible);
            Assert.Equal(1000, shell.FaucetHeldXp);
            Assert.Equal(0, glass.Fill);
            Assert.True(shell.VatPollRunning);
            // A later accepted reading adds its delta to the hold; the liquid does not move.
            todayXp = 1500;
            await service.RefreshAsync("poll", force: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1500, shell.FaucetHeldXp);
            Assert.Equal(0, glass.Fill);
            shell.WindowState = WindowState.Minimized;
            Assert.False(shell.VatPollRunning);
            shell.WindowState = WindowState.Normal;
            shell.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.True(shell.VatPollRunning);
            shell.ShowTab("settings"); Dispatcher.UIThread.RunJobs();
            Assert.False(shell.VatPollRunning);
            shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();
            Assert.True(shell.VatPollRunning);
            // Logout/offline withdraws the block: the jar disarms back to the 104px avatar.
            s.OfflineMode = true; service.Reset(); Dispatcher.UIThread.RunJobs();
            Assert.False(shell.VatPollRunning);
            Assert.False(glass.IsVisible);
            Assert.Equal(104, page.FindControl<global::ConditioningControlPanel.Avalonia.Views.Controls.AdornedAvatar>("ProfileHeroAvatar")!.AvatarSize);
            s.OfflineMode = false; await service.RefreshAsync("rearm", force: true); Dispatcher.UIThread.RunJobs();
            Assert.True(glass.IsVisible);
            Assert.True(shell.VatPollRunning);
            shell.Close();
            Assert.False(shell.VatPollRunning);
        }
        finally
        {
            shell?.Close(); service.Reset();
            if (!introWasSeen) s.SeenFeatureIntros.Remove("descent-vat");
            (s.UnifiedId, s.OfflineMode, global::ConditioningControlPanel.Avalonia.App.Descent, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider,
                s.VatPouredAccount, s.VatPouredDayUtc, s.VatPouredTodayXp) = saved;
        }
    });
    [Fact]
    public Task OpeningTheTrainerCardFetchesEvenForAnAccountWithoutABlock() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.UnifiedId, s.OfflineMode, global::ConditioningControlPanel.Avalonia.App.Descent,
            CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new DescentService(id =>
        {
            Assert.Equal("vat-open-test", id);
            called.TrySetResult();
            return Task.FromResult<JObject?>(new JObject());
        });
        MainShellWindow? shell = null;
        try
        {
            CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "test-token" : null;
            CoreSecrets.StoreProvider = (_, _) => { };
            (s.UnifiedId, s.OfflineMode) = ("vat-open-test", false);
            global::ConditioningControlPanel.Avalonia.App.Descent = service;
            shell = new MainShellWindow(); shell.Show();
            shell.ShowTab("discord");
            await called.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(service.HasSeenBlock);
            Assert.False(shell.VatPollRunning);
            Assert.False(shell.ProfilePage.FindControl<VatGlassCanvas>("ProfileVatGlass")!.IsVisible);
        }
        finally
        {
            s.OfflineMode = true; service.Reset(); shell?.Close();
            (s.UnifiedId, s.OfflineMode, global::ConditioningControlPanel.Avalonia.App.Descent,
                CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = saved;
        }
    });

    [Fact]
    public Task AFollowedCurveEpochRepaintsTheLevelHeader() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.PlayerLevel, s.PlayerXP, s.DescentEpoch, s.HighestLevelEver, s.PendingDescentMigrationChoice);
        MainShellWindow? shell = null;
        try
        {
            (s.PlayerLevel, s.PlayerXP, s.DescentEpoch, s.PendingDescentMigrationChoice) = (40, 100, XpCurve.CurveEpochLegacy, null);
            shell = new MainShellWindow(); shell.Show(); Dispatcher.UIThread.RunJobs();
            // Low levels price the same on both curves, so a sentinel (not a before/after diff) proves the repaint.
            shell.Named<TextBlock>("TxtXP")!.Text = "stale";
            shell.Named<TextBlock>("TxtLevelLabel")!.Text = "stale";
            global::ConditioningControlPanel.Avalonia.App.AdoptServerCurveEpoch(XpCurve.CurveEpochDescent, () => shell);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(XpCurve.CurveEpochDescent, s.DescentEpoch);
            var needed = XpCurve.GetXPForLevel(s.PlayerLevel, XpCurve.CurveEpochDescent);
            Assert.Equal($"LVL {s.PlayerLevel}", shell.Named<TextBlock>("TxtLevelLabel")!.Text);
            Assert.Equal($"{(int)s.PlayerXP} / {(int)needed} XP", shell.Named<TextBlock>("TxtXP")!.Text);
        }
        finally
        {
            shell?.Close();
            (s.PlayerLevel, s.PlayerXP, s.DescentEpoch, s.HighestLevelEver, s.PendingDescentMigrationChoice) = saved;
            CoreSettings.Save();
        }
        return Task.CompletedTask;
    });
}
