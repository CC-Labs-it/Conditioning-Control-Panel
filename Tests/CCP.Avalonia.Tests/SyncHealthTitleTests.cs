using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SyncHealthTitleTests
{
    private sealed class Wire : HttpMessageHandler
    {
        public bool Succeed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(Succeed ? HttpStatusCode.OK : HttpStatusCode.InternalServerError) { Content = new StringContent("{}") });
    }

    [Fact]
    public Task HealthSuffixSurvivesNavigationAndClearsOnSuccess() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldSync = AccountSeed.Sync;
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var wire = new Wire();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, wire);
        AccountSeed.Sync = sync;
        var now = DateTime.UtcNow;
        sync.UtcNow = () => now;
        settings.Current.UnifiedId = "health-title-test";
        settings.Current.AuthToken = "fake";
        settings.Current.OfflineMode = false;
        settings.Current.LastConfirmedServerXp = 0;
        sync.MarkLoaded(null);
        var window = new MainShellWindow();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.False(await sync.PushAsync("failure"));
                now += TimeSpan.FromMinutes(20);
            }
            Assert.EndsWith(" - Cloud sync issue", window.Title);
            window.UpdateNavTitle(null, "settings", null);
            Assert.EndsWith(" - Cloud sync issue", window.Title);
            wire.Succeed = true;
            Assert.True(await sync.PushAsync("success"));
            Assert.DoesNotContain("Cloud sync issue", window.Title);
        }
        finally
        {
            window.Close();
            sync.Reset();
            AccountSeed.Sync = oldSync;
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    });
}
