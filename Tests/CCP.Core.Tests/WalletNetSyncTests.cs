using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public sealed class WalletNetSyncTests
{
    private const string A = "wallet-a";
    private static AppSettings Wallet(int points = 100, int? net = -100) => new()
    {
        UnifiedId = A, SkillPoints = points,
        WalletNetSeen = net is { } n ? new Dictionary<string, int> { [A] = n } : new()
    };
    private static bool Apply(AppSettings s, string json, string account = A) =>
        SyncPush.ApplySyncWallet(s, account, JObject.Parse(json));

    [Fact]
    public void SpendAdoptsAndNotifiesWhileReplacingTheAccountNet()
    {
        var s = Wallet();
        s.WalletNetSeen["other"] = -7;
        var before = s.WalletNetSeen;
        int notices = 0;
        s.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.SkillPoints)) notices++; };
        Assert.True(Apply(s, "{\"skill_points\":70,\"wallet_net\":-130}"));
        Assert.Equal(70, s.SkillPoints);
        Assert.Equal(-130, s.WalletNetSeen[A]);
        Assert.Equal(-7, s.WalletNetSeen["other"]);
        Assert.NotSame(before, s.WalletNetSeen);
        Assert.Equal(-100, before[A]);
        Assert.Equal(1, notices);
        Assert.False(Apply(s, "{\"skill_points\":70,\"wallet_net\":-130}"));
        Assert.Equal(1, notices);
    }

    [Fact]
    public void MissingFirstEqualAndRisingNetRemainRaiseOnly()
    {
        var s = Wallet(net: null);
        Assert.True(Apply(s, "{\"skill_points\":70,\"wallet_net\":-300}"));
        Assert.Equal(100, s.SkillPoints);
        Assert.Equal(-300, s.WalletNetSeen[A]);
        Assert.False(Apply(s, "{\"skill_points\":50}"));
        Assert.Equal(-300, s.WalletNetSeen[A]);
        Assert.False(Apply(s, "{\"skill_points\":60,\"wallet_net\":-300}"));
        Assert.True(Apply(s, "{\"skill_points\":50,\"wallet_net\":-200}"));
        Assert.Equal(100, s.SkillPoints);
        Assert.True(Apply(s, "{\"skill_points\":150,\"wallet_net\":-200}"));
        Assert.Equal(150, s.SkillPoints);
    }

    [Fact]
    public void InvalidBalanceDoesNotConsumeDropAndBadNetIsMissing()
    {
        var s = Wallet();
        foreach (var json in new[] { "{\"wallet_net\":-130}", "{\"skill_points\":-1,\"wallet_net\":-130}",
                     "{\"skill_points\":{},\"wallet_net\":-130}", "{\"skill_points\":999999999999999999999,\"wallet_net\":-130}" })
            Assert.False(Apply(s, json));
        Assert.Equal(-100, s.WalletNetSeen[A]);
        Assert.True(Apply(s, "{\"skill_points\":120,\"wallet_net\":999999999999999999999}"));
        Assert.Equal(120, s.SkillPoints);
        Assert.Equal(-100, s.WalletNetSeen[A]);
        Assert.True(Apply(s, "{\"skill_points\":70,\"wallet_net\":-130}"));
        Assert.Equal(70, s.SkillPoints);
    }

    [Fact]
    public void SpendClampsAndTheFirstNetForAnotherAccountIsIndependent()
    {
        var s = Wallet();
        s.UnifiedId = "wallet-b";
        Assert.True(Apply(s, "{\"skill_points\":70,\"wallet_net\":-200}", "wallet-b"));
        Assert.Equal(100, s.SkillPoints);
        Assert.Equal(-100, s.WalletNetSeen[A]);
        Assert.Equal(-200, s.WalletNetSeen["wallet-b"]);
        Assert.True(Apply(s, $"{{\"skill_points\":{SparklePoints.Cap + 5},\"wallet_net\":-201}}", "wallet-b"));
        Assert.Equal(SparklePoints.Cap, s.SkillPoints);
    }

    [Fact]
    public void AccountAndResetGuardsLeaveTheWalletAndNetAlone()
    {
        var s = Wallet();
        Assert.False(Apply(s, "{\"skill_points\":500,\"wallet_net\":-130}", "other"));
        s.UnlockedSkills = new() { "skill" };
        Assert.True(Apply(s, "{\"skill_points\":100,\"wallet_net\":-130,\"force_skills_reset\":true}"));
        Assert.Empty(s.UnlockedSkills);
        Assert.True(s.PendingSkillsResetAck);
        Assert.True(Apply(s, "{\"skill_points\":70,\"wallet_net\":-130}"));
        Assert.False(s.PendingSkillsResetAck);
        Assert.Equal(100, s.SkillPoints);
        Assert.Equal(-100, s.WalletNetSeen[A]);
        Assert.True(Apply(s, "{\"skill_points\":70,\"wallet_net\":-130}"));
        Assert.Equal(70, s.SkillPoints);
    }

    [Fact]
    public void ResetFallbackAndAcknowledgementSurviveUntilServerClearsFlag()
    {
        var s = Wallet();
        s.PlayerLevel = 25;
        s.UnlockedSkills = new() { "skill" };
        Assert.Null(JObject.FromObject(SyncPush.Body(s, null))["force_skills_reset"]);
        Assert.True(Apply(s, "{\"force_skills_reset\":true}"));
        Assert.Equal(25, s.SkillPoints);
        Assert.Empty(s.UnlockedSkills);
        Assert.True(s.PendingSkillsResetAck);
        var reloaded = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
        Assert.True(reloaded.PendingSkillsResetAck);
        Assert.False(JObject.FromObject(SyncPush.Body(reloaded, null)).Value<bool>("force_skills_reset"));
        var body = JObject.FromObject(SyncPush.Body(s, null));
        Assert.Equal(JTokenType.Boolean, body["force_skills_reset"]!.Type);
        Assert.False(body.Value<bool>("force_skills_reset"));
        s.SkillPoints = 60;
        Assert.False(Apply(s, "{\"force_skills_reset\":true,\"skill_points\":60,\"wallet_net\":-100}"));
        Assert.True(s.PendingSkillsResetAck);
        Assert.True(Apply(s, "{\"force_skills_reset\":false,\"skill_points\":50,\"wallet_net\":-130}"));
        Assert.False(s.PendingSkillsResetAck);
        Assert.Equal(60, s.SkillPoints);
        Assert.True(Apply(s, "{\"skill_points\":50,\"wallet_net\":-130}"));
        Assert.Equal(50, s.SkillPoints);
        Assert.Equal(-130, s.WalletNetSeen[A]);
        Assert.Null(JObject.FromObject(SyncPush.Body(s, null))["force_skills_reset"]);
    }

    [Fact]
    public async Task RealSyncUsesTopLevelFieldsAndLiveSettingsAfterReplacement()
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var first = new SettingsService();
        var replacement = new SettingsService();
        CoreSettings.ServiceProvider = () => first;
        CoreAccount.IsLoggedInProvider = () => true;
        var original = first.Current;
        original.UnifiedId = A;
        original.OfflineMode = false;
        original.LastConfirmedServerXp = 0;
        original.SkillPoints = 100;
        original.WalletNetSeen = new() { [A] = -100 };
        var live = replacement.Current;
        live.UnifiedId = A;
        live.SkillPoints = 100;
        live.WalletNetSeen = new() { [A] = -100 };
        var handler = new HeldReply();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler);
        sync.MarkLoaded(null);
        try
        {
            var pending = sync.PushAsync("wallet test");
            await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            CoreSettings.ServiceProvider = () => replacement;
            handler.Release.TrySetResult(true);
            Assert.True(await pending);
            Assert.Equal(100, original.SkillPoints);
            Assert.Equal(70, live.SkillPoints);
            Assert.Equal(-130, live.WalletNetSeen[A]);
        }
        finally
        {
            handler.Release.TrySetResult(true);
            sync.Reset();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = oldProvider;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

    [Fact]
    public async Task ReplyForOldAccountCannotChangeReplacementWallet()
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var first = new SettingsService();
        var replacement = new SettingsService();
        CoreSettings.ServiceProvider = () => first;
        CoreAccount.IsLoggedInProvider = () => true;
        first.Current.UnifiedId = A;
        first.Current.OfflineMode = false;
        first.Current.LastConfirmedServerXp = 0;
        replacement.Current.UnifiedId = "wallet-b";
        replacement.Current.SkillPoints = 200;
        replacement.Current.WalletNetSeen = new() { [A] = -100, ["wallet-b"] = -10 };
        var handler = new HeldReply();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler);
        sync.MarkLoaded(null);
        try
        {
            var pending = sync.PushAsync("wallet account swap");
            await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            CoreSettings.ServiceProvider = () => replacement;
            handler.Release.TrySetResult(true);
            await pending;
            Assert.Equal(200, replacement.Current.SkillPoints);
            Assert.Equal(-100, replacement.Current.WalletNetSeen[A]);
            Assert.Equal(-10, replacement.Current.WalletNetSeen["wallet-b"]);
        }
        finally
        {
            handler.Release.TrySetResult(true);
            sync.Reset();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = oldProvider;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

    [Fact]
    public async Task ResetReplyIsAcknowledgedOnNextPushAndWalletRecovers()
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var s = settings.Current;
        s.UnifiedId = A; s.OfflineMode = false; s.LastConfirmedServerXp = 0;
        s.SkillPoints = 100; s.UnlockedSkills = new() { "skill" }; s.WalletNetSeen = new() { [A] = -100 };
        var handler = new ResetReplies();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler);
        var now = DateTime.UtcNow;
        sync.UtcNow = () => now;
        sync.MarkLoaded(null);
        try
        {
            Assert.True(await sync.PushAsync("reset"));
            Assert.Empty(s.UnlockedSkills);
            Assert.Equal(80, s.SkillPoints);
            Assert.True(s.PendingSkillsResetAck);
            Assert.Null(handler.Bodies[0]["force_skills_reset"]);
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.True(await sync.PushAsync("ack"));
            Assert.Equal(JTokenType.Boolean, handler.Bodies[1]["force_skills_reset"]!.Type);
            Assert.False(handler.Bodies[1].Value<bool>("force_skills_reset"));
            Assert.False(s.PendingSkillsResetAck);
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.True(await sync.PushAsync("wallet"));
            Assert.Null(handler.Bodies[2]["force_skills_reset"]);
            Assert.Equal(50, s.SkillPoints);
            Assert.Equal(-130, s.WalletNetSeen[A]);
        }
        finally
        {
            sync.Reset();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = oldProvider;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

    private sealed class ResetReplies : HttpMessageHandler
    {
        internal readonly List<JObject> Bodies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(JObject.Parse(await request.Content!.ReadAsStringAsync(ct)));
            var json = Bodies.Count switch
            {
                1 => "{\"force_skills_reset\":true,\"skill_points\":80}",
                2 => "{\"force_skills_reset\":false,\"skill_points\":50,\"wallet_net\":-130}",
                _ => "{\"skill_points\":50,\"wallet_net\":-130}"
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }

    private sealed class HeldReply : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<bool> Received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Received.TrySetResult(true);
            await Release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"skill_points\":70,\"wallet_net\":-130}") };
        }
    }
}
