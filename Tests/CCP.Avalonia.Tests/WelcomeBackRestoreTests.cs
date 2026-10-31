using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WelcomeBackRestoreTests
{
    private sealed class BackupServer(string? data, bool bodyFails) : HttpMessageHandler
    {
        public int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Reads++;
            var body = bodyFails && Reads > 1 ? "bad-data" : data;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(new
                {
                    backup = data == null ? null : new { settings_data = body, backed_up_at = "2026-08-01T00:00:00Z" }
                }))
            });
        }
    }

    internal static CloudSettingsBackup ClientFor(AppSettings settings) => new(new BackupServer(Pack(settings), false));

    private static string Pack(AppSettings settings)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
            gzip.Write(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(settings)));
        return Convert.ToBase64String(output.ToArray());
    }

    // WPF App.xaml.cs:4899-4923,4982-5000,5033-5035,5080-5118.
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public Task BackupIsPeekedButOnlyAChosenDownloadedBodyIsApplied(bool choose, bool bodyFails, bool sameVersion)
        => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        CoreSecrets.RetrieveProvider = _ => null;
        CoreSecrets.StoreProvider = (_, _) => { };
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var current = service.Current;
        current.UnifiedId = CoreAccount.UnifiedUserId = "welcome-test";
        current.PlayerLevel = 24;
        current.CurrentSeason = current.LastSeasonResetSeen = "2026-10";
        current.SpiralPath = "/this-machine/spiral.gif";
        current.FlashEnabled = true;
        current.Welcomed = current.FirstRunAssetsPromptShown = true;
        current.TotalConditioningMinutes = 100;
        CoreReleaseContent.AppVersionProvider = () => "7.2.0";
        CoreReleaseContent.PatchNotesProvider = () => "Current notes";
        var backup = new AppSettings
        {
            FlashEnabled = false, PlayerLevel = 99, SpiralPath = @"C:\other.gif",
            LastSeenVersion = sameVersion ? "7.2.0" : "7.1.0", LastSeasonResetSeen = "2026-08",
            TotalConditioningMinutes = 150
        };
        var server = new BackupServer(Pack(backup), bodyFails);
        var dir = Directory.CreateTempSubdirectory("welcome-restore-").FullName;
        WelcomeBackOffer.ResetForTests();
        try
        {
            WelcomeBackOffer.NewBackup = () => new CloudSettingsBackup(server);
            WelcomeBackSheetContent? offered = null;
            WelcomeBackOffer.Choose = content =>
            {
                offered = content;
                Assert.Same(current, CoreSettings.Current);
                Assert.True(current.FlashEnabled); // peek has no effects
                return Task.FromResult((choose, false));
            };
            WelcomeBackOffer.Arm(true, dir);
            await WelcomeBackOffer.TryOfferAsync("startup");
            await WelcomeBackOffer.TryOfferAsync("profile");
            Assert.Equal(2, server.Reads); // both looks share one downloaded backup
            Assert.NotNull(offered);
            Assert.Equal(!bodyFails, offered!.Plan.ShowRestoreRow);
            Assert.Equal(!bodyFails && sameVersion ? "" : "Current notes", offered.PatchNotes);
            Assert.Equal(!bodyFails, offered.SeasonLine != null); // backup history wins over this device
            Assert.Equal(!bodyFails, offered.BackupTakenAt != null);
            var applied = choose && !bodyFails;
            Assert.Equal(!applied, CoreSettings.Current.FlashEnabled);
            if (applied)
            {
                Assert.NotSame(current, CoreSettings.Current);
                Assert.Equal("7.2.0", CoreSettings.Current.LastSeenVersion);
                Assert.Equal(24, CoreSettings.Current.PlayerLevel);
                Assert.Equal("/this-machine/spiral.gif", CoreSettings.Current.SpiralPath);
                Assert.True(CoreSettings.Current.Welcomed && CoreSettings.Current.FirstRunAssetsPromptShown);
                Assert.Equal(150, CoreSettings.Current.TotalConditioningMinutes);
            }
        }
        finally { WelcomeBackOffer.ResetForTests(); Directory.Delete(dir, true); }
    });

    [Fact]
    public Task RestoreFailureFilesAnInboxRetryThatDownloadsAgain() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        CoreSecrets.RetrieveProvider = _ => null;
        CoreSecrets.StoreProvider = (_, _) => { };
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.UnifiedId = "welcome-test";
        var server = new BackupServer(null, false);
        WelcomeBackOffer.ResetForTests();
        StartupLadder.Inbox.Items.Clear();
        try
        {
            WelcomeBackOffer.NewBackup = () => new CloudSettingsBackup(server);
            WelcomeBackOffer.ApplyRestore(null);
            var item = Assert.Single(StartupLadder.Inbox.Items);
            Assert.Equal("welcome-back:restore-failed", item.Key);
            item.Open!();
            await Task.Yield();
            Assert.Equal(1, server.Reads);
            Assert.Single(StartupLadder.Inbox.Items);
        }
        finally { WelcomeBackOffer.ResetForTests(); StartupLadder.Inbox.Items.Clear(); }
    });
}
