using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Web;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;
using IconLabel = ConditioningControlPanel.Avalonia.Controls.IconLabel;
using IconGlyph = ConditioningControlPanel.Avalonia.Controls.IconGlyph;
using IconKind = FluentIcons.Common.Symbol;
using Avalonia.Media;

namespace CCP.Avalonia.Tests;

/// <summary>SETTINGS · ACCOUNT on this head, driven from the shell the way a user reaches it:
/// fake providers (in-memory secrets, no keyring, no server), Settings door, the card buttons.</summary>
public sealed class AccountSettingsSectionTests
{
    private static void Run(Action<MainShellWindow, AccountSettingsSection, AppSettings, Dictionary<string, string?>> body) =>
        RunAsync((shell, section, s, secrets) => { body(shell, section, s, secrets); return Task.CompletedTask; }).GetAwaiter().GetResult();

    private static Task RunAsync(Func<MainShellWindow, AccountSettingsSection, AppSettings, Dictionary<string, string?>, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                // The production builder includes Inter and AppFonts registration for this headless host.
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldGet = CoreSecrets.RetrieveProvider;
            var oldSet = CoreSecrets.StoreProvider;
            var oldSettings = CoreSettings.ServiceProvider;
            var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
            var settingsBefore = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            var secrets = new Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            // No test reaches the real server for the backup status line.
            var oldBackup = AccountSettingsSection.NewBackup;
            AccountSettingsSection.NewBackup = () => new CloudSettingsBackup(new CannedBackup("{\"backup\":null}"));
            MainShellWindow? shell = null;
            try
            {
                Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => s)));
                shell = new MainShellWindow();
                shell.Show();
                shell.ShowTab("appsettings");
                Dispatcher.UIThread.RunJobs();
                var section = shell.AppSettingsPage!.FindControl<AccountSettingsSection>("SectionAccount")!;
                await body(shell, section, s, secrets);
            }
            finally
            {
                shell?.Close();
                Dispatcher.UIThread.RunJobs();
                CoreSecrets.RetrieveProvider = oldGet;
                CoreSecrets.StoreProvider = oldSet;
                CoreSettings.ServiceProvider = oldSettings;
                AccountSettingsSection.NewBackup = oldBackup;
                // P02: providers that read the process settings again, not this test's thrown-away ones.
                AccountSeed.Seed(p => new ProviderSubscription(p));
                CoreAccount.IsLoggedInProvider = CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = CoreAccount.IsWhitelistedProvider = null;
                CoreAccount.DisplayNameProvider = null;
                CoreAccount.ChangeDisplayNameProvider = null;
                CoreAccount.DeleteAccountProvider = null;
                CoreAccount.UnifiedUserId = null;
                CoreEntitlement.HasPremiumProvider = CoreEntitlement.HasLabProvider = null;
                CoreProgram.HasPremiumProvider = null;
                if (settingsBefore != null) File.WriteAllBytes(settingsPath, settingsBefore); else File.Delete(settingsPath);
            }
        });

    private sealed class CannedBackup(string body) : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            lock (Paths) Paths.Add(r.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class MergedExportServer(AppSettings settings) : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public bool ClearedAndStopped, FailProfile;
        public TaskCompletionSource? AuthSeen, Hold;   // set: the provider reply waits until the test releases it
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Paths) Paths.Add(path);
            if (path == "/v2/user/export-data")
                return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}") };
            if (path.StartsWith("/v2/auth/"))
            {
                ClearedAndStopped = settings.AuthToken == null && AccountSeed.Sync?.Loaded == false && AccountSeed.Sync?.HeartbeatRunning == false;
                AuthSeen?.TrySetResult();
                if (Hold != null) await Hold.Task;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"auth_token\":\"fresh\",\"user\":{\"unified_id\":\"u_canon123\",\"display_name\":\"Bambi\"}}") };
            }
            if (path == "/v2/user/profile" && FailProfile) return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") };
            var body = path == "/v2/user/profile" ? "{\"user\":{\"unified_id\":\"u_canon123\",\"display_name\":\"Bambi\"}}" : "{\"backup\":null}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private static string Packed(AppSettings settings)
    {
        using var output = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(settings)));
        return Convert.ToBase64String(output.ToArray());
    }

    /// <summary>Settings > Account > Restore from cloud: asked first (No sends nothing), then the backup replaces the
    /// settings with this machine's absolute paths and level kept, and the Backup now button uploads.</summary>
    [Fact]
    public Task CloudRestoreAsksFirstAndKeepsThisMachinesPaths() => RunAsync(async (shell, section, s, _) =>
    {
        s.UnifiedId = "u-test";
        s.AuthToken = "TOK";
        s.SpiralPath = "/home/me/spiral.gif";
        s.FlashEnabled = true;
        s.PlayerLevel = 5;
        var windows = new AppSettings { SpiralPath = @"C:\spiral.gif", FlashEnabled = false, PlayerLevel = 99 };
        var server = new CannedBackup(new Newtonsoft.Json.Linq.JObject
        {
            ["backup"] = new Newtonsoft.Json.Linq.JObject { ["settings_data"] = Packed(windows), ["app_version"] = "6.11.0" },
        }.ToString());
        var oldBackup = AccountSettingsSection.NewBackup;
        var oldTell = AccountSettingsSection.Tell;
        var oldConfirm = AccountSettingsSection.Confirm;
        var oldSession = CoreSession.IsSessionRunningProvider;
        var dialogs = new List<string>();
        var answer = false;
        AccountSettingsSection.NewBackup = () => new CloudSettingsBackup(server);
        AccountSettingsSection.Tell = (_, title, _) => { dialogs.Add(title); return Task.CompletedTask; };
        AccountSettingsSection.Confirm = (_, title, _) => { dialogs.Add(title); return Task.FromResult(answer); };
        try
        {
            section.Refresh();
            Assert.True(Shown(section, "CloudSettingsBackupSection"));

            await section.RestoreAsync();
            Assert.Equal(new[] { Loc.Get("title_restore_settings_from_cloud") }, dialogs);
            Assert.Same(s, CoreSettings.Current);   // No: nothing swapped

            answer = true;
            await section.RestoreAsync();
            var now = CoreSettings.Current;
            Assert.NotSame(s, now);
            Assert.Equal("/home/me/spiral.gif", now.SpiralPath);
            Assert.Equal(5, now.PlayerLevel);
            Assert.False(now.FlashEnabled);
            Assert.Equal("u-test", now.UnifiedId);
            Assert.Contains(Loc.Get("title_settings_restored"), dialogs);
            Assert.True(section.FindControl<Button>("BtnRestoreSettings")!.IsEnabled);

            await section.BackupNowAsync();
            lock (server.Paths) Assert.Contains("/v2/user/backup-settings", server.Paths);
            Assert.Contains(Loc.Get("title_backup_complete"), dialogs);

            // A section with no shell above it still refuses mid-session (PresetsTabView's `?? IsSessionRunning`).
            dialogs.Clear();
            CoreSession.IsSessionRunningProvider = () => true;
            await new AccountSettingsSection().RestoreAsync();
            Assert.Empty(dialogs);
        }
        finally
        {
            CoreSession.IsSessionRunningProvider = oldSession;
            AccountSettingsSection.NewBackup = oldBackup;
            AccountSettingsSection.Tell = oldTell;
            AccountSettingsSection.Confirm = oldConfirm;
        }
    });

    // AccountSeed.Seed is the production startup path (App.axaml.cs:665); only HTTP is faked.
    [Theory]
    [InlineData("patreon")] [InlineData("discord")]
    [InlineData("substar")] [InlineData("none")] [InlineData("none-early")]
    [InlineData("patreon-logout")] [InlineData("patreon-noprofile")]
    public Task MergedExportUsesStartupRecoveryAndPreservesQuests(string provider) => RunAsync(async (_, section, s, secrets) =>
    {
        var oldSync = AccountSeed.Sync; var oldV2 = AccountSeed.NewV2;
        var oldBackup = AccountSettingsSection.NewBackup; var oldTell = AccountSettingsSection.Tell;
        var oldSession = CoreSession.IsSessionRunningProvider; var oldQuests = global::ConditioningControlPanel.Avalonia.App.Quests; var oldTour = CoreTutorial.IsActiveProvider; var oldModal = MainShellWindow.IsStartupDialogShowing;
        using var quests = new QuestService(null, CorePaths.UserData);
        var server = new MergedExportServer(s); var sync = new SyncPush(() => Array.Empty<string>(), () => false, server);
        var dialogs = new List<string>();
        MergedAccountRecovery.ResetForTest(); StartupLadder.ResetForTests();
        try
        {
            var mode = provider.Split('-').Skip(1).FirstOrDefault(); provider = provider.Split('-')[0];
            if (mode == "logout") (server.AuthSeen, server.Hold) = (new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously));
            server.FailProfile = mode == "noprofile";
            if (provider != "none") secrets[provider + "_auth"] = JsonConvert.SerializeObject(new PatreonTokenData { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(1) });
            s.UnifiedId = CoreAccount.UnifiedUserId = "u_tombstone1"; s.AuthToken = "old"; s.PlayerLevel = 9;
            quests.StampOwner(s.UnifiedId); global::ConditioningControlPanel.Avalonia.App.Quests = quests;
            sync.MarkLoaded(Array.Empty<string>()); sync.StartHeartbeat(); AccountSeed.Sync = sync;
            AccountSeed.NewV2 = () => new V2AuthService(() => s, server);
            AccountSettingsSection.NewBackup = () => new CloudSettingsBackup(server);
            AccountSettingsSection.Tell = (_, _, body) => { dialogs.Add(body); return Task.CompletedTask; };
            CoreSession.IsSessionRunningProvider = () => mode != "early"; CoreTutorial.IsActiveProvider = () => false; MainShellWindow.IsStartupDialogShowing = false;
            if (mode == "early") Assert.False(StartupLadder.IsQuiet);
            await section.ExportDataAsync();
            if (mode == "logout")
            {
                // The provider answers only after the user signed out: nobody is signed back in.
                await server.AuthSeen!.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await AccountSeed.Logout();
                server.Hold!.SetResult();
                await MergedAccountRecovery.LastSwap.WaitAsync(TimeSpan.FromSeconds(10)); Dispatcher.UIThread.RunJobs();
                Assert.Null(s.UnifiedId); Assert.Null(s.AuthToken); Assert.Null(CoreAccount.UnifiedUserId);
                Assert.DoesNotContain("/v2/user/profile", server.Paths);
                Assert.False(sync.Loaded); Assert.False(sync.HeartbeatRunning);
                Assert.DoesNotContain(StartupLadder.Inbox.Items, item => item.Key == "merged-account-signin:u_canon123");
                return;
            }
            await MergedAccountRecovery.LastSwap.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(Loc.Get("account_merged_retry_hint"), dialogs);
            Assert.Equal("u_canon123", s.UnifiedId); Assert.Equal("u_canon123", CoreAccount.UnifiedUserId);
            Assert.Equal("u_canon123", quests.Progress.OwnerUnifiedId); Assert.Equal(9, s.PlayerLevel);
            if (provider != "none")
            {
                Assert.Contains("/v2/auth/" + provider, server.Paths);
                Assert.True(server.ClearedAndStopped); Assert.Equal("fresh", s.AuthToken);
                // A failed read is retried once; pushes stay off, the heartbeat restarts as WPF.
                lock (server.Paths) Assert.Equal(mode == "noprofile" ? 2 : 1, server.Paths.Count(p => p == "/v2/user/profile"));
                Assert.Equal(mode != "noprofile", sync.Loaded);
                Assert.True(sync.HeartbeatRunning);
            }
            else
            {
                Assert.Null(s.AuthToken);
                Assert.False(sync.Loaded); Assert.False(sync.HeartbeatRunning);
                Assert.Contains(StartupLadder.Inbox.Items, item => item.Key == "merged-account-signin:u_canon123");
            }
        }
        finally
        {
            sync.Reset(); AccountSeed.Sync = oldSync; AccountSeed.NewV2 = oldV2;
            AccountSettingsSection.NewBackup = oldBackup; AccountSettingsSection.Tell = oldTell;
            CoreSession.IsSessionRunningProvider = oldSession; CoreTutorial.IsActiveProvider = oldTour; MainShellWindow.IsStartupDialogShowing = oldModal;
            global::ConditioningControlPanel.Avalonia.App.Quests = oldQuests;
            MergedAccountRecovery.ResetForTest(); StartupLadder.ResetForTests();
        }
    });

    [Fact]
    public Task ExportButtonFetchesThenSavesAndRestoresItsLabel() => RunAsync(async (shell, section, s, _) =>
    {
        s.UnifiedId = "u-test";
        s.AuthToken = "TOK";
        var server = new CannedBackup("{\"data\":{\"name\":\"Ada\"}}");
        var language = LocalizationManager.Instance.CurrentLanguage;
        var (oldBackup, oldPick, oldTell) = (AccountSettingsSection.NewBackup, AccountSettingsSection.PickExport, AccountSettingsSection.Tell);
        var picked = new TaskCompletionSource<string?>();
        var told = new TaskCompletionSource<(string Title, string Body)>();
        var path = Path.Combine(CorePaths.UserData, "export-test.json");
        FilePickerSaveOptions? options = null;
        AccountSettingsSection.NewBackup = () => new CloudSettingsBackup(server);
        AccountSettingsSection.PickExport = (owner, o) => { Assert.Same(shell, owner); options = o; return picked.Task; };
        AccountSettingsSection.Tell = (_, title, body) => { Dispatcher.UIThread.Post(() => told.TrySetResult((title, body))); return Task.CompletedTask; };
        try
        {
            LocalizationManager.Instance.SetLanguage("es");
            section.Refresh();
            Click(section, "BtnExportData");
            Assert.False(section.FindControl<Button>("BtnExportData")!.IsEnabled);
            Assert.Equal(Loc.Get("btn_exporting"), Text(section, "TxtBtnExportData"));
            Assert.Contains("/v2/user/export-data", server.Paths);
            Assert.NotNull(options);
            Assert.Equal(Loc.Get("title_save_data_export"), options!.Title);
            Assert.Equal($"my-data-export-{DateTime.Now:yyyy-MM-dd}.json", options.SuggestedFileName);
            var fileType = Assert.Single(options.FileTypeChoices!);
            Assert.Equal("Archivos JSON", fileType.Name);
            Assert.Contains("*.json", fileType.Patterns!);
            picked.SetResult(path);
            var dialog = await told.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Loc.Get("title_export_complete"), dialog.Title);
            Assert.Contains(path, dialog.Body);
            Assert.Equal(Newtonsoft.Json.Linq.JToken.Parse("{\"data\":{\"name\":\"Ada\"}}").ToString(), File.ReadAllText(path));
            Assert.True(section.FindControl<Button>("BtnExportData")!.IsEnabled);
            Assert.Equal(Loc.Get("btn_export_my_data"), Text(section, "TxtBtnExportData"));
        }
        finally
        {
            File.Delete(path);
            (AccountSettingsSection.NewBackup, AccountSettingsSection.PickExport, AccountSettingsSection.Tell) = (oldBackup, oldPick, oldTell);
            LocalizationManager.Instance.SetLanguage(language);
        }
    });

    [Fact]
    public Task ExportCancellationAndFailureDoNotSave() => RunAsync(async (_, section, s, _) =>
    {
        s.UnifiedId = "u-test";
        var (oldBackup, oldPick, oldTell) = (AccountSettingsSection.NewBackup, AccountSettingsSection.PickExport, AccountSettingsSection.Tell);
        var picks = 0;
        var dialogs = new List<string>();
        AccountSettingsSection.NewBackup = () => new CloudSettingsBackup(new CannedBackup("{}"));
        AccountSettingsSection.PickExport = (_, _) => { picks++; return Task.FromResult<string?>(null); };
        AccountSettingsSection.Tell = (_, title, _) => { dialogs.Add(title); return Task.CompletedTask; };
        try
        {
            await section.ExportDataAsync();
            Assert.Equal(1, picks);
            Assert.Empty(dialogs);
            // Saving to a directory fails after the picker, and reports the write error.
            AccountSettingsSection.PickExport = (_, _) => Task.FromResult<string?>(CorePaths.UserData);
            await section.ExportDataAsync();
            Assert.Equal(Loc.Get("title_export_error"), Assert.Single(dialogs));
            dialogs.Clear();
            s.UnifiedId = null;
            await section.ExportDataAsync();
            Assert.Equal(1, picks);
            Assert.Equal(Loc.Get("title_export_failed"), Assert.Single(dialogs));
            Assert.True(section.FindControl<Button>("BtnExportData")!.IsEnabled);
            Assert.Equal(Loc.Get("btn_export_my_data"), Text(section, "TxtBtnExportData"));
        }
        finally
        {
            (AccountSettingsSection.NewBackup, AccountSettingsSection.PickExport, AccountSettingsSection.Tell) = (oldBackup, oldPick, oldTell);
        }
    });

    private static string Text(Control c, string name) => c.FindControl<TextBlock>(name)!.Text ?? "";
    private static bool Shown(Control c, string name) => c.FindControl<Control>(name)!.IsVisible;
    private static void Click(Control c, string name)
    {
        c.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void SignInPatreon(Dictionary<string, string?> secrets, AppSettings s)
    {
        secrets["patreon_auth"] = JsonConvert.SerializeObject(new PatreonTokenData
        { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(10) });
        secrets["patreon_cache"] = JsonConvert.SerializeObject(new PatreonCachedState
        { Tier = PatreonTier.Level2, IsActive = true, CacheExpiresAt = DateTime.UtcNow.AddHours(1), DisplayName = "Bambi" });
        s.UnifiedId = "u-test";
        s.UserDisplayName = "Bambi";
        s.HasLinkedPatreon = true;
    }

    /// <summary>The tier badge's colour is its meaning (WPF 🧪 violet / 🔒 gold): the Fluent icon must
    /// keep it through the shared semantic brushes (fluent-icons 2026-10-10).</summary>
    [Fact]
    public void TierBadgeKeepsTheTierColourAsAFluentIcon()
    {
        Run((shell, section, s, secrets) =>
        {
            var badge = section.FindControl<IconGlyph>("TxtAccountTierBadge")!;
            Color Res(string key) => ((ISolidColorBrush)section.FindResource(key)!).Color;
            Color Ink() => Assert.IsAssignableFrom<ISolidColorBrush>(badge.Foreground).Color;
            CoreAccount.IsLoggedInProvider = () => true;

            CoreAccount.HasLabAccessProvider = () => true;
            section.RefreshTierBadge();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.Beaker, badge.Kind);
            Assert.Equal(Res("IconTier2Brush"), Ink());

            CoreAccount.HasLabAccessProvider = () => false;
            CoreAccount.HasPremiumAccessProvider = () => true;
            section.RefreshTierBadge();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.LockClosed, badge.Kind);
            Assert.Equal(Res("IconTier1Brush"), Ink());
            Assert.NotEqual(Res("IconTier1Brush"), Res("IconTier2Brush"));
        });
    }

    /// <summary>The local sandbox whitelist (AccountSeed.SandboxWhitelistRequested) names itself on the tier card in a
    /// DEBUG build; Release compiles it out, so the signed-out card stays.</summary>
    [Fact]
    public void SandboxWhitelistShowsOnTheTierCardOnlyInDebug()
    {
        var old = Environment.GetEnvironmentVariable("CCP_SANDBOX_WHITELIST");
        Environment.SetEnvironmentVariable("CCP_SANDBOX_WHITELIST", "1");   // read by Run's Seed
        try
        {
            Run((shell, section, s, secrets) =>
            {
                Environment.SetEnvironmentVariable("CCP_SANDBOX_WHITELIST", old); // before Run's cleanup re-seeds
                section.OnSectionShown();
#if DEBUG
                Assert.Equal("Whitelisted (sandbox)", Text(section, "TxtAccountSectionTier"));
#else
                Assert.Equal(Loc.Get("label_login_to_unlock_exclusive_features"), Text(section, "TxtAccountSectionTier"));
#endif
            });
        }
        finally { Environment.SetEnvironmentVariable("CCP_SANDBOX_WHITELIST", old); }
    }

    [Fact]
    public void SignedInPatronSeesHerCardsAndTheProviderButtonSignsHerOut()
    {
        Run((shell, section, s, secrets) =>
        {
            SignInPatreon(secrets, s);
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => s)));
            shell.ShowTab("dashboard");
            shell.ShowTab("appsettings");          // the door repaints the section (WPF IsVisibleChanged)
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Loc.GetF("label_welcome", "Bambi"), Text(section, "TxtPatreonStatus"));
            Assert.Equal(Loc.Get("label_patreon_tier_level2"), Text(section, "TxtPatreonTier"));
            Assert.Equal(Loc.Get("btn_logout"), Text(section, "TxtBtnPatreonLogin"));
            Assert.Equal(Loc.Get("btn_link_discord"), Text(section, "TxtBtnDiscordLogin"));
            Assert.Equal(Loc.Get("btn_login"), Text(section, "TxtBtnSubscribeStarLogin"));
            Assert.True(Shown(section, "AccountLinkingSection"));   // Discord not linked yet
            Assert.True(Shown(section, "BtnLinkDiscord"));
            Assert.False(Shown(section, "BtnLinkPatreon"));         // the grant works
            Assert.True(Shown(section, "DataPrivacySection"));
            Assert.True(Shown(section, "BtnExportData"));           // available inside the signed-in privacy section

            Click(section, "BtnPatreonLogin");     // the only provider: the whole account goes

            Assert.Null(s.UnifiedId);
            Assert.False(AccountSeed.Patreon!.IsAuthenticated);
            Assert.Equal(Loc.Get("label_not_connected"), Text(section, "TxtPatreonStatus"));
            Assert.Equal(Loc.Get("btn_login"), Text(section, "TxtBtnPatreonLogin"));
            Assert.Equal(Loc.Get("btn_login"), Text(section, "TxtBtnDiscordLogin"));
            Assert.False(Shown(section, "AccountLinkingSection"));
            Assert.False(Shown(section, "DataPrivacySection"));
        });
    }

    [Fact]
    public void APatronWithADeadGrantIsOfferedReconnect()
    {
        Run((shell, section, s, secrets) =>
        {
            // Linked server-side, no token on this PC, premium off because of it (PatreonReconnectRule).
            s.UnifiedId = "u-test";
            s.HasLinkedPatreon = true;
            s.HasLinkedDiscord = true;
            section.OnSectionShown();

            Assert.True(Shown(section, "AccountLinkingSection"));
            Assert.True(Shown(section, "BtnLinkPatreon"));
            Assert.Equal("btn_reconnect_patreon", section.FindControl<IconLabel>("TxtBtnLinkPatreon")!.Key);
            Assert.True(Shown(section, "TxtPatreonReconnectHint"));
            Assert.False(Shown(section, "BtnLinkDiscord"));
        });
    }

    [Fact]
    public void AFailedDiscordLinkGivesTheButtonItsLabelBack()
    {
        Run((shell, section, s, secrets) =>
        {
            s.UnifiedId = "u-test";
            s.HasLinkedDiscord = false;
            section.OnSectionShown();
            var discord = AccountSeed.Discord!;
            discord.IsVerifying = true;            // SignInAsync returns at once: no token, the link fails
            var oldProbe = LoginDialog.ProxyReachable;
            LoginDialog.ProxyReachable = _ => Task.FromResult(true); // reachable, so the link runs inline
            try
            {
                Click(section, "BtnLinkDiscord");
                Assert.True(section.FindControl<Button>("BtnLinkDiscord")!.IsEnabled);
                Assert.Equal("btn_link_discord", section.FindControl<IconLabel>("TxtBtnLinkDiscord")!.Key);
            }
            finally { discord.IsVerifying = false; LoginDialog.ProxyReachable = oldProbe; }
        });
    }

    /// <summary>The privacy panel's Discord button goes through this section's real provider flow (no fake action).</summary>
    [Fact]
    public void PrivacyPanelDiscordButtonRunsTheSectionLinkFlow()
    {
        Run((shell, section, s, secrets) =>
        {
            s.UnifiedId = "u-test";
            s.HasLinkedDiscord = false;
            s.OfflineMode = false;
            var discord = AccountSeed.Discord!;
            discord.IsVerifying = true;            // SignInAsync returns at once: no token, the link fails
            var oldProbe = LoginDialog.ProxyReachable;
            int probes = 0;
            LoginDialog.ProxyReachable = _ => { probes++; return Task.FromResult(true); };
            var panel = new global::ConditioningControlPanel.Avalonia.Views.Controls.ProfilePrivacyPanel();
            var dialog = new global::ConditioningControlPanel.Avalonia.Views.Dialogs.ProfilePrivacyDialog(panel);
            try
            {
                dialog.Show(shell);
                var login = panel.LoginAsync();
                Dispatcher.UIThread.RunJobs();
                Assert.True(login.IsCompleted);
                Assert.True(probes > 0);           // the section's LinkAsync ran
                Assert.Equal("btn_link_discord", section.FindControl<IconLabel>("TxtBtnLinkDiscord")!.Key);
            }
            finally { discord.IsVerifying = false; LoginDialog.ProxyReachable = oldProbe; dialog.Close(); }
        });
    }

    [Fact]
    public void PresenceSwitchAndChasterRowWork()
    {
        Run((shell, section, s, secrets) =>
        {
            var oldHead = FriendsHead.Service;
            var oldChaster = ChasterHead.Service;
            var oldMark = PresenceAsk.MarkAsked;
            var asked = false;
            PresenceAsk.MarkAsked = () => asked = true;
            try
            {
                FriendsHead.Service = null;
                ChasterHead.Service = null;
                s.FriendsPresenceShared = false;
                section.OnSectionShown();
                var chk = section.FindControl<CheckBox>("ChkFriendsPresence")!;
                Assert.False(chk.IsChecked);
                chk.IsChecked = true;                   // no friends service: the setting carries it
                Assert.True(s.FriendsPresenceShared);
                Assert.True(asked);

                Assert.Equal(Loc.Get("chaster_account_name") + " · " + Loc.Get("label_not_connected"), Text(section, "TxtChasterStatus"));
                Assert.Equal(Loc.Get("chaster_account_hint"), Text(section, "TxtChasterInfo"));
                Assert.Equal(Loc.Get("chaster_link"), Text(section, "TxtBtnChasterLink"));
                Assert.False(section.FindControl<Button>("BtnChasterLink")!.IsEnabled); // no service, no link
                Click(section, "BtnChasterOpen");
                Assert.Equal("chaster", shell.CurrentTab);
            }
            finally
            {
                FriendsHead.Service = oldHead;
                ChasterHead.Service = oldChaster;
                PresenceAsk.MarkAsked = oldMark;
            }
        });
    }
    private sealed class LinkServer(bool success) : HttpMessageHandler
    {
        internal readonly List<string> Paths = new();
        internal string? LinkBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            var json = path switch
            {
                "/discord/token" => "{\"access_token\":\"fake-discord-token\",\"refresh_token\":\"fake-refresh\",\"expires_in\":3600}",
                "/discord/validate" => "{\"id\":\"discord-test\",\"username\":\"Bambi\",\"unified_id\":\"u-test\"}",
                "/v2/auth/link" when success => "{\"success\":true,\"auth_token\":\"fake-rotated-token\"}",
                "/v2/auth/link" => "{\"error\":\"different account\"}",
                _ => throw new InvalidOperationException("Unexpected fake request " + path),
            };
            if (path == "/v2/auth/link") LinkBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(path == "/v2/auth/link" && !success ? HttpStatusCode.Conflict : HttpStatusCode.OK)
            { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task DiscordLinkRoundTripOnlyOffersSharingAfterSuccessfulLink(bool success) => RunAsync(async (shell, section, s, secrets) =>
    {
        s.UnifiedId = "u-test";
        s.HasLinkedDiscord = false;
        s.DiscordShareAchievements = false;
        using var server = new LinkServer(success);
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s), p => new DiscordAccount(() => p, () => s, server));
        var oldV2 = AccountSeed.NewV2;
        var oldProbe = LoginDialog.ProxyReachable;
        var oldTell = AccountSettingsSection.Tell;
        var dialogs = new List<string>();
        Task? callback = null;
        StartupLadder.ResetForTests();
        AccountSeed.NewV2 = () => new V2AuthService(() => s, server);
        LoginDialog.ProxyReachable = _ => Task.FromResult(true);
        AccountSettingsSection.Tell = (_, title, _) => { dialogs.Add(title); return Task.CompletedTask; };
        section.OpenProviderForTest = url =>
        {
            var query = HttpUtility.ParseQueryString(new Uri(url).Query);
            callback = Task.Run(async () =>
            {
                using var http = new HttpClient();
                var reply = await http.GetAsync(query["redirect_uri"] + "?code=fake-code&state=" + Uri.EscapeDataString(query["state"]!));
                Assert.True(reply.IsSuccessStatusCode);
            });
        };
        try
        {
            shell.Hide(); // OAuth may resolve minutes later with the panel in the tray.
            await section.LinkAsync("discord", section.FindControl<Button>("BtnLinkDiscord")!, "TxtBtnLinkDiscord").WaitAsync(TimeSpan.FromSeconds(15));
            await callback!;
            Assert.Equal(new[] { "/discord/token", "/discord/validate", "/v2/auth/link" }, server.Paths);
            Assert.Contains("fake-discord-token", server.LinkBody);
            Assert.Equal(success, s.HasLinkedDiscord);
            Assert.False(s.DiscordShareAchievements); // an offer is never consent
            Assert.True(section.FindControl<Button>("BtnLinkDiscord")!.IsEnabled);
            if (success)
            {
                Assert.Equal("fake-rotated-token", s.AuthToken);
                Assert.Equal("discord-share-achievements", Assert.Single(StartupLadder.Inbox.Items).Key);
                Assert.Contains(Loc.Get("account_linked_title"), dialogs);
            }
            else
            {
                Assert.Empty(StartupLadder.Inbox.Items);
                Assert.Contains(Loc.Get("account_link_failed_title"), dialogs);
            }
        }
        finally
        {
            AccountSeed.NewV2 = oldV2;
            LoginDialog.ProxyReachable = oldProbe;
            AccountSettingsSection.Tell = oldTell;
            StartupLadder.ResetForTests();
        }
    });

}
