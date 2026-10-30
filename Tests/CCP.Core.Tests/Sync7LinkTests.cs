using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class Sync7LinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static FriendLink Link(string token = "7K2Q9ABCDEFG") => new(token, Now.AddDays(7), 25);
    private sealed class Api : IFriendsApi
    {
        internal Queue<FriendLink?> Links = new();
        internal int Calls, Revokes, States;
        internal FriendsPollReply? Poll;
        internal TaskCompletionSource<FriendLink?>? Gate;
        public Task<FriendLink?> LinkAsync(CancellationToken ct = default)
        { Calls++; if (Gate is { } gate) { Gate = null; return gate.Task; } return Task.FromResult(Links.Count > 0 ? Links.Dequeue() : null); }
        public Task<bool> LinkRevokeAsync(CancellationToken ct = default) { Revokes++; return Task.FromResult(true); }
        public Task<ClaimResult> ClaimAsync(string token, CancellationToken ct = default) => Task.FromResult(ClaimResult.Friends);
        public Task<FriendsSnapshot?> StateAsync(CancellationToken ct = default) { States++; return Task.FromResult<FriendsSnapshot?>(FriendsSnapshot.Empty); }
        public Task<FriendsPollReply?> PollAsync(PresenceActivity? activity, int? lockDay, bool shared, CancellationToken ct = default) => Task.FromResult(Poll);
        public Task<AddResult> RequestAsync(string code, CancellationToken ct = default) => Task.FromResult(AddResult.Sent);
        public Task<SendResult> SendAsync(string to, SendKind kind, string? poke, string? destination, string? code, WatchRef? watch, CancellationToken ct = default) => Task.FromResult(SendResult.Sent);
        public Task<bool> ActAsync(string op, string id, JObject? extra = null, CancellationToken ct = default) => Task.FromResult(true);
    }

    [Fact]
    public void VerifiedSetupWritesAllThreeFields()
    {
        var prompt = new CompanionPromptSettings { AiOllamaHost = "", AiProvider = AiProviderType.Cloud };
        OllamaSetupService.ApplyVerifiedSetup(prompt, OllamaSetupService.DefaultHost, "model");
        Assert.Equal("http://localhost:11434/", prompt.AiOllamaHost);
        Assert.Equal(AiProviderType.Local, prompt.AiProvider);
        Assert.Equal("model", prompt.AiModel);
    }

    [Fact]
    public async Task EveryPressAsksAndCacheBelongsToAccountAndClaimRefreshesOnce()
    {
        var api = new Api();
        string account = "a";
        using var svc = new FriendsService(api, () => account, () => true, () => Now, () => false, _ => { });
        api.Links.Enqueue(Link());
        Assert.Equal(Link(), await svc.GetLinkAsync());
        Assert.Equal(Link(), await svc.GetLinkAsync()); // failed call falls back
        Assert.Equal(2, api.Calls);
        account = "b";
        Assert.Null(await svc.GetLinkAsync());
        Assert.Equal(ClaimResult.Friends, await svc.ClaimLinkAsync("https://app.cclabs.app/f/7k2q9abcdefg"));
        Assert.Equal(1, api.States);
    }

    [Fact]
    public async Task NearExpiryRenewsAndLateRepliesCannotReviveRetiredLink()
    {
        var api = new Api();
        using var svc = new FriendsService(api, () => "a", () => true, () => Now, () => false, _ => { });
        api.Links.Enqueue(Link() with { ExpiresAt = Now.AddMinutes(30) });
        api.Links.Enqueue(Link("ABCDEFGHJKLM"));
        Assert.Equal("ABCDEFGHJKLM", (await svc.GetLinkAsync())!.Token);
        Assert.Equal(1, api.Revokes);
        var gate = api.Gate = new TaskCompletionSource<FriendLink?>();
        var slow = svc.GetLinkAsync();
        api.Links.Enqueue(Link("BCDEFGHJKLMN"));
        await svc.NewLinkAsync();
        gate.SetResult(Link());
        Assert.Equal("BCDEFGHJKLMN", (await slow)!.Token);
    }

    [Fact]
    public async Task AcceptedClaimReceiptFetchesStateInTheSameTick()
    {
        var api = new Api();
        using var svc = new FriendsService(api, () => "a", () => true, () => Now, () => false, _ => { });
        await svc.TickAsync();
        var before = api.States;
        api.Poll = new FriendsPollReply(Array.Empty<string>(), Array.Empty<InboxItem>(), null,
            new[] { new SenderReceipt(null, "request", "u_b", "B", "accepted", Now.UtcDateTime, null) });
        await svc.TickAsync();
        Assert.Equal(before + 1, api.States);
    }

    [Fact]
    public void NotesHostChecksAndReceiptDedupeUseRealData()
    {
        Assert.Equal(("friends_link_copied_left_hours", 5, 3), FriendsLinkRules.CopiedNote(Link() with { ExpiresAt = Now.AddHours(5), UsesLeft = 3 }, Now));
        Assert.Equal(("", false), FriendsLinkRules.BoxText("https://evil.example/f/7K2Q9ABCDEFG"));
        Assert.Equal(("CCPABCDEFGHJ", true), FriendsLinkRules.BoxText("CCPABCDEFGHJ", true));
        Assert.Equal("friends_link_full", FriendsLinkRules.ClaimResultKey(ClaimResult.Full));
        var receipt = new SenderReceipt(null, "request", "u_b", "B", "accepted", Now.UtcDateTime, null);
        Assert.True(FriendsPollRule.AcceptedRequest(new[] { receipt }));
        Assert.NotEqual(FriendsService.FeedLine(receipt)!.Key, FriendsService.FeedLine(receipt with { AtUtc = Now.AddHours(2).UtcDateTime })!.Key);
        Assert.Same(FriendPlatform.Normalise(new[] { "phone", "desktop" }), FriendPlatform.Normalise(new[] { "desktop", "phone" }));
    }
}
