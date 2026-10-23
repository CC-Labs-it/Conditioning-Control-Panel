using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class LoginAchievementOfferTests
{
    private sealed class LinkWire(bool success) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Assert.Equal("/v2/auth/link", r.RequestUri!.AbsolutePath);
            Calls++;
            return Task.FromResult(new HttpResponseMessage(success ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            { Content = new StringContent(success ? "{\"success\":true}" : "{\"error\":\"refused\"}") });
        }
    }

    private sealed class PrivacyWire : HttpMessageHandler
    {
        internal readonly System.Collections.Generic.List<Newtonsoft.Json.Linq.JObject> Bodies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Assert.Equal("/v2/user/sync", r.RequestUri!.AbsolutePath);
            Bodies.Add(Newtonsoft.Json.Linq.JObject.Parse(await r.Content!.ReadAsStringAsync(ct)));
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task DiscordLinkOffersSharingOnlyAfterASuccessfulServerLink(bool success) => Run(async (shell, s) =>
    {
        s.UnifiedId = "test-account";
        s.HasLinkedDiscord = false;
        shell.ShowTab("appsettings");
        var section = shell.AppSettingsPage!.FindControl<AccountSettingsSection>("SectionAccount")!;
        var wire = new LinkWire(success);
        AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
        var oldProbe = LoginDialog.ProxyReachable;
        var oldTell = AccountSettingsSection.Tell;
        LoginDialog.ProxyReachable = _ => Task.FromResult(true);
        AccountSettingsSection.Tell = (_, _, _) => Task.CompletedTask;
        AccountSeed.Discord!.IsVerifying = true; // cached fake token; skip the real OAuth browser
        try
        {
            var linking = section.LinkAsync("discord", section.FindControl<Button>("BtnLinkDiscord")!, "TxtBtnLinkDiscord");
            if (success)
            {
                await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().Any());
                Assert.True(s.HasLinkedDiscord);
                Assert.False(s.DiscordShareAchievements);
                Assert.Single(shell.OwnedWindows.OfType<MessageDialog>()).Close(true);
            }
            await linking;
            Assert.Equal(1, wire.Calls);
            Assert.Equal(success, s.DiscordShareAchievements);
            Assert.Equal(success, s.HasLinkedDiscord);
            Assert.True(section.FindControl<Button>("BtnLinkDiscord")!.IsEnabled);
        }
        finally
        {
            AccountSeed.Discord.IsVerifying = false;
            LoginDialog.ProxyReachable = oldProbe;
            AccountSettingsSection.Tell = oldTell;
        }
    });

    [Fact]
    public Task FirstWebClaimQueuesOneNineSecondExplanationAndPersistsTheSeenKey() => Run((shell, s) =>
    {
        s.SeenFeatureIntros.Remove("web-xp");
        var host = new StackPanel();
        App.Notifications.AttachHost(host);
        host.Children.Clear();
        App.AnnounceFirstWebXpClaim(120);
        App.AnnounceFirstWebXpClaim(200);
        Dispatcher.UIThread.RunJobs();
        var toast = Assert.IsType<Border>(Assert.Single(host.Children));
        Assert.Contains(toast.GetLogicalDescendants().OfType<TextBlock>(), t =>
            t.Text == "+120 XP from your web account - descending is account-wide now.");
        Assert.Equal(TimeSpan.FromSeconds(9), Assert.IsType<DispatcherTimer>(toast.Tag).Interval);
        Assert.Contains("web-xp", s.SeenFeatureIntros);
        Assert.True(CoreSettings.Service!.HasPendingSave);
        Assert.IsType<DispatcherTimer>(toast.Tag).Stop();
        App.Notifications.AttachHost(shell.Named<Panel>("NotificationHost") ?? new StackPanel());
        return Task.CompletedTask;
    });

    [Fact]
    public Task PrivacyControlsPersistIndependentConsentAndSendRevocation() => Run(async (shell, s) =>
    {
        var panel = shell.ProfilePage!.PrivacyPanel;
        s.UnifiedId = CoreAccount.UnifiedUserId = "test-account";
        var wire = new PrivacyWire(); var now = DateTime.UtcNow;
        var sync = AccountSeed.Sync = new SyncPush(() => Array.Empty<string>(), () => false, wire) { UtcNow = () => now };
        sync.MarkLoaded(Array.Empty<string>());
        void Toggle(string name, bool value)
        { now = now.AddSeconds(31); panel.FindControl<CheckBox>(name)!.IsChecked = value; }
        Toggle("ChkDiscordTabAllowDm", false); Toggle("ChkDiscordTabAllowDm", true);
        Toggle("ChkDiscordTabSharePfp", false); Toggle("ChkDiscordTabSharePfp", true);
        Toggle("ChkPublicShareRealAvatar", true); Toggle("ChkPublicShareRealAvatar", false);
        Toggle("ChkDiscordTabShowOnline", true); Toggle("ChkDiscordTabShowOnline", false);
        Toggle("ChkGoonShareAvatar", false); Toggle("ChkGoonShareAvatar", true);
        Toggle("ChkGoonShareDiscordDm", true); Toggle("ChkGoonShareDiscordDm", false);
        await WaitFor(() => wire.Bodies.Count > 0 && wire.Bodies.Last().Value<bool?>("goon_share_dm") == false);
        var body = wire.Bodies.Last();
        Assert.True((bool)body["allow_discord_dm"]!);
        Assert.True((bool)body["share_profile_picture"]!);
        Assert.False((bool)body["public_share_avatar"]!);
        Assert.False((bool)body["show_online_status"]!);
        Assert.True((bool)body["goon_share_avatar"]!);
        Assert.False((bool)body["goon_share_dm"]!);
        Assert.True(CoreSettings.Service!.HasPendingSave);
    });
    [Fact]
    public Task LogoutKeepsQuestProgressForSameAccountButNextAccountClearsIt() => Run(async (shell, s) =>
    {
        var oldQuests = App.Quests;
        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-login-quests-").FullName;
        using var quests = new QuestService(null, dir);
        App.Quests = quests;
        try
        {
            s.UnifiedId = CoreAccount.UnifiedUserId = "account-a";
            quests.Progress.TotalDailyQuestsCompleted = 7;
            await AccountSeed.Logout();
            Assert.Equal("account-a", quests.Progress.OwnerUnifiedId);
            Assert.Equal(7, quests.Progress.TotalDailyQuestsCompleted);
            s.UnifiedId = "account-a";
            Assert.False(await AccountSeed.LoadProfileAsync()); // fixture's failed profile, ownership still checked
            Assert.Equal(7, quests.Progress.TotalDailyQuestsCompleted);
            s.UnifiedId = "account-b";
            Assert.False(await AccountSeed.LoadProfileAsync());
            Assert.Equal(0, quests.Progress.TotalDailyQuestsCompleted);
            Assert.Equal("account-b", quests.Progress.OwnerUnifiedId);
        }
        finally { App.Quests = oldQuests; quests.Dispose(); System.IO.Directory.Delete(dir, true); }
    });

}
