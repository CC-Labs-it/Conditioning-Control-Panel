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
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().WithInterFont().With(HeadlessFonts())
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
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

    private static FontManagerOptions HeadlessFonts()
    {
        // The shell's PremiumSpark requests Windows faces even in a Linux headless host.
        // Resolve those names to the bundled text font; no installed Windows fonts required.
        var options = AppFonts.Options(systemEmoji: false);
        options.FontFamilyMappings = new Dictionary<string, FontFamily>
        {
            ["Fredoka"] = new FontFamily(AppFonts.Fredoka),
            ["Segoe UI"] = new FontFamily(AppFonts.Inter),
            ["Segoe UI Black"] = new FontFamily(AppFonts.Inter),
        };
        return options;
    }

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
            Assert.False(Shown(section, "BtnExportData"));          // not ported: hidden, not inert

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task SharingOfferIsOwnedAndRequiresYesAndCanBeRevoked(bool accept) => RunAsync(async (shell, section, s, secrets) =>
    {
        s.DiscordShareAchievements = false;
        var oldSurfaces = PanicSurfaces.All;
        PanicSurfaces.All = Array.Empty<PanicSurfaces.Surface>();
        try
        {
            var ask = shell.OfferAchievementSharingAfterDiscordLink();
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(Loc.Get("title_discord_linked"), dialog.Title);
            Assert.Equal(Loc.Get("msg_discord_share_achievements_prompt"), Text(dialog, "TxtMessage"));
            Assert.False(s.DiscordShareAchievements);
            dialog.Close(accept);
            await ask;
            Assert.Equal(accept, s.DiscordShareAchievements);
            var privacy = shell.FindControl<ConditioningControlPanel.Avalonia.Views.Tabs.DiscordTabView>("DiscordTab")!.PrivacyPanel;
            privacy.RefreshAchievementSharing();
            var share = privacy.FindControl<CheckBox>("ChkDiscordTabShareAchievements")!;
            Assert.Equal(accept, share.IsChecked);
            share.IsChecked = !accept;
            Assert.Equal(!accept, s.DiscordShareAchievements);
            Assert.Empty(StartupLadder.Inbox.Items);
        }
        finally { PanicSurfaces.All = oldSurfaces; }
    });

    [Fact]
    public Task SharingOfferWaitsForGameAndInboxClickAsksDespiteGame() => RunAsync(async (shell, section, s, secrets) =>
    {
        s.DiscordShareAchievements = false;
        var oldSurfaces = PanicSurfaces.All;
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("fake-game", _ => { }, () => true) };
        StartupLadder.ResetForTests();
        try
        {
            await shell.OfferAchievementSharingAfterDiscordLink();
            await shell.OfferAchievementSharingAfterDiscordLink();
            var row = Assert.Single(StartupLadder.Inbox.Items);
            Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
            StartupLadder.Inbox.Open(row);
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            dialog.Close(true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(s.DiscordShareAchievements);
            await shell.OfferAchievementSharingAfterDiscordLink();
            Assert.Empty(StartupLadder.Inbox.Items);
            Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
        }
        finally { PanicSurfaces.All = oldSurfaces; StartupLadder.ResetForTests(); }
    });

}
