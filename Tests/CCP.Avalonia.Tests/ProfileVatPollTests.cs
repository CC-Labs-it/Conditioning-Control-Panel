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
    public Task PollWaitsForTheDrawnJarThenNeedsTheVisibleActiveCardAndAnAcceptedBlock() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.UnifiedId, s.OfflineMode, global::ConditioningControlPanel.Avalonia.App.Descent, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        string? token = "test-token";
        var reply = DescentReader.ParseWire("{\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":4000,\"today_xp\":1000,\"fill_lip_pct\":120}}}")!;
        var service = new DescentService(_ => Task.FromResult<JObject?>(reply));
        global::ConditioningControlPanel.Avalonia.App.Descent = service;
        MainShellWindow? shell = null;
        try
        {
            CoreSecrets.RetrieveProvider = name => name == CoreSecrets.AuthToken ? token : null;
            CoreSecrets.StoreProvider = (name, value) => { if (name == CoreSecrets.AuthToken) token = value; };
            (s.UnifiedId, s.OfflineMode) = ("vat-test", false);
            await service.RefreshAsync("accepted", force: true);
            shell = new MainShellWindow();
            shell.Show(); shell.Activate(); Dispatcher.UIThread.RunJobs();
            shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();   // the user's door to the Trainer Card
            var page = shell.ProfilePage!;
            Assert.True(page.IsVisible);
            var glass = page.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            // The faucet host is a follow-up: no misleading jar is revealed just because data arrived,
            // and no 60 s poll runs for a jar nobody sees (P07).
            Assert.False(glass.IsVisible);
            Assert.False(shell.VatPollRunning);
            // Stand-in for lane -b's ApplyDescentToVat, the only future ArmVat caller.
            shell.ArmVat(glass, 4000);
            Assert.True(shell.VatPollRunning);
            shell.WindowState = WindowState.Minimized;
            Assert.False(shell.VatPollRunning);
            shell.WindowState = WindowState.Normal;
            shell.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.True(shell.VatPollRunning);
            shell.ShowTab("settings"); Dispatcher.UIThread.RunJobs();
            Assert.False(shell.VatPollRunning);
            shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();
            Assert.True(shell.VatPollRunning);
            s.OfflineMode = true; service.Reset(); Dispatcher.UIThread.RunJobs();
            Assert.False(shell.VatPollRunning);
            s.OfflineMode = false; await service.RefreshAsync("rearm", force: true); Dispatcher.UIThread.RunJobs();
            Assert.True(shell.VatPollRunning);
            shell.Close();
            Assert.False(shell.VatPollRunning);
        }
        finally
        {
            shell?.Close(); service.Reset();
            (s.UnifiedId, s.OfflineMode, global::ConditioningControlPanel.Avalonia.App.Descent, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = saved;
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
