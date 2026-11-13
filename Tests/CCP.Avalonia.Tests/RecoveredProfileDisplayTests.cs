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
        var marker = Path.Combine(CorePaths.UserData, "settings.json.restored");
        await WithPreservedRestoreMarker(marker, async () =>
        {
            var oldProvider = CoreSettings.ServiceProvider;
            var oldLogin = CoreAccount.IsLoggedInProvider;
            SyncPush? sync = null;
            MainShellWindow? shell = null;
            try
            {
                Directory.CreateDirectory(CorePaths.UserData);
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
                CoreSettings.ServiceProvider = () => settings;
                CoreAccount.IsLoggedInProvider = () => true;
                sync = new SyncPush(() => Array.Empty<string>(), () => false, new Handler());
                sync.MarkLoaded(null);
                shell = new MainShellWindow();
                AvApp.WireRecoveredProfileRefresh(sync, () => shell);
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
                try
                {
                    sync?.Reset();
                    shell?.Close();
                    Dispatcher.UIThread.RunJobs();
                }
                finally
                {
                    CoreSettings.ServiceProvider = oldProvider;
                    CoreAccount.IsLoggedInProvider = oldLogin;
                }
            }
        });
    });

    private static async Task WithPreservedRestoreMarker(string marker, Func<Task> run)
    {
        var original = File.Exists(marker) ? File.ReadAllBytes(marker) : null;
        try { await run(); }
        finally
        {
            if (original == null) { if (File.Exists(marker)) File.Delete(marker); }
            else File.WriteAllBytes(marker, original);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RestoreMarkerSurvivesRecoveryOrSetupFailure(bool existed, bool fail)
    {
        var marker = Path.Combine(CorePaths.UserData, "settings.json.restored");
        await WithPreservedRestoreMarker(marker, async () =>
        {
            Directory.CreateDirectory(CorePaths.UserData);
            byte[] original = { 0, 1, 2, 255 };
            if (existed) File.WriteAllBytes(marker, original);
            else File.Delete(marker);
            Task Run() => WithPreservedRestoreMarker(marker, () =>
            {
                File.WriteAllText(marker, "interrupted setup");
                if (fail) throw new InvalidOperationException();
                File.Delete(marker); // successful reconciliation clears the marker
                return Task.CompletedTask;
            });
            if (fail) await Assert.ThrowsAsync<InvalidOperationException>(Run);
            else await Run();
            Assert.Equal(existed, File.Exists(marker));
            if (existed) Assert.Equal(original, File.ReadAllBytes(marker));
        });
    }

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
