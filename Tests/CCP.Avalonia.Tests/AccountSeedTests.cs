using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The head's CoreAccount seed (Platform/AccountSeed.cs): entitled only on a real answer,
/// FAIL CLOSED on any exception.</summary>
public sealed partial class AccountSeedTests : IDisposable
{
    private readonly Action<string?, string?, PrizesBlock?, string>? _oldPrizes = ProviderSubscription.PrizesSink;
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();

    public AccountSeedTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
        Unseed();
    }

    public void Dispose()
    {
        ProviderSubscription.PrizesSink = _oldPrizes;
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
        Unseed();
    }

    private static void Unseed()
    {
        CoreAccount.IsLoggedInProvider = CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = CoreAccount.IsWhitelistedProvider = null;
        CoreAccount.DisplayNameProvider = null;
        CoreAccount.PatreonAccessTokenProvider = null;
        CoreAccount.ChangeDisplayNameProvider = null;
        CoreAccount.DeleteAccountProvider = null;
        CoreAccount.UnifiedUserId = null;
        CoreEntitlement.HasPremiumProvider = CoreEntitlement.HasLabProvider = null;
        AuthRecovery.ProviderRevalidate = null;
        AuthRecovery.Recovered = null;
    }

    [Fact]
    public void OnlyServerRisesRaiseTheSignal()
    {
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.AuthToken, s.PatreonTier, s.PatreonPremiumValidUntil,
            s.PatreonLabValidUntil, s.ServerTierSeen);
        var rises = new List<int>();
        void Raised(int tier) => rises.Add(tier);
        try
        {
            s.UnifiedId = "rise-signal-test";
            s.AuthToken = "test";
            s.PatreonTier = 0;
            AccountSeed.TierRaised += Raised;
            AccountSeed.ApplyTierReading(1, "test");
            AccountSeed.ApplyTierReading(1, "test");
            AccountSeed.ApplyTierReading(0, "test");
            AccountSeed.ApplyTierReading(2, "test");
            Assert.Equal(new[] { 1, 2 }, rises);
        }
        finally
        {
            AccountSeed.TierRaised -= Raised;
            (s.UnifiedId, s.AuthToken, s.PatreonTier, s.PatreonPremiumValidUntil,
                s.PatreonLabValidUntil, s.ServerTierSeen) = old;
        }
    }

    private static ProviderSubscription Make(string prefix) => new(prefix, () => new AppSettings());

    private static void AssertNothing()
    {
        Assert.False(CoreAccount.IsLoggedIn);
        Assert.False(CoreAccount.IsWhitelisted);
        Assert.False(CoreAccount.HasPremiumAccess);
        Assert.False(CoreAccount.HasLabAccess);
        Assert.False(CoreEntitlement.HasPremium); // TierGate's seam
        Assert.False(CoreEntitlement.HasLab);
    }

    [Fact]
    public void PatreonTokenTracksStoredTokensAndFailsClosed()
    {
        Assert.True(AccountSeed.Seed(Make));
        var patreon = AccountSeed.Patreon!;
        patreon.StoreTokens("token", "refresh", DateTime.UtcNow.AddDays(1));
        Assert.Equal("token", CoreAccount.PatreonAccessToken);
        patreon.ClearTokens();
        Assert.Null(CoreAccount.PatreonAccessToken);
        CoreSecrets.RetrieveProvider = _ => throw new InvalidOperationException("keyring gone");
        Assert.Null(CoreAccount.PatreonAccessToken);
    }

    [Fact]
    public async Task LegacyChatSendsTheSeededPatreonBearer()
    {
        Assert.True(AccountSeed.Seed(Make));
        AccountSeed.Patreon!.StoreTokens("token", "refresh", DateTime.UtcNow.AddDays(1));
        CoreAccount.HasPremiumAccessProvider = () => true;
        CoreAccount.UnifiedUserId = null;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var url = $"http://127.0.0.1:{port}";
        using var listener = new HttpListener();
        listener.Prefixes.Add(url + "/");
        listener.Start();
        using var ai = new AiService(url);
#pragma warning disable CS0618
        var reply = ai.GetBambiReplyExAsync("hello there");
#pragma warning restore CS0618
        var incoming = listener.GetContextAsync();
        Assert.Same(incoming, await Task.WhenAny(incoming, Task.Delay(TimeSpan.FromSeconds(10))));
        var ctx = await incoming;
        Assert.Equal("POST", ctx.Request.HttpMethod);
        Assert.Equal("/ai/chat", ctx.Request.Url!.AbsolutePath);
        Assert.Equal("Bearer token", ctx.Request.Headers["Authorization"]);
        var body = Encoding.UTF8.GetBytes("{\"content\":\"hello\"}");
        await ctx.Response.OutputStream.WriteAsync(body);
        ctx.Response.Close();
        Assert.True((await reply).IsAiGenerated);
    }

    [Fact]
    public void AStoredTier2Patron_IsEntitled()
    {
        // Positive control: the same seed DOES answer yes from a real cached validate.
        _secrets["patreon_auth"] = JsonConvert.SerializeObject(new PatreonTokenData
        { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(10) });
        _secrets["patreon_cache"] = JsonConvert.SerializeObject(new PatreonCachedState
        { Tier = PatreonTier.Level2, IsActive = true, CacheExpiresAt = DateTime.UtcNow.AddHours(1), DisplayName = "Bambi" });
        Assert.True(AccountSeed.Seed(Make));
        Assert.True(CoreAccount.IsLoggedIn);
        Assert.True(CoreAccount.HasPremiumAccess);
        Assert.True(CoreAccount.HasLabAccess);
        // TierGate reads CoreEntitlement, not CoreAccount: the seed must feed both (WPF App.xaml.cs:487).
        Assert.True(CoreEntitlement.HasPremium);
        Assert.True(CoreEntitlement.HasLab);
        Assert.Equal("Bambi", CoreAccount.DisplayName);
    }

    [Fact]
    public void AStoredWhitelistedDiscordUser_IsLoggedInAndEntitled()
    {
        // Parallel to Patreon, as WPF: Discord's cached whitelist promotes the Patreon gate (DiscordService.ApplyWhitelistAccess).
        _secrets["discord_auth"] = JsonConvert.SerializeObject(new DiscordTokenData
        { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(5) });
        _secrets["discord_cache"] = JsonConvert.SerializeObject(new DiscordCachedState
        { UserId = "4242", Username = "bambi", IsWhitelisted = true, CacheExpiresAt = DateTime.UtcNow.AddHours(1) });
        Assert.True(AccountSeed.Seed(Make, p => new DiscordAccount(() => p, () => new AppSettings())));
        Assert.True(CoreAccount.IsLoggedIn);
        Assert.True(CoreAccount.IsWhitelisted);
        Assert.True(CoreAccount.HasLabAccess);
        Assert.Equal("bambi", CoreAccount.DisplayName);
    }

    [Fact]
    public void AProviderThatCannotBeBuilt_SeedsNothing()
    {
        Assert.False(AccountSeed.Seed(_ => throw new InvalidOperationException("boom")));
        AssertNothing();
        Assert.False(AccountSeed.Seed(Make, _ => throw new InvalidOperationException("boom")));
        AssertNothing();
    }

    [Fact]
    public void NoTokens_OrAStoreThatThrows_IsNotEntitled()
    {
        Assert.True(AccountSeed.Seed(Make));
        AssertNothing();
        CoreSecrets.RetrieveProvider = _ => throw new InvalidOperationException("keyring gone");
        AssertNothing();
    }

    [Fact]
    public async Task Logout_ClearsTokensIdentityAndProgression_AndRestoreBringsTheIdBack()
    {
        var s = CoreSettings.Current;
        var (oldId, oldName, oldLevel, oldXp) = (s.UnifiedId, s.UserDisplayName, s.PlayerLevel, s.PlayerXP);
        try
        {
            s.UnifiedId = "u-1"; s.UserDisplayName = "Bambi"; s.HasLinkedDiscord = s.HasLinkedPatreon = true;
            s.AuthToken = "tok"; s.PlayerLevel = 12; s.PlayerXP = 345;
            foreach (var n in new[] { "patreon_auth", "discord_auth", "substar_auth" })
                _secrets[n] = JsonConvert.SerializeObject(new PatreonTokenData { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(1) });
            Assert.True(AccountSeed.Seed(Make, p => new DiscordAccount(() => p, () => new AppSettings())));

            AccountSeed.RestoreSession(); // WPF App.xaml.cs:2267
            Assert.Equal("u-1", CoreAccount.UnifiedUserId);

            await AccountSeed.Logout();

            Assert.False(CoreAccount.IsLoggedIn);
            Assert.Null(CoreAccount.UnifiedUserId);
            Assert.Null(s.UnifiedId);
            Assert.Null(s.UserDisplayName);
            Assert.Null(s.AuthToken);
            Assert.False(s.HasLinkedDiscord || s.HasLinkedPatreon);
            Assert.All(new[] { "patreon_auth", "discord_auth", "substar_auth" }, n => Assert.Null(_secrets.GetValueOrDefault(n)));
            // Unit 7c: the progression clear ships with the push (WPF ClearProgressionData).
            Assert.Equal(1, s.PlayerLevel);
            Assert.Equal(0, s.PlayerXP);
        }
        finally { (s.UnifiedId, s.UserDisplayName, s.PlayerLevel, s.PlayerXP) = (oldId, oldName, oldLevel, oldXp); }
    }

    [Fact]
    public async Task Logout_WhoseKeyringClearFails_IsReported_AndASuccessfulOneResetsIt()
    {
        var (sandboxed, notRemembered) = (SecretStore.Sandboxed, SecretStore.NotRemembered);
        var clearsWork = false;
        try
        {
            SecretStore.Sandboxed = SecretStore.NotRemembered = false;
            SecretStore.OsWriteOverride = (_, v) => v != null || clearsWork;   // a locked keyring: writes land, removals do not
            CoreSecrets.RetrieveProvider = SecretStore.Retrieve;
            CoreSecrets.StoreProvider = SecretStore.Store;
            Assert.True(AccountSeed.Seed(Make, p => new DiscordAccount(() => p, () => new AppSettings())));
            SecretStore.Store("patreon_auth", "{}");

            await AccountSeed.Logout();
            Assert.True(SecretStore.ClearFailed);

            clearsWork = true;
            await AccountSeed.Logout();
            Assert.False(SecretStore.ClearFailed);
        }
        finally
        {
            SecretStore.OsWriteOverride = null;
            (SecretStore.Sandboxed, SecretStore.NotRemembered) = (sandboxed, notRemembered);
            SecretStore.ClearFailed = false;
        }
    }

    [Fact]
    public async Task RestoreSession_NeedsSomethingToProveIt_AndA404SignsOut()
    {
        var s = CoreSettings.Current;
        var oldId = s.UnifiedId;
        try
        {
            Assert.True(AccountSeed.Seed(Make, p => new DiscordAccount(() => p, () => new AppSettings())));
            s.UnifiedId = "u-1";
            s.AuthToken = null;
            AccountSeed.RestoreSession();            // no token, no provider: stays signed out
            Assert.Null(CoreAccount.UnifiedUserId);
            Assert.False(CoreAccount.IsLoggedIn);

            s.AuthToken = "tok";
            AccountSeed.RestoreSession();
            Assert.Equal("u-1", CoreAccount.UnifiedUserId);

            await AccountSeed.ValidateRestoredSessionAsync(new V2AuthService(() => s, new Answer(HttpStatusCode.NotFound)));
            Assert.Null(CoreAccount.UnifiedUserId);  // server does not know the id: signed out
            Assert.Null(s.UnifiedId);
        }
        finally { s.UnifiedId = oldId; s.AuthToken = null; }
    }

    /// <summary>The #4073 tier readings as this head applies them (AccountSeed.ApplyTierReading): one path for the
    /// profile load, the heartbeat and the focus refresh, rise AND lower, so a Linux payer unlocks and lapses the
    /// way WPF EntitlementTierSync does. Settings and the static events are put back afterwards.</summary>
    private async Task WithTierReadings(string uid, int storedTier, DateTime now, Func<AppSettings, List<string>, Task> body)
    {
        var s = CoreSettings.Current;
        var saved = (s.UnifiedId, s.AuthToken, s.OfflineMode, s.PatreonTier, s.PatreonPremiumValidUntil, s.PatreonLabValidUntil,
            s.ServerTierSeen, s.GraceClosedTier, s.GraceClosedAtUtc);
        var events = new List<string>();
        void Raised(int tier) => events.Add("raised:" + tier);
        void Closed() => events.Add("closed");
        var clock = AccountSeed.UtcNow;
        try
        {
            s.UnifiedId = uid; s.AuthToken = "test-token"; s.OfflineMode = false; s.PatreonTier = storedTier;
            s.ServerTierSeen = null; s.GraceClosedTier = null; s.GraceClosedAtUtc = null;
            s.PatreonPremiumValidUntil = storedTier >= 1 ? now.AddDays(13) : null;
            s.PatreonLabValidUntil = storedTier >= 2 ? now.AddDays(13) : null;
            AccountSeed.UtcNow = () => now;
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => s)));
            AccountSeed.Patreon!.TierChanged += (_, t) => events.Add("changed:" + (int)t);
            AccountSeed.TierRaised += Raised;
            AccountSeed.GraceClosed += Closed;
            await body(s, events);
        }
        finally
        {
            AccountSeed.TierRaised -= Raised;
            AccountSeed.GraceClosed -= Closed;
            AccountSeed.UtcNow = clock;
            (s.UnifiedId, s.AuthToken, s.OfflineMode, s.PatreonTier, s.PatreonPremiumValidUntil, s.PatreonLabValidUntil,
                s.ServerTierSeen, s.GraceClosedTier, s.GraceClosedAtUtc) = saved;
        }
    }

    private static string ProfileBody(int tier) => JsonConvert.SerializeObject(new { user = new { effective_tier = tier, patreon_tier = tier } });

    /// <summary>The readings' clock, ten minutes behind the real one: access reads the cached grace against the REAL
    /// clock (AppSettings.HasCachedPremiumAccess), so a stamp a close sets to "now" must already be past, and a fixed
    /// date would make these tests expire with their 14-day stamps.</summary>
    private static DateTime ReadingClock() => DateTime.UtcNow.AddMinutes(-10);

    [Fact]
    public Task Heartbeat_tier_is_ignored_after_account_or_token_changes() => WithTierReadings("heartbeat-user", 0, ReadingClock(), (s, events) =>
    {
        const string body = "{\"effective_tier\":2}";
        AccountSeed.ApplyHeartbeatTier("other", s.AuthToken, body);
        AccountSeed.ApplyHeartbeatTier(s.UnifiedId!, "old-token", body);
        s.OfflineMode = true;
        AccountSeed.ApplyHeartbeatTier(s.UnifiedId!, s.AuthToken, body);
        Assert.Equal(0, s.PatreonTier);
        Assert.Empty(events);
        s.OfflineMode = false;
        AccountSeed.ApplyHeartbeatTier(s.UnifiedId!, s.AuthToken, body);
        Assert.Equal(2, s.PatreonTier);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_rise_from_the_heartbeat_raises_TierRaised_before_TierChanged() => WithTierReadings("rising", 0, ReadingClock(), (s, events) =>
    {
        Assert.False(CoreAccount.HasPremiumAccess);
        AccountSeed.ApplyTierReading(2, "heartbeat");
        Assert.Equal(new[] { "raised:2", "changed:" + (int)PatreonTier.Level2 }, events);   // WPF: the fanfare, then TierChanged
        Assert.Equal(2, s.PatreonTier);
        Assert.Equal(AccountSeed.UtcNow().AddDays(EntitlementTierRule.GraceDays), s.PatreonLabValidUntil);
        Assert.True(CoreAccount.HasLabAccess);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task A_focus_refresh_renews_an_expired_grace_at_the_same_tier_and_is_throttled(int tier)
    {
        var start = ReadingClock();
        var now = start;
        return WithTierReadings("returning-" + tier, tier, start, async (s, events) =>
        {
            AccountSeed.UtcNow = () => now;
            s.PatreonPremiumValidUntil = s.PatreonLabValidUntil = start.AddDays(-1);   // a payer away over 14 days
            Assert.False(CoreAccount.HasPremiumAccess);
            var answer = new Answer(HttpStatusCode.OK, ProfileBody(tier));
            var client = new V2AuthService(() => s, answer);

            await AccountSeed.OnAppFocusedAsync(client);

            Assert.Equal(1, answer.Requests);
            Assert.Equal(tier, s.PatreonTier);
            Assert.Equal(start.AddDays(EntitlementTierRule.GraceDays), s.PatreonPremiumValidUntil);   // #4073 RenewGrace, from now
            Assert.True(CoreAccount.HasPremiumAccess);
            Assert.Equal(tier == 2, CoreAccount.HasLabAccess);
            // The doors repaint (TierChanged) but nothing celebrates: the same tier is not a purchase.
            Assert.Equal(new[] { "changed:" + (int)(tier >= 2 ? PatreonTier.Level2 : PatreonTier.Level1) }, events);

            now = start.AddSeconds(19);
            await AccountSeed.OnAppFocusedAsync(client);
            Assert.Equal(1, answer.Requests);               // inside FocusCheckGap: no read
            now = start.AddSeconds(20);
            await AccountSeed.OnAppFocusedAsync(client);
            Assert.Equal(2, answer.Requests);
        });
    }

    [Fact]
    public Task A_lower_reading_confirmed_90_s_later_lapses_the_tier_as_WPF_does()
    {
        var start = ReadingClock();
        var now = start;
        return WithTierReadings("lapsing", 2, start, async (s, events) =>
        {
            AccountSeed.UtcNow = () => now;
            Assert.True(CoreAccount.HasLabAccess);

            // The heartbeat says 0: remembered for the lapse rows, nothing closes on one reading.
            AccountSeed.ApplyTierReading(0, "heartbeat");
            Assert.Equal(2, s.PatreonTier);
            Assert.Equal(0, s.ServerTierSeen);
            Assert.True(CoreAccount.HasLabAccess);
            Assert.Empty(events);

            now = start.AddSeconds(89);
            AccountSeed.ApplyTierReading(0, "heartbeat");
            Assert.True(CoreAccount.HasLabAccess);
            Assert.Empty(events);

            // The second look lands while a session runs: the close waits for it (WPF HoldGraceClose).
            now = start.AddSeconds(90);
            var oldSession = CoreSession.IsSessionRunningProvider;
            CoreSession.IsSessionRunningProvider = () => true;
            try { AccountSeed.ApplyTierReading(0, "heartbeat"); }
            finally { CoreSession.IsSessionRunningProvider = oldSession; }
            Assert.Equal(2, s.PatreonTier);
            Assert.True(CoreAccount.HasLabAccess);
            Assert.Empty(events);

            // The same reading from the FOCUS refresh once the session ended: the one path closes the grace, as WPF does.
            now = start.AddSeconds(95);
            var answer = new Answer(HttpStatusCode.OK, ProfileBody(0));
            await AccountSeed.OnAppFocusedAsync(new V2AuthService(() => s, answer));

            Assert.Equal(1, answer.Requests);
            Assert.Equal(new[] { "closed" }, events);       // GraceClosed repaints; never TierChanged (no celebration)
            Assert.Equal(0, s.PatreonTier);
            Assert.Equal(2, s.GraceClosedTier);
            Assert.Equal(now, s.GraceClosedAtUtc);
            Assert.Equal(now, s.PatreonPremiumValidUntil);
            Assert.Equal(now, s.PatreonLabValidUntil);
            Assert.Null(s.ServerTierSeen);
            Assert.False(CoreAccount.HasPremiumAccess);
            Assert.False(CoreAccount.HasLabAccess);

            // Paying again is an ordinary rise.
            now = start.AddSeconds(120);
            AccountSeed.ApplyTierReading(2, "heartbeat");
            Assert.Equal(new[] { "closed", "raised:2", "changed:" + (int)PatreonTier.Level2 }, events);
            Assert.True(CoreAccount.HasLabAccess);
            Assert.Null(s.GraceClosedTier);
        });
    }

    /// <summary>WPF EntitlementTierSync.OfferPatreon: a fresh server Patreon confirmation re-stamps a Discord sign-in
    /// patron's lapsed grace from the confirmation (never from now), with no effective_tier to lean on.</summary>
    [Fact]
    public Task A_fresh_Patreon_confirmation_restamps_a_lapsed_grace_from_the_confirmation()
    {
        var start = ReadingClock();
        return WithTierReadings("discord-patron", 1, start, async (s, events) =>
        {
            s.PatreonPremiumValidUntil = start.AddDays(-1);
            Assert.False(CoreAccount.HasPremiumAccess);
            var confirmed = start.AddHours(-2);
            confirmed = confirmed.AddTicks(-(confirmed.Ticks % TimeSpan.TicksPerSecond));   // whole seconds on the wire
            var answer = new Answer(HttpStatusCode.OK, JsonConvert.SerializeObject(new
            {
                user = new { patreon_is_active = true, patreon_confirmed_at = confirmed.ToString("s") + "Z", patreon_tier = 1 },
            }));

            await AccountSeed.OnAppFocusedAsync(new V2AuthService(() => s, answer));

            Assert.Equal(1, answer.Requests);
            Assert.Equal(confirmed.AddDays(EntitlementTierRule.GraceDays), s.PatreonPremiumValidUntil);
            Assert.True(CoreAccount.HasPremiumAccess);
            Assert.Equal(1, s.PatreonTier);
            // Access came back: the doors repaint, nothing celebrates.
            Assert.Equal(new[] { "changed:" + (int)PatreonTier.Level1 }, events);

            // A stale confirmation (over 72 h) moves nothing.
            s.PatreonPremiumValidUntil = start.AddDays(-1);
            AccountSeed.ApplyPatreonReading(new PatreonActiveReading(true, start.AddDays(-4), 1), "profile");
            Assert.Equal(start.AddDays(-1), s.PatreonPremiumValidUntil);
            Assert.False(CoreAccount.HasPremiumAccess);
        });
    }

    [Fact]
    public Task A_focus_read_applies_the_member_fields_on_the_same_request() => WithTierReadings("free-member", 0, ReadingClock(), async (s, events) =>
    {
        var saved = (s.PatreonMember, s.ServerPaidUntil, s.PostGrants);
        var applied = 0;
        void Applied() => applied++;
        AccountSeed.ProfileExtrasApplied += Applied;
        try
        {
            s.PatreonMember = false;
            s.ServerPaidUntil = DateTime.UtcNow;
            s.PostGrants = new Dictionary<string, string>();
            var answer = new Answer(HttpStatusCode.OK, JsonConvert.SerializeObject(new
            {
                user = new { effective_tier = 0, patreon_member = true, paid_until = (string?)null, post_grants = new { voice = "2099-01-01T00:00:00Z" } },
            }));

            await AccountSeed.OnAppFocusedAsync(new V2AuthService(() => s, answer));

            Assert.Equal(1, answer.Requests);   // the tier and the member fields ride one read (no second focus poll)
            Assert.True(s.PatreonMember);
            Assert.Null(s.ServerPaidUntil);
            Assert.True(ConditioningControlPanel.Services.Startup.Conversion.ProfileExtras.GrantOpen(s.PostGrants, "voice", DateTime.UtcNow));
            Assert.Equal(1, applied);
            // A field the server did not send keeps what is stored (older server).
            AccountSeed.ApplyProfileExtras(ConditioningControlPanel.Services.Startup.Conversion.ProfileExtras.Parse(new Newtonsoft.Json.Linq.JObject()));
            Assert.True(s.PatreonMember);
            // Signed out: nothing moves.
            s.AuthToken = null;
            AccountSeed.ApplyProfileExtras(ConditioningControlPanel.Services.Startup.Conversion.ProfileExtras.Parse(
                Newtonsoft.Json.Linq.JObject.Parse("{\"patreon_member\":false}")));
            Assert.True(s.PatreonMember);
            Assert.Equal(2, applied);
        }
        finally
        {
            AccountSeed.ProfileExtrasApplied -= Applied;
            (s.PatreonMember, s.ServerPaidUntil, s.PostGrants) = saved;
        }
    });

    private sealed class Answer(HttpStatusCode status, string body = "{}") : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
