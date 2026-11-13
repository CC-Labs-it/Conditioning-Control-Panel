using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

public sealed class WelcomeBackRestoreTests
{
    private sealed class BackupServer(AppSettings? settings, bool bodyFails) : HttpMessageHandler
    {
        public int Reads;
        public TaskCompletionSource? FirstReadStarted, ReleaseFirstRead;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Reads++;
            if (Reads == 1 && FirstReadStarted != null && ReleaseFirstRead != null)
            {
                FirstReadStarted.SetResult();
                await ReleaseFirstRead.Task;
            }
            var data = settings == null ? null : SettingsBackupBudget.Encode(Newtonsoft.Json.Linq.JObject.FromObject(settings)).Base64;
            return new HttpResponseMessage
            {
                Content = JsonContent.Create(new { backup = data == null ? null : new
                    { settings_data = bodyFails && Reads > 1 ? "bad-data" : data, backed_up_at = "2026-08-01T00:00:00Z" } })
            };
        }
    }

    internal static CloudSettingsBackup ClientFor(AppSettings settings) => new(new BackupServer(settings, false));
    /// <summary>A loopback client with an empty slot: welcome-back tests never reach the real proxy.</summary>
    internal static CloudSettingsBackup NoBackupClient() => new(new BackupServer(null, false));

    // WPF App.xaml.cs:4899-4923,4982-5000,5033-5035,5080-5118.
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public Task BackupIsPeekedButOnlyAChosenDownloadedBodyIsApplied(bool choose, bool bodyFails, bool sameVersion)
        => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var saved = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, CoreSettings.ServiceProvider,
            CoreAccount.UnifiedUserId, CoreReleaseContent.AppVersionProvider, CoreReleaseContent.PatchNotesProvider,
            WelcomeBackOffer.NewBackup, WelcomeBackOffer.Choose, WelcomeBackOffer.Present, WelcomeBackOffer.Shell);
        var dir = Directory.CreateTempSubdirectory("welcome-restore-").FullName;
        try
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
            current.PlayerXP = 123;
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
            var server = new BackupServer(backup, bodyFails);
            WelcomeBackOffer.ResetForTests();
            WelcomeBackOffer.RunModal = show => show(); // the restore rules, not the ladder (StartupModalLadderTests)
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
                Assert.Equal(123, CoreSettings.Current.PlayerXP);
                Assert.Equal("welcome-test", CoreSettings.Current.UnifiedId);
                Assert.Equal("/this-machine/spiral.gif", CoreSettings.Current.SpiralPath);
                Assert.True(CoreSettings.Current.Welcomed && CoreSettings.Current.FirstRunAssetsPromptShown);
                Assert.Equal(150, CoreSettings.Current.TotalConditioningMinutes);
            }
        }
        finally
        {
            WelcomeBackOffer.ResetForTests();
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, CoreSettings.ServiceProvider,
                CoreAccount.UnifiedUserId, CoreReleaseContent.AppVersionProvider, CoreReleaseContent.PatchNotesProvider,
                WelcomeBackOffer.NewBackup, WelcomeBackOffer.Choose, WelcomeBackOffer.Present, WelcomeBackOffer.Shell) = saved;
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public Task ConcurrentLooksPeekOnceAndShowOneSheet() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var saved = (CoreSettings.ServiceProvider, CoreAccount.UnifiedUserId, WelcomeBackOffer.NewBackup,
            WelcomeBackOffer.Choose, WelcomeBackOffer.Present, WelcomeBackOffer.Shell);
        var dir = Directory.CreateTempSubdirectory("welcome-gate-").FullName;
        var server = new BackupServer(new AppSettings(), false)
        {
            FirstReadStarted = new TaskCompletionSource(),
            ReleaseFirstRead = new TaskCompletionSource()
        };
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.UnifiedId = CoreAccount.UnifiedUserId = "welcome-test";
            WelcomeBackOffer.ResetForTests();
            WelcomeBackOffer.RunModal = show => show(); // the restore rules, not the ladder (StartupModalLadderTests)
            var sheets = 0;
            WelcomeBackOffer.NewBackup = () => new CloudSettingsBackup(server);
            WelcomeBackOffer.Choose = _ => { sheets++; return Task.FromResult((false, false)); };
            WelcomeBackOffer.Arm(true, dir);
            var first = WelcomeBackOffer.TryOfferAsync("first");
            await server.FirstReadStarted.Task;
            var second = WelcomeBackOffer.TryOfferAsync("second");
            Assert.Equal(1, server.Reads);
            server.ReleaseFirstRead.SetResult();
            await Task.WhenAll(first, second);
            Assert.Equal(2, server.Reads);
            Assert.Equal(1, sheets);
        }
        finally
        {
            server.ReleaseFirstRead.TrySetResult();
            WelcomeBackOffer.ResetForTests();
            (CoreSettings.ServiceProvider, CoreAccount.UnifiedUserId, WelcomeBackOffer.NewBackup,
                WelcomeBackOffer.Choose, WelcomeBackOffer.Present, WelcomeBackOffer.Shell) = saved;
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public Task TestMethodsRestorePriorProvidersAndAccount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var saved = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, CoreSettings.ServiceProvider,
            CoreAccount.UnifiedUserId, CoreReleaseContent.AppVersionProvider, CoreReleaseContent.PatchNotesProvider,
            CoreSession.IsSessionRunningProvider, WelcomeBackOffer.NewBackup, WelcomeBackOffer.Choose,
            WelcomeBackOffer.Present, WelcomeBackOffer.Shell);
        var inbox = StartupLadder.Inbox.Items.ToArray();
        try
        {
            CoreSecrets.RetrieveProvider = _ => "prior";
            CoreSecrets.StoreProvider = (_, _) => { };
            CoreSettings.ServiceProvider = () => null;
            CoreAccount.UnifiedUserId = "prior-user";
            CoreReleaseContent.AppVersionProvider = () => "prior-version";
            CoreReleaseContent.PatchNotesProvider = () => "prior-notes";
            CoreSession.IsSessionRunningProvider = () => false;
            WelcomeBackOffer.NewBackup = () => new CloudSettingsBackup();
            WelcomeBackOffer.Choose = _ => Task.FromResult((false, false));
            WelcomeBackOffer.Present = _ => Task.CompletedTask;
            WelcomeBackOffer.Shell = () => null;
            var expected = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, CoreSettings.ServiceProvider,
                CoreAccount.UnifiedUserId, CoreReleaseContent.AppVersionProvider, CoreReleaseContent.PatchNotesProvider,
                CoreSession.IsSessionRunningProvider, WelcomeBackOffer.NewBackup, WelcomeBackOffer.Choose,
                WelcomeBackOffer.Present, WelcomeBackOffer.Shell);
            var row = new ConditioningControlPanel.Services.Startup.InboxItem { Key = "prior-row" };
            StartupLadder.Inbox.Items.Clear();
            StartupLadder.Inbox.Items.Add(row);
            void AssertRestored()
            {
                Assert.Equal(expected, (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, CoreSettings.ServiceProvider,
                    CoreAccount.UnifiedUserId, CoreReleaseContent.AppVersionProvider, CoreReleaseContent.PatchNotesProvider,
                    CoreSession.IsSessionRunningProvider, WelcomeBackOffer.NewBackup, WelcomeBackOffer.Choose,
                    WelcomeBackOffer.Present, WelcomeBackOffer.Shell));
                Assert.Same(row, Assert.Single(StartupLadder.Inbox.Items));
            }
            await BackupIsPeekedButOnlyAChosenDownloadedBodyIsApplied(true, false, true);
            AssertRestored();
            await ConcurrentLooksPeekOnceAndShowOneSheet();
            AssertRestored();
            await RestoreFailureFilesAnInboxRetryThatDownloadsAgain(false);
            AssertRestored();
            await RestoreFailureFilesAnInboxRetryThatDownloadsAgain(true);
            AssertRestored();
        }
        finally
        {
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, CoreSettings.ServiceProvider,
                CoreAccount.UnifiedUserId, CoreReleaseContent.AppVersionProvider, CoreReleaseContent.PatchNotesProvider,
                CoreSession.IsSessionRunningProvider, WelcomeBackOffer.NewBackup, WelcomeBackOffer.Choose,
                WelcomeBackOffer.Present, WelcomeBackOffer.Shell) = saved;
            StartupLadder.Inbox.Items.Clear();
            foreach (var item in inbox) StartupLadder.Inbox.Items.Add(item);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RestoreFailureFilesAnInboxRetryThatDownloadsAgain(bool locked) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var saved = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, CoreSettings.ServiceProvider,
            CoreSession.IsSessionRunningProvider, WelcomeBackOffer.NewBackup, WelcomeBackOffer.Choose,
            WelcomeBackOffer.Present, WelcomeBackOffer.Shell);
        var savedInbox = StartupLadder.Inbox.Items.ToArray();
        try
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            CoreSecrets.RetrieveProvider = _ => null;
            CoreSecrets.StoreProvider = (_, _) => { };
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.UnifiedId = "welcome-test";
            var server = new BackupServer(null, false);
            WelcomeBackOffer.ResetForTests();
            StartupLadder.Inbox.Items.Clear();
            WelcomeBackOffer.NewBackup = () => new CloudSettingsBackup(server);
            WelcomeBackOffer.ApplyRestore(null);
            var item = Assert.Single(StartupLadder.Inbox.Items);
            Assert.Equal("welcome-back:restore-failed", item.Key);
            Assert.Equal(nameof(IconKind.Cloud), item.IconKey);
            var host = new Window { Content = new InboxFlyout() };
            try
            {
                host.Show();
                host.UpdateLayout();
                var icon = host.GetVisualDescendants().OfType<IconGlyph>().Single(g => g.Name == "InboxRowIcon");
                Assert.Equal(IconKind.Cloud, icon.Kind);
                Assert.False(icon.IsFallback);
            }
            finally { host.Close(); }
            var current = service.Current;
            CoreSession.IsSessionRunningProvider = () => locked;
            StartupLadder.Inbox.Open(item);
            await Task.Yield();
            Assert.Equal(locked ? 0 : 1, server.Reads);
            if (locked) WelcomeBackOffer.ApplyRestore(new AppSettings { FlashEnabled = !current.FlashEnabled });
            Assert.Same(current, service.Current);
            Assert.Single(StartupLadder.Inbox.Items);
        }
        finally
        {
            WelcomeBackOffer.ResetForTests();
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, CoreSettings.ServiceProvider,
                CoreSession.IsSessionRunningProvider, WelcomeBackOffer.NewBackup, WelcomeBackOffer.Choose,
                WelcomeBackOffer.Present, WelcomeBackOffer.Shell) = saved;
            StartupLadder.Inbox.Items.Clear();
            foreach (var item in savedInbox) StartupLadder.Inbox.Items.Add(item);
        }
    });
}
