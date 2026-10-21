using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Friend links (CONTRACT.md "Links", PR D1): the token grammar and pasted links, the
/// per-account cache, the offline fallback, the wire, and the drawer's Copy link / claim.</summary>
public class FriendsLinkRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("https://app.cclabs.app/f/7K2Q9ABCDEFG", "7K2Q9ABCDEFG")]
    [InlineData("  HTTPS://APP.CCLABS.APP/F/7k2q9abcdefg/  ", "7K2Q9ABCDEFG")]
    [InlineData("app.cclabs.app/f/7k2q9abcdefg?utm=x", "7K2Q9ABCDEFG")]
    [InlineData("http://www.app.cclabs.app/f/7K2Q9ABCDEFG#top", "7K2Q9ABCDEFG")]
    [InlineData("7k2q9abcdefg", "7K2Q9ABCDEFG")]
    [InlineData("https://evil.example/f/7K2Q9ABCDEFG", null)]
    [InlineData("https://app.cclabs.app/f/7K2Q9ABCDEF0", null)]
    [InlineData("https://app.cclabs.app/f/7K2Q9", null)]
    [InlineData("CCP-7K2Q9", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_pasted_link_or_bare_token_reads_as_its_token(string? input, string? expected)
        => Assert.Equal(expected, FriendsLinkRules.ParseToken(input));

    [Theory]
    [InlineData("ccp-7k2q9", "7K2Q9", false)]
    [InlineData("7k2q", "7K2Q", false)]
    [InlineData("CCP7K2Q9", "7K2Q9", false)]
    [InlineData("7K2Q9A", "7K2Q9A", true)]
    [InlineData("https://app.cclabs.app/f/7K2Q9ABCDEFG", "7K2Q9ABCDEFG", true)]
    [InlineData("https://app.cclabs.app/f/7K2Q", "7K2Q", false)]
    [InlineData("7K2Q9ABCDEFGHJK", "7K2Q9ABCDEFG", true)]
    public void The_add_box_keeps_a_code_or_a_token(string typed, string text, bool isToken)
        => Assert.Equal((text, isToken), FriendsLinkRules.BoxText(typed));

    [Fact]
    public void The_url_is_always_built_from_the_token()
        => Assert.Equal("https://app.cclabs.app/f/7K2Q9ABCDEFG", new FriendLink("7K2Q9ABCDEFG", T0, 25).Url);

    [Fact]
    public void A_link_is_usable_until_near_its_end_and_while_uses_are_left()
    {
        Assert.True(FriendsLinkRules.IsUsable(new FriendLink("7K2Q9ABCDEFG", T0.AddDays(7), 25), T0));
        Assert.False(FriendsLinkRules.IsUsable(new FriendLink("7K2Q9ABCDEFG", T0.AddMinutes(2), 25), T0));
        Assert.False(FriendsLinkRules.IsUsable(new FriendLink("7K2Q9ABCDEFG", T0.AddDays(7), 0), T0));
        Assert.False(FriendsLinkRules.IsUsable(null, T0));
    }

    [Fact]
    public void Copy_falls_back_to_the_code_and_then_to_nothing()
    {
        var link = new FriendLink("7K2Q9ABCDEFG", T0.AddDays(7), 25);
        Assert.Equal((link.Url, true), FriendsLinkRules.CopyChoice(link, "CCP-7K2Q9"));
        Assert.Equal(("CCP-7K2Q9", false), FriendsLinkRules.CopyChoice(null, "CCP-7K2Q9"));
        Assert.Equal(((string?)null, false), FriendsLinkRules.CopyChoice(null, ""));
    }

    [Fact]
    public void A_link_reply_parses_only_when_it_fits_the_contract()
    {
        var ok = JObject.Parse("{\"ok\":true,\"url\":\"https://app.cclabs.app/f/7K2Q9ABCDEFG\",\"token\":\"7k2q9abcdefg\",\"expires_at\":\"2026-10-27T12:00:00.000Z\",\"uses_left\":24}");
        var link = FriendsLinkRules.ParseLink(ok)!;
        Assert.Equal("7K2Q9ABCDEFG", link.Token);
        Assert.Equal(24, link.UsesLeft);
        Assert.Equal(T0.AddDays(7), link.ExpiresAt);
        Assert.Null(FriendsLinkRules.ParseLink(JObject.Parse("{\"ok\":true,\"token\":\"BAD\",\"expires_at\":\"2026-10-27T12:00:00Z\"}")));
        Assert.Null(FriendsLinkRules.ParseLink(JObject.Parse("{\"ok\":true,\"token\":\"7K2Q9ABCDEFG\"}")));
        Assert.Null(FriendsLinkRules.ParseLink(JObject.Parse("{\"ok\":false,\"reason\":\"too_fast\"}")));
        Assert.Null(FriendsLinkRules.ParseLink(null));
    }

    [Theory]
    [InlineData("friends", ClaimResult.Friends)]
    [InlineData("already", ClaimResult.Already)]
    [InlineData("self", ClaimResult.Self)]
    [InlineData("full", ClaimResult.Full)]
    [InlineData("unusable", ClaimResult.Unusable)]
    [InlineData("bad_input", ClaimResult.Unusable)]
    [InlineData("too_fast", ClaimResult.TryLater)]
    [InlineData(null, ClaimResult.TryLater)]
    public void Claim_statuses_are_worded(string? wire, ClaimResult expected)
        => Assert.Equal(expected, FriendsLinkRules.ClaimFromWire(wire));
}

public partial class FriendsServiceTests
{
    private static readonly DateTimeOffset L0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_link_is_fetched_once_per_account_until_it_nears_its_end()
    {
        var api = new FakeApi();
        string? account = "u_me";
        var now = L0;
        bool s = false;
        var svc = new FriendsService(api, () => account, () => true, () => now, () => s, v => s = v);
        api.Links.Enqueue(new FriendLink("7K2Q9ABCDEFG", L0.AddDays(7), 25));
        api.Links.Enqueue(new FriendLink("ABCDEFGHJKLM", L0.AddDays(8), 25));
        api.Links.Enqueue(new FriendLink("MNPQRSTUVWXY", L0.AddDays(14), 25));

        Assert.Equal("7K2Q9ABCDEFG", (await svc.GetLinkAsync())!.Token);
        Assert.Equal("7K2Q9ABCDEFG", (await svc.GetLinkAsync())!.Token);
        Assert.Equal(1, api.LinkCalls);

        account = "u_other";
        Assert.Equal("ABCDEFGHJKLM", (await svc.GetLinkAsync())!.Token);
        Assert.Equal(2, api.LinkCalls);

        now = L0.AddDays(8);
        Assert.Equal("MNPQRSTUVWXY", (await svc.GetLinkAsync())!.Token);
        Assert.Equal(3, api.LinkCalls);

        account = null;
        Assert.Null(await svc.GetLinkAsync());
        Assert.Equal(3, api.LinkCalls);
    }

    [Fact]
    public async Task New_link_revokes_then_mints_and_a_failed_revoke_mints_nothing()
    {
        var api = new FakeApi();
        var svc = new FriendsService(api, () => "u_me", () => true, () => L0, () => false, _ => { });
        api.Links.Enqueue(new FriendLink("7K2Q9ABCDEFG", L0.AddDays(7), 25));
        await svc.GetLinkAsync();
        api.Links.Enqueue(new FriendLink("ABCDEFGHJKLM", L0.AddDays(7), 25));
        Assert.Equal("ABCDEFGHJKLM", (await svc.NewLinkAsync())!.Token);
        Assert.Equal(1, api.RevokeCalls);

        api.RevokeAnswer = false;
        Assert.Null(await svc.NewLinkAsync());
        Assert.Equal(2, api.LinkCalls);
    }

    [Fact]
    public async Task A_claim_sends_the_token_and_refreshes_on_friends()
    {
        var api = new FakeApi();
        var svc = new FriendsService(api, () => "u_me", () => true, () => L0, () => false, _ => { });
        Assert.Equal(ClaimResult.Friends, await svc.ClaimLinkAsync("https://app.cclabs.app/f/7k2q9abcdefg"));
        Assert.Equal("7K2Q9ABCDEFG", api.Claims.Single());
        Assert.Equal(1, api.StateCalls);

        Assert.Equal(ClaimResult.Unusable, await svc.ClaimLinkAsync("not a link"));
        Assert.Single(api.Claims);

        api.Links.Enqueue(new FriendLink("ABCDEFGHJKLM", L0.AddDays(7), 25));
        await svc.GetLinkAsync();
        Assert.Equal(ClaimResult.Self, await svc.ClaimLinkAsync("ABCDEFGHJKLM"));
        Assert.Single(api.Claims);
    }
}

public class FriendsLinkWireTests
{
    private sealed class Handler : HttpMessageHandler
    {
        private readonly string _reply;
        public string? LastBody, LastUri;
        public Handler(string reply) => _reply = reply;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.ToString();
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_reply, Encoding.UTF8, "application/json") };
        }
    }

    private static (FriendsApi, Handler) Make(string reply)
    {
        var h = new Handler(reply);
        return (new FriendsApi(new HttpClient(h), () => ("u_me", "tok"), "http://127.0.0.1:1"), h);
    }

    [Fact]
    public async Task Link_ops_post_to_the_friends_door()
    {
        var (api, h) = Make("{\"ok\":true,\"url\":\"https://app.cclabs.app/f/7K2Q9ABCDEFG\",\"token\":\"7K2Q9ABCDEFG\",\"expires_at\":\"2026-10-27T12:00:00Z\",\"uses_left\":25}");
        var link = await api.LinkAsync();
        Assert.Equal("7K2Q9ABCDEFG", link!.Token);
        Assert.EndsWith("/v2/friends/link", h.LastUri);
        Assert.Equal("u_me", JObject.Parse(h.LastBody!).Value<string>("unified_id"));

        var (revoke, rh) = Make("{\"ok\":true}");
        Assert.True(await revoke.LinkRevokeAsync());
        Assert.EndsWith("/v2/friends/link_revoke", rh.LastUri);

        var (claim, ch) = Make("{\"ok\":true,\"status\":\"already\"}");
        Assert.Equal(ClaimResult.Already, await claim.ClaimAsync("7K2Q9ABCDEFG"));
        Assert.EndsWith("/v2/friends/claim", ch.LastUri);
        Assert.Equal("7K2Q9ABCDEFG", JObject.Parse(ch.LastBody!).Value<string>("token"));

        var (old, _) = Make("<html>404</html>");
        Assert.Null(await old.LinkAsync());
        Assert.Equal(ClaimResult.TryLater, await old.ClaimAsync("7K2Q9ABCDEFG"));
    }
}
