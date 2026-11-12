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
using ConditioningControlPanel.Localization;
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
    [Fact]
    public Task WatermarkRefusalsShowTitleAtThreeAndClearAfterSuccess() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldSync = AccountSeed.Sync;
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, new Wire { Succeed = true });
        AccountSeed.Sync = sync;
        var s = settings.Current;
        s.UnifiedId = "watermark-title-test";
        s.AuthToken = "fake";
        s.OfflineMode = false;
        s.PendingDescentMigrationChoice = null;
        s.LastConfirmedServerXpAccount = s.UnifiedId;
        s.LastConfirmedServerXpSeason = s.CurrentSeason;
        s.LastConfirmedServerXp = ProfileAdopt.TotalXp(s) + 1000;
        sync.MarkLoaded(null);
        var window = new MainShellWindow();
        try
        {
            Assert.False(await sync.PushAsync("watermark"));
            Assert.False(await sync.PushAsync("watermark"));
            Assert.DoesNotContain("Cloud sync issue", window.Title);
            Assert.False(await sync.PushAsync("watermark"));
            Assert.EndsWith(" - Cloud sync issue", window.Title);
            s.LastConfirmedServerXp = 0;
            Assert.True(await sync.PushAsync("watermark cleared"));
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

    [Fact]
    public void SyncIssueTitleTracksSelectedLanguage() => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = LocalizationManager.Instance;
        var original = loc.CurrentLanguage;
        var window = new MainShellWindow();
        try
        {
            window.OnSyncHealthChanged(null, 3);
            foreach (var (language, text) in new[]
            {
                ("en", "Cloud sync issue"), ("de", "Problem bei der Cloud-Synchronisierung"),
                ("es", "Problema de sincronización en la nube"), ("fr", "Problème de synchronisation cloud"),
                ("it", "Problema di sincronizzazione cloud"), ("ja", "クラウド同期の問題"),
                ("ko", "클라우드 동기화 문제"), ("pt-BR", "Problema de sincronização na nuvem"),
                ("ru", "Проблема синхронизации с облаком"), ("zh-CN", "云同步出现问题")
            })
            {
                loc.SetLanguage(language);
                Assert.EndsWith(" - " + text, window.Title);
            }
            window.OnSyncHealthChanged(null, 0);
            Assert.DoesNotContain("云同步出现问题", window.Title);
        }
        finally
        {
            window.Close();
            loc.SetLanguage(original);
        }
    });

}
