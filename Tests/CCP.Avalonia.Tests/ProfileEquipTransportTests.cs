using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ProfileSlots = ConditioningControlPanel.Services.ProfileEquip.ProfileSlots;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    private sealed class EquipWire : HttpMessageHandler
    {
        internal readonly List<(HttpMethod Method, string Path, string? Auth, JObject? Body)> Seen = new();
        internal Func<CancellationToken, Task<HttpResponseMessage>> Reply = _ => Task.FromResult(Response("{}"));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = req.Content == null ? null : JObject.Parse(await req.Content.ReadAsStringAsync(ct));
            Seen.Add((req.Method, req.RequestUri!.PathAndQuery, string.Join("", req.Headers.GetValues("X-Auth-Token")), body));
            return await Reply(ct);
        }
    }

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };
    private const string AvatarReply = "{\"ok\":true,\"equip\":{\"profile\":{\"frame_id\":null,\"avatar_id\":\"avatar_bambi_2\"},\"updatedAt\":500}}";

    private async Task WithEquipWire(Func<EquipWire, Task> body)
    {
        var s = CoreSettings.Current;
        var old = (s.ProfileCosmetics, s.ProfileEquipAccount, s.ProfileEquipUpdatedMs, s.ProfileEquipPushOwed, s.UnifiedId, s.AuthToken, s.OfflineMode);
        var http = ProfileEquipSync.Http;
        var wire = new EquipWire();
        using var client = new HttpClient(wire);
        try
        {
            ProfileEquipSync.ResetForTests();
            ProfileEquipSync.PostForTests = a => a();
            ProfileEquipSync.Http = client;
            ProfileEquipSync.ReadyForTests = _ => true;
            (s.ProfileCosmetics, s.ProfileEquipAccount, s.ProfileEquipUpdatedMs, s.ProfileEquipPushOwed) = (new ProfileCosmetics(), null, 0, false);
            (s.UnifiedId, s.AuthToken, s.OfflineMode) = ("acct a", "test-token", false);
            await body(wire);
        }
        finally
        {
            ProfileEquipSync.ResetForTests();
            ProfileEquipSync.Http = http;
            (s.ProfileCosmetics, s.ProfileEquipAccount, s.ProfileEquipUpdatedMs, s.ProfileEquipPushOwed, s.UnifiedId, s.AuthToken, s.OfflineMode) = old;
        }
    }

    [Fact]
    public void EquipPendingClear_PutsBothSlotsAndSettlesOnServerAnswer() => WithFreshInstall(() => WithEquipWire(async wire =>
    {
        var s = CoreSettings.Current;
        s.ProfileCosmetics = new ProfileCosmetics { BannerId = "keep", AvatarId = "avatar_bambi_1" };
        ProfileEquipSync.Saved(ProfileSlots.Of(s.ProfileCosmetics), s.ProfileCosmetics);
        Assert.Empty(wire.Seen); // a banner-only save cannot stamp the shared equip clock
        s.OfflineMode = true;
        var before = ProfileSlots.Of(s.ProfileCosmetics);
        s.ProfileCosmetics = new ProfileCosmetics { BannerId = "keep" };
        ProfileEquipSync.Saved(before, s.ProfileCosmetics);
        Assert.True(s.ProfileEquipPushOwed);
        s.OfflineMode = false;
        wire.Reply = _ => Task.FromResult(Response(AvatarReply));
        await ProfileEquipSync.PullOnceAsync();
        var request = Assert.Single(wire.Seen);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/v2/equip", request.Path);
        Assert.Equal("test-token", request.Auth);
        Assert.Equal("acct a", (string?)request.Body!["unified_id"]);
        Assert.Equal("profile", (string?)request.Body["family"]);
        Assert.Equal(JTokenType.Null, request.Body["value"]!["frame_id"]!.Type);
        Assert.Equal(JTokenType.Null, request.Body["value"]!["avatar_id"]!.Type);
        Assert.Equal("avatar_bambi_2", s.ProfileCosmetics.AvatarId);
        Assert.Equal("keep", s.ProfileCosmetics.BannerId);
        Assert.False(s.ProfileEquipPushOwed);
        Assert.Equal(500, s.ProfileEquipUpdatedMs);
    }));

    [Fact]
    public void EquipGet_RefusalRetries_AndLateAccountCannotAdopt() => WithFreshInstall(() => WithEquipWire(async wire =>
    {
        var s = CoreSettings.Current;
        wire.Reply = _ => Task.FromResult(Response("{}", HttpStatusCode.ServiceUnavailable));
        await ProfileEquipSync.PullOnceAsync();
        Assert.Empty(s.ProfileCosmetics.AvatarId ?? "");
        wire.Reply = _ => Task.FromResult(Response(AvatarReply));
        await ProfileEquipSync.PullOnceAsync();
        Assert.Equal("avatar_bambi_2", s.ProfileCosmetics.AvatarId);
        Assert.StartsWith("/v2/equip?unified_id=acct%20a", wire.Seen[0].Path);
        Assert.Equal(2, wire.Seen.Count);
        await ProfileEquipSync.PullOnceAsync();
        Assert.Equal(2, wire.Seen.Count); // once per account per launch

        ProfileEquipSync.ResetForTests(); ProfileEquipSync.PostForTests = a => a(); ProfileEquipSync.ReadyForTests = _ => true;
        s.ProfileCosmetics = new ProfileCosmetics(); s.ProfileEquipAccount = null;
        var hold = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.Reply = _ => hold.Task;
        var pull = ProfileEquipSync.PullOnceAsync();
        s.UnifiedId = "other";
        hold.SetResult(Response(AvatarReply));
        await pull;
        Assert.Null(s.ProfileCosmetics.AvatarId);

    }));
    [Fact]
    public void EquipFailureRetainsDebt_FirstRunPutPreservesEmptySlot() => WithFreshInstall(() => WithEquipWire(async wire =>
    {
        var s = CoreSettings.Current;
        s.ProfileCosmetics = new ProfileCosmetics { AvatarId = "avatar_bambi_1" };
        s.ProfileEquipAccount = s.UnifiedId;
        s.ProfileEquipUpdatedMs = 20;
        s.ProfileEquipPushOwed = true;
        wire.Reply = _ => Task.FromResult(Response("{}", HttpStatusCode.ServiceUnavailable));
        await ProfileEquipSync.PullOnceAsync();
        Assert.True(s.ProfileEquipPushOwed);
        Assert.Equal("avatar_bambi_1", s.ProfileCosmetics.AvatarId);
        Assert.Equal(20, s.ProfileEquipUpdatedMs);

        wire.Reply = _ => Task.FromResult(Response(AvatarReply));
        await ProfileEquipSync.PullOnceAsync();
        Assert.False(s.ProfileEquipPushOwed); // a failed PUT can retry on the next profile load
        Assert.Equal("avatar_bambi_2", s.ProfileCosmetics.AvatarId);

        ProfileEquipSync.ResetForTests(); ProfileEquipSync.PostForTests = a => a(); ProfileEquipSync.ReadyForTests = _ => true;
        s.ProfileCosmetics.AvatarId = "avatar_bambi_1";
        s.ProfileEquipAccount = null; s.ProfileEquipPushOwed = false; s.ProfileEquipUpdatedMs = 0;
        wire.Seen.Clear();
        wire.Reply = _ => Task.FromResult(Response(AvatarReply));
        await ProfileEquipSync.PullOnceAsync();
        Assert.Equal(2, wire.Seen.Count);
        Assert.Equal(HttpMethod.Get, wire.Seen[0].Method);
        Assert.Equal(HttpMethod.Put, wire.Seen[1].Method);
        var value = wire.Seen[1].Body!["value"]!;
        Assert.Null(value["frame_id"]);
        Assert.Equal("avatar_bambi_1", (string?)value["avatar_id"]);
        Assert.Equal("avatar_bambi_2", s.ProfileCosmetics.AvatarId);
    }));

    // WPF ProfileSyncService.cs:1949 + :2703: fill the full loadout before equip.
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(5)]
    public void FreshLogin_SyncAnswerFillsLoadout_EquipAndNextSyncKeepIt(int scenario) => WithFreshInstall(() => WithEquipWire(async equip =>
    {
        var s = CoreSettings.Current;
        var banner = ConditioningControlPanel.Services.CosmeticsPool.Banners[0].Id; var pin = Achievement.All.Keys.First();
        var (wire, sync, _) = SignIn("acct a", L40Profile("acct a"));
        var answer = new JObject { ["success"] = true, ["cosmetics"] = new JObject
            { ["banner_id"] = banner, ["accent"] = "#FF69B4", ["avatar_deco"] = "bambi_silk_bow", ["pinned_achievements"] = new JArray(pin) } }.ToString();
        wire.SyncReply = scenario == 2 ? "{\"success\":true}" : scenario == 3 ? "{\"success\":true,\"cosmetics\":5}" : scenario == 4 ? answer.Replace("true", "false") : answer;
        wire.SyncStatus = scenario == 1 ? HttpStatusCode.ServiceUnavailable : scenario == 5 ? (HttpStatusCode)429 : HttpStatusCode.OK;
        ProfileEquipSync.ReadyForTests = null;
        equip.Reply = _ => Task.FromResult(Response(AvatarReply));
        Task? pulled = null;
        sync.CosmeticsReady += () => pulled = ProfileEquipSync.PullOnceAsync();
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Null(Assert.Single(wire.Syncs)["cosmetics"]);
        if (scenario != 0)
        {
            await ProfileEquipSync.PullOnceAsync();
            Assert.Empty(equip.Seen);
            Assert.True(s.ProfileCosmetics.IsEmpty);
            Assert.Null(pulled);
            wire.SyncStatus = HttpStatusCode.OK; wire.SyncReply = answer;
            sync.UtcNow = () => T0.AddMinutes(1);
            Assert.True(await sync.PushAsync("retry"));
            Assert.Null(wire.Syncs.Last()["cosmetics"]);
        }
        Assert.Equal(banner, s.ProfileCosmetics.BannerId);
        Assert.NotNull(pulled);
        await pulled!;
        Assert.Equal("avatar_bambi_2", s.ProfileCosmetics.AvatarId);
        s.UnifiedId = "other"; await ProfileEquipSync.PullOnceAsync(); Assert.Single(equip.Seen); s.UnifiedId = "acct a";

        sync.UtcNow = () => T0.AddMinutes(2);
        Assert.True(await sync.PushAsync("level-up"));
        var sent = wire.Syncs.Last()["cosmetics"]!;
        Assert.Equal(banner, (string?)sent["banner_id"]);
        Assert.Equal("#FF69B4", (string?)sent["accent"]);
        Assert.Equal("bambi_silk_bow", (string?)sent["avatar_deco"]); Assert.Equal(pin, (string?)sent["pinned_achievements"]![0]);
        Assert.Equal("avatar_bambi_2", (string?)sent["avatar_id"]);

        // An unequip still owed to /v2/equip: the echo is what was just taken off, so it is not adopted.
        s.ProfileCosmetics = new ProfileCosmetics();
        s.ProfileEquipPushOwed = true;
        sync.UtcNow = () => T0.AddMinutes(3);
        Assert.True(await sync.PushAsync("level-up"));
        Assert.True(s.ProfileCosmetics.IsEmpty);
    }));
}
