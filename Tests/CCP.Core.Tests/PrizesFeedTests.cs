using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Prizes;
using Xunit;
namespace CCP.Core.Tests;
public class PrizesFeedTests
{
    private const string Me = "uid-me", Other = "uid-other";
    private string? _account = Me;
    private readonly List<OwnershipChangedEventArgs> _events = new();
    private readonly OwnershipService _ownership;
    private readonly PrizesFeed _feed;
    public PrizesFeedTests()
    {
        _ownership = new OwnershipService(() => _account, null, invoke => invoke());
        _ownership.OwnershipChanged += (_, e) => _events.Add(e);
        _feed = new PrizesFeed(() => _ownership, () => _account, invoke => invoke());
    }
    private static PrizesBlock Block(long revision, params string[] grants)
        => new() { Revision = revision, Grants = grants.ToList() };
    [Fact]
    public void SyncSnapshot_IsApplied()
    {
        Assert.True(_feed.Apply(Me, Me, Block(3, PrizeGrants.JackpotRemix, PrizeGrants.RacingTrack(0)), "test"));
        Assert.True(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.True(_ownership.IsGranted("rt.original.00"));
        Assert.False(_ownership.IsGranted("rt.original.01"));
        Assert.Equal(3, _ownership.Revision);
        var e = Assert.Single(_events);
        Assert.Equal(2, e.Added.Count);
        Assert.True(_feed.Apply(Me, Me, Block(4, PrizeGrants.JackpotRemix, PrizeGrants.RacingTrack(0), "fx.flash.pendulum"), "test"));
        Assert.True(_ownership.IsGranted("fx.flash.pendulum"));
        Assert.Equal(new[] { "fx.flash.pendulum" }, _events[1].Added);
    }
    [Fact]
    public void MissingBlock_LeavesStateAlone()
    {
        _feed.Apply(Me, Me, Block(2, PrizeGrants.JackpotRemix), "test");
        _events.Clear();
        Assert.False(_feed.Apply(Me, Me, null, "old server"));
        Assert.False(_feed.Apply(Me, Me, new PrizesBlock { Revision = 9, Grants = null }, "grants missing"));
        Assert.True(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Equal(2, _ownership.Revision);
        Assert.Empty(_events);
    }
    [Fact]
    public void EmptyGrants_IsARealSnapshot()
    {
        _feed.Apply(Me, Me, Block(2, PrizeGrants.JackpotRemix), "test");
        Assert.True(_feed.Apply(Me, Me, Block(3), "test"));
        Assert.False(_ownership.IsGranted(PrizeGrants.JackpotRemix));
    }
    [Fact]
    public void OtherAccountResponse_IsIgnored()
    {
        _feed.Apply(Me, Me, Block(2, PrizeGrants.JackpotRemix), "test");
        _events.Clear();
        Assert.False(_feed.Apply(Me, Other, Block(7, "fx.bubble.rain"), "validate"));
        Assert.False(_feed.Apply(Me, null, Block(7, "fx.bubble.rain"), "validate"));
        Assert.False(_feed.Apply(null, Me, Block(7, "fx.bubble.rain"), "validate"));
        Assert.False(_ownership.IsGranted("fx.bubble.rain"));
        Assert.True(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Empty(_events);
    }
    [Fact]
    public void ResponseForAnAccountNoLongerSignedIn_IsIgnored()
    {
        _account = Other;
        _feed.Apply(Me, Me, Block(5, PrizeGrants.JackpotRemix), "V2 sync");
        Assert.False(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Empty(_ownership.Grants);
        Assert.Empty(_events);
    }
    [Fact]
    public void LateResponseForThePreviousAccount_DoesNotClearTheCurrentOne()
    {
        _feed.Apply(Me, Me, Block(3, PrizeGrants.JackpotRemix), "V2 sync");
        _account = Other;
        _feed.NoteAccount(Other);
        _feed.Apply(Other, Other, Block(1, "fx.bubble.rain"), "V2 sync");
        _events.Clear();
        _feed.Apply(Me, Me, Block(9, PrizeGrants.JackpotRemix), "Patreon validate");
        _feed.NoteAccount(Me);
        Assert.True(_ownership.IsGranted("fx.bubble.rain"));
        Assert.False(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Empty(_events);
        _feed.NoteAccount(Other);
        Assert.Empty(_events);
        Assert.Equal(1, _ownership.Revision);
    }
    [Fact]
    public void PoolThreadSnapshot_ThenUiThreadLogout_ListenersEndWithNothing()
    {
        var queue = new Queue<Action>();
        var onUi = false;
        string? account = Me;
        var ownership = new OwnershipService(() => account, null, invoke => { if (onUi) invoke(); else queue.Enqueue(invoke); });
        var heard = new HashSet<string>();
        ownership.OwnershipChanged += (_, e) => { heard.UnionWith(e.Added); heard.ExceptWith(e.Removed); };
        var feed = new PrizesFeed(() => ownership, () => account, queue.Enqueue);
        feed.Apply(Me, Me, Block(3, PrizeGrants.JackpotRemix), "V2 sync");
        onUi = true;
        account = null;
        feed.Clear();
        while (queue.Count > 0) queue.Dequeue()();
        Assert.Empty(ownership.Grants);
        Assert.Empty(heard);
    }
    [Fact]
    public void Logout_Clears()
    {
        _feed.Apply(Me, Me, Block(3, PrizeGrants.JackpotRemix, "fx.bubble.rain"), "test");
        _events.Clear();
        _feed.Clear();
        Assert.False(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Equal(0, _ownership.Revision);
        var e = Assert.Single(_events);
        Assert.Empty(e.Added);
        Assert.Equal(2, e.Removed.Count);
        Assert.True(_feed.Apply(Me, Me, Block(3, PrizeGrants.JackpotRemix), "test"));
        Assert.True(_ownership.IsGranted(PrizeGrants.JackpotRemix));
    }
    [Fact]
    public void AccountSwitch_ClearsBeforeTheNextAccountsSnapshot()
    {
        _feed.Apply(Me, Me, Block(8, PrizeGrants.JackpotRemix), "test");
        _events.Clear();
        _account = Other;
        _feed.NoteAccount(Other);
        var cleared = Assert.Single(_events);
        Assert.Equal(new[] { PrizeGrants.JackpotRemix }, cleared.Removed);
        Assert.Equal(0, _ownership.Revision);
        Assert.True(_feed.Apply(Other, Other, Block(1, "fx.bubble.rain"), "V2 sync"));
        Assert.True(_ownership.IsGranted("fx.bubble.rain"));
        Assert.False(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        _events.Clear();
        _feed.NoteAccount(Other);
        Assert.Empty(_events);
    }
    [Fact]
    public void ValidateResponses_ParseThePrizesBlock_WithBothSerializers()
    {
        const string json = "{\"unified_id\":\"uid-me\",\"prizes\":{\"revision\":4,\"grants\":[\"fx.jackpot_remix\",\"rt.original.00\"]}}";
        var patreon = System.Text.Json.JsonSerializer.Deserialize<PatreonSubscriptionResponse>(json)!;
        Assert.Equal(4, patreon.Prizes!.Revision);
        Assert.Equal(new[] { "fx.jackpot_remix", "rt.original.00" }, patreon.Prizes.Grants);
        var discord = System.Text.Json.JsonSerializer.Deserialize<DiscordUserResponse>(json)!;
        Assert.Equal("uid-me", discord.UnifiedId);
        Assert.Equal(2, discord.Prizes!.Grants!.Count);
        var viaNewtonsoft = Newtonsoft.Json.JsonConvert.DeserializeObject<PatreonSubscriptionResponse>(json)!;
        Assert.Equal(4, viaNewtonsoft.Prizes!.Revision);
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<DiscordUserResponse>("{\"unified_id\":\"uid-me\"}")!.Prizes);
    }
    [Fact]
    public void Grants_AreNeverCachedWithProviderState()
    {
        Assert.Null(typeof(PatreonCachedState).GetProperty("Prizes"));
        Assert.Null(typeof(DiscordCachedState).GetProperty("Prizes"));
    }
}
