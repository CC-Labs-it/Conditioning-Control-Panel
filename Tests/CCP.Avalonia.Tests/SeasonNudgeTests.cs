using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SeasonNudgeTests
{
    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"user":{"level":1,"xp":0,"current_season":"2098-08"}}""") });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ServerSeasonRollsAndRevealsRecapWhileShellIsUp(bool readBeforeWrite) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        AmbientBeatLoopsTests.Setup();
        var service = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldSync = AccountSeed.Sync;
        AccountSeed.Sync = null;
        CoreSettings.ServiceProvider = () => service;
        CoreAccount.IsLoggedInProvider = () => true;
        var path = System.IO.Path.Combine(CorePaths.UserData, "season-recaps", "2098-07.json");
        MainShellWindow? shell = null;
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, new Wire())
            { SeasonNudge = AccountSeed.NudgeSeason };
        try
        {
            var s = service.Current;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = true;
            s.CurrentSeason = "2098-07";
            s.LastSeasonResetSeen = "2098-07";
            s.SeasonStatsSeason = "2098-07";
            s.SeasonSessionsStarted = 3;
            s.UnifiedId = "test";
            s.AuthToken = "test";
            s.OfflineMode = false;
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            s.SeasonResetPending = true;
            sync.MarkLoaded(null);
            if (readBeforeWrite)
                Assert.True(await AccountSeed.LoadProfileAsync(new V2AuthService(() => s, new Wire())));
            else
                Assert.True(await sync.PushAsync("test"));
            Dispatcher.UIThread.RunJobs();
            var saved = SeasonRecapStore.Load("2098-07");
            Assert.NotNull(saved);
            Assert.Equal(3, saved.SessionCount);
            Assert.Equal("2098-08", s.SeasonStatsSeason);
            Assert.Equal(0, s.SeasonSessionsStarted);
            Assert.False(s.SeasonResetPending);
            var tab = shell.Named<LeaderboardTabView>("LeaderboardTab")!;
            Assert.True(tab.FindControl<Border>("SeasonRecapHost")!.IsVisible);
            Assert.True(tab.FindControl<Button>("BtnViewSeasonRecap")!.IsVisible);
        }
        finally
        {
            sync.Reset();
            shell?.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
            AccountSeed.Sync = oldSync;
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    });
}
