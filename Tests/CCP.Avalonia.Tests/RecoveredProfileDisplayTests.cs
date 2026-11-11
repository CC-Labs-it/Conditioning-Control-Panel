using System;
using System.IO;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class RecoveredProfileDisplayTests
{
    [Fact]
    public Task RecoveryAfterShellIsVisibleRepaintsTheHeader() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        Directory.CreateDirectory(CorePaths.UserData);
        var marker = Path.Combine(CorePaths.UserData, "settings.json.restored");
        var initial = new SettingsService();
        initial.Current.UnifiedId = "display-test";
        initial.Current.AuthToken = "fake";
        initial.Current.OfflineMode = false;
        initial.Current.PlayerLevel = 10;
        initial.Current.PlayerXP = 0;
        initial.Current.CurrentSeason = "2025-01";
        initial.Current.LastConfirmedServerXp = 0;
        initial.SaveImmediate(true);
        File.WriteAllText(marker, DateTime.UtcNow.ToString("o") + "|backup|2025-01");
        var settings = new SettingsService();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, new Handler());
        sync.MarkLoaded(null);
        var shell = new MainShellWindow();
        AvApp.WireRecoveredProfileRefresh(sync, () => shell);
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var label = shell.FindControl<TextBlock>("TxtLevelLabel")!;
            var menuXp = shell.FindControl<TextBlock>("ProfileMenuXp")!;
            Assert.Equal("LVL 10", label.Text);
            Assert.StartsWith("0 /", menuXp.Text);
            Assert.True(await Task.Run(() => sync.PushAsync("recovery")));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("LVL 1", label.Text);
            Assert.StartsWith("50 /", menuXp.Text);
            Assert.Equal(1, settings.Current.PlayerLevel);
        }
        finally
        {
            sync.Reset();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = oldProvider;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    });

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Get
                    ? "{\"user\":{\"unified_id\":\"display-test\",\"level\":1,\"xp\":50,\"current_season\":\"2026-01\"}}"
                    : "{}")
            });
    }
}
