using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SyncHealthTitleTests
{
    [Fact]
    public Task FailuresComposeWithNavigationAndRecoveryRestoresTheCrumb() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
        {
            var fonts = AppFonts.Options();
            var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(AppFonts.Inter);
            fonts.FontFamilyMappings = mappings;
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
        var oldSync = AccountSeed.Sync;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var s = CoreSettings.Current;
        var oldId = s.UnifiedId;
        var oldToken = s.AuthToken;
        var oldOffline = s.OfflineMode;
        var oldWatermark = s.LastConfirmedServerXp;
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        var oldSettingsLanguage = s.Language;
        s.Language = "en";
        LocalizationManager.Instance.SetLanguage("en");
        var wire = new Wire();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, wire);
        var now = DateTime.UtcNow;
        sync.UtcNow = () => now;
        AccountSeed.Sync = sync;
        MainShellWindow? w = null;
        try
        {
            w = new MainShellWindow();
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.ShowTab("playsessions");
            var before = w.Title;
            CoreAccount.IsLoggedInProvider = () => true;
            s.UnifiedId = "health-test";
            s.AuthToken = "fake";
            s.OfflineMode = false;
            s.LastConfirmedServerXp = 0;
            sync.MarkLoaded(null);
            for (int i = 0; i < 3; i++)
            {
                Assert.False(await sync.PushAsync("failure"));
                Dispatcher.UIThread.RunJobs();
                if (i < 2) Assert.Equal(before, w.Title);
                now += TimeSpan.FromHours(1);
            }
            Assert.Equal(before + " - Cloud sync issue", w.Title);
            w.ApplyLanguageSelection("fr");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(" - Jouer", w.Title);
            Assert.EndsWith(" - Problème de synchronisation cloud", w.Title);
            w.OpenAppSettingsSection("data");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Données", w.Title);
            w.ApplyLanguageSelection("de");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Einstellungen", w.Title);
            Assert.Contains("Daten", w.Title);
            Assert.EndsWith(" - Problem bei der Cloud-Synchronisierung", w.Title);
            w.ApplyLanguageSelection("en");
            Dispatcher.UIThread.RunJobs();
            w.ShowTab("settings");
            var homeWithIssue = w.Title;
            Assert.EndsWith(" - Cloud sync issue", homeWithIssue);
            Assert.NotEqual(before + " - Cloud sync issue", homeWithIssue);
            wire.Status = HttpStatusCode.OK;
            Assert.True(await sync.PushAsync("recovered"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(homeWithIssue![..^" - Cloud sync issue".Length], w.Title);
            w.Close();
            var closedTitle = w.Title;
            now += TimeSpan.FromHours(1);
            wire.Status = HttpStatusCode.InternalServerError;
            for (int i = 0; i < 3; i++)
            {
                await sync.PushAsync("after close");
                now += TimeSpan.FromHours(1);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(closedTitle, w.Title);

            // A shell created after failures composes the notice without capturing it as the base.
            CoreAccount.IsLoggedInProvider = () => false;
            s.UnifiedId = null;
            s.AuthToken = null;
            var reopened = new MainShellWindow();
            try
            {
                reopened.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.EndsWith(" - Cloud sync issue", reopened.Title);
                reopened.ShowTab("play");
                Assert.EndsWith(" - Cloud sync issue", reopened.Title);
                CoreAccount.IsLoggedInProvider = () => true;
                s.UnifiedId = "health-test";
                s.AuthToken = "fake";
                wire.Status = HttpStatusCode.OK;
                Assert.True(await sync.PushAsync("recovered again"));
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain("Cloud sync issue", reopened.Title);
                Assert.StartsWith("Conditioning Control Panel", reopened.Title);
            }
            finally { reopened.Close(); }

            // The title still follows language changes when there is no cloud sync service.
            AccountSeed.Sync = null;
            CoreAccount.IsLoggedInProvider = () => false;
            s.UnifiedId = null;
            s.AuthToken = null;
            var offline = new MainShellWindow();
            try
            {
                offline.Show();
                offline.ShowTab("play");
                offline.ApplyLanguageSelection("fr");
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(" - Jouer", offline.Title);
                Assert.DoesNotContain("Cloud sync issue", offline.Title);
            }
            finally { offline.Close(); }
            var closedOfflineTitle = offline.Title;
            LocalizationManager.Instance.SetLanguage("de");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(closedOfflineTitle, offline.Title);
        }
        finally
        {
            w?.Close();
            sync.Reset();
            AccountSeed.Sync = oldSync;
            CoreAccount.IsLoggedInProvider = oldLogin;
            s.UnifiedId = oldId;
            s.AuthToken = oldToken;
            s.OfflineMode = oldOffline;
            s.LastConfirmedServerXp = oldWatermark;
            s.Language = oldSettingsLanguage;
            LocalizationManager.Instance.SetLanguage(oldLanguage);
        }
    });

    private sealed class Wire : HttpMessageHandler
    {
        internal HttpStatusCode Status = HttpStatusCode.InternalServerError;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("{}") });
    }
}
