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
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileVatPollTests
{
    [Fact]
    public Task PollNeedsTheVisibleActiveCardAndAnAcceptedBlockAndStopsOnLogoutOrClose() => AvaloniaTestDispatcher.RunAsync(async () =>
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
            var page = shell.ProfilePage!;
            page.IsVisible = true;
            shell.OnProfileVatVisibilityChanged(true);
            Assert.True(shell.VatPollRunning);
            // The faucet host is a follow-up: no misleading jar is revealed just because data arrived.
            Assert.False(page.FindControl<VatGlassCanvas>("ProfileVatGlass")!.IsVisible);
            shell.WindowState = WindowState.Minimized;
            Assert.False(shell.VatPollRunning);
            shell.WindowState = WindowState.Normal;
            shell.Activate(); Dispatcher.UIThread.RunJobs();
            shell.OnProfileVatVisibilityChanged(true);
            Assert.True(shell.VatPollRunning);
            page.IsVisible = false;
            Assert.False(shell.VatPollRunning);
            page.IsVisible = true;
            shell.OnProfileVatVisibilityChanged(true);
            Assert.True(shell.VatPollRunning);
            s.OfflineMode = true; service.Reset(); Dispatcher.UIThread.RunJobs();
            Assert.False(shell.VatPollRunning);
            s.OfflineMode = false; await service.RefreshAsync("rearm", force: true); Dispatcher.UIThread.RunJobs();
            shell.OnProfileVatVisibilityChanged(true);
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
            shell.ProfilePage!.IsVisible = true;
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

}
