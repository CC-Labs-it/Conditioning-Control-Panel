using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SyncProgressionRepaintTests
{
    [Fact]
    public Task ClampedSyncRepaintsAfterPanicWithoutRestartingAutonomy() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldPost = CoreDispatch.PostProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        CoreDispatch.PostProvider = action => Dispatcher.UIThread.Post(action);
        var s = settings.Current;
        s.UnifiedId = "sync-repaint-test"; s.AuthToken = "fake"; s.OfflineMode = false;
        s.CurrentSeason = "2025-01"; s.PlayerLevel = 80; s.PlayerXP = 0;
        s.LastConfirmedServerXp = 0; s.PendingDescentMigrationChoice = null;
        s.MotionLevel = MotionLevel.Off;
        s.AutonomyResumeOnStartup = false; s.AutonomyModeEnabled = false;
        var shell = new MainShellWindow();
        var sync = AccountSeed.NewSync(() => Array.Empty<string>(), () => false, new Reply());
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("LVL 80", shell.FindControl<TextBlock>("TxtLevelLabel")!.Text);
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            s.AutonomyResumeOnStartup = true; s.AutonomyModeEnabled = true; s.AutonomyConsentGiven = true;
            shell.StopAutonomyForPanic();
            sync.MarkLoaded(null);
            Assert.True(await sync.PushAsync("test"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("LVL 2", shell.FindControl<TextBlock>("TxtLevelLabel")!.Text);
            Assert.False(shell.Autonomy.IsEnabled);
        }
        finally
        {
            sync.Reset(); shell.Close(); Dispatcher.UIThread.RunJobs();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = oldSettings; CoreAccount.IsLoggedInProvider = oldLogin;
            CoreDispatch.PostProvider = oldPost;
            CoreEntitlement.HasPremiumProvider = oldPremium; CoreEntitlement.IsFreeTodayProvider = oldFree;
        }
    });

    private sealed class Reply : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"user\":{\"level\":2,\"xp\":1000,\"current_season\":\"2025-01\"}}") });
    }
}
