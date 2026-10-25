using System;
using System.Net;
using ConditioningControlPanel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class ProfileSpiralTests : IDisposable
{
    private readonly Func<string, string?>? _get = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _set = CoreSecrets.StoreProvider;
    private string? _token;
    public ProfileSpiralTests()
    {
        CoreSecrets.RetrieveProvider = _ => _token;
        CoreSecrets.StoreProvider = (_, value) => _token = value;
    }
    public void Dispose()
    {
        CoreSecrets.RetrieveProvider = _get;
        CoreSecrets.StoreProvider = _set;
    }

    [Fact]
    public void DoorsRequireBlockAndAnsweredMigration_CardAlsoRequiresSelf()
    {
        var block = new DescentBlock { DevotionDays = 12 };
        Assert.False(ProfileSpiral.IsVisible(null, false));
        Assert.False(ProfileSpiral.IsVisible(block, true));
        Assert.False(ProfileSpiral.IsVisible(block, false, false));
        Assert.True(ProfileSpiral.IsVisible(block, false));
        var s = new AppSettings { DescentMigrationOffered = true };
        Assert.True(ProfileSpiral.IsWithheld(s));
        s.PendingDescentMigrationChoice = "invalid";
        Assert.True(ProfileSpiral.IsWithheld(s));
        s.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;
        Assert.False(ProfileSpiral.IsWithheld(s, true, true));
        s.PendingDescentMigrationChoice = null;
        s.DescentMigrationCompleted = true;
        Assert.False(ProfileSpiral.IsWithheld(s, true, true));
        Assert.False(ProfileSpiral.IsWithheld(null, true, true));
    }

    [Theory]
    [InlineData("clean", true)]
    [InlineData("dirty", false)]
    [InlineData("unproven", false)]
    [InlineData("season", false)]
    [InlineData("pending", false)]
    [InlineData("offline", false)]
    [InlineData("lower", false)]
    public void PollAdoptsOnlyProvenCleanSameSeasonLedger(string gate, bool adopts)
    {
        var s = new AppSettings { UnifiedId = "fake-user", CurrentSeason = "2026-08",
            PlayerLevel = 2, PlayerXP = 10 };
        var total = ProfileAdopt.TotalXp(s);
        s.LastConfirmedServerXpAccount = s.UnifiedId;
        s.LastConfirmedServerXpSeason = s.CurrentSeason;
        s.LastConfirmedServerXp = gate == "unproven" ? 0 : gate == "dirty" ? total - 1 : total;
        if (gate == "pending") s.PendingDescentMigrationChoice = "restore";
        s.OfflineMode = gate == "offline";
        var user = Newtonsoft.Json.Linq.JObject.FromObject(new { level = 3,
            xp = gate == "lower" ? total - 1 : total + 1000,
            current_season = gate == "season" ? "2026-07" : s.CurrentSeason });
        Assert.Equal(adopts, ProfileAdopt.AdoptProfilePoll(s, user));
        Assert.Equal(adopts ? 3 : 2, s.PlayerLevel);
        Assert.Equal(adopts ? total + 1000 : total, ProfileAdopt.TotalXp(s), 5);
    }

    [Fact]
    public void PollFollowsCurveBeforeComparingXp_AndMalformedReadDoesNothing()
    {
        var s = new AppSettings { UnifiedId = "fake-user", PlayerLevel = 2, PlayerXP = 10 };
        var total = ProfileAdopt.TotalXp(s);
        Assert.False(ProfileAdopt.AdoptProfilePoll(s, new Newtonsoft.Json.Linq.JObject()));
        Assert.True(ProfileAdopt.AdoptProfilePoll(s, Newtonsoft.Json.Linq.JObject.Parse(
            """{"level":2,"xp":1,"curve_epoch":1}""")));
        Assert.Equal(1, s.DescentEpoch);
        Assert.Equal(total, ProfileAdopt.TotalXp(s), 5);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AcceptedSyncRefreshesOnlyAnAlreadyLitReader(bool lit, bool accepted)
    {
        var old = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        CoreAccount.IsLoggedInProvider = () => true;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var s = service.Current;
            s.UnifiedId = "fake-user"; s.AuthToken = "fake-token"; s.OfflineMode = false;
            s.PlayerLevel = 2; s.PlayerXP = 10; s.LastConfirmedServerXp = 0;
            var wire = new Wire();
            var reader = Reader(s, wire);
            var now = DateTime.UtcNow;
            reader.UtcNow = () => now;
            if (lit) await reader.RefreshAsync("light");
            now = now.AddMinutes(1);
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            reader.BlockChanged += (_, _) => received.TrySetResult();
            var pushWire = new PushWire(accepted);
            var sync = new SyncPush(() => Array.Empty<string>(), () => false, pushWire) { DescentReader = reader };
            sync.MarkLoaded(Array.Empty<string>());
            Assert.Equal(accepted, await sync.PushAsync("test"));
            if (lit && accepted) await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            else Assert.False(received.Task.IsCompleted);
            Assert.Equal(lit ? accepted ? 2 : 1 : 0, wire.Calls);
        }
        finally { CoreSettings.ServiceProvider = old; CoreAccount.IsLoggedInProvider = oldLogin; }
    }

    private sealed class PushWire(bool accepted) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
                { Content = new StringContent("{}") });
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal int Calls;
        internal string Body = """{"user":{"descent":{"devotion_days":12}}}""";
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal TaskCompletionSource? Hold;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v2/user/profile", request.RequestUri!.AbsolutePath);
            if (Hold is not null) await Hold.Task;
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    private static DescentService Reader(AppSettings s, Wire wire) =>
        new(() => s, new V2AuthService(() => s, wire));

    [Fact]
    public async Task ReadOnlyFetchCoalesces_RetainsOnFailure_RetractsOnWithdrawal()
    {
        var s = new AppSettings { UnifiedId = "fake-user", AuthToken = "fake-token" };
        var wire = new Wire();
        var reader = Reader(s, wire);
        int changed = 0;
        reader.BlockChanged += (_, _) => changed++;
        Assert.True(await reader.RefreshAsync("card"));
        Assert.Equal(12, reader.Current!.DevotionDays);
        Assert.True(reader.HasSeenBlock);
        Assert.False(await reader.RefreshAsync("same credentials"));
        Assert.Equal(1, wire.Calls);
        wire.Status = HttpStatusCode.ServiceUnavailable;
        Assert.True(await reader.RefreshAsync("failed read", force: true));
        Assert.Equal(12, reader.Current!.DevotionDays);
        Assert.Equal(1, changed);
        wire.Status = HttpStatusCode.OK;
        wire.Body = """{"user":{}}""";
        Assert.True(await reader.RefreshAsync("withdrawn", force: true));
        Assert.Null(reader.Current);
        Assert.True(reader.HasSeenBlock);
        Assert.Equal(2, changed);
        reader.Reset();
        Assert.False(reader.HasSeenBlock);
    }

    [Fact]
    public async Task ResetDiscardsOldAccountsInFlightReply()
    {
        var s = new AppSettings { UnifiedId = "fake-a", AuthToken = "fake-token" };
        var wire = new Wire { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var reader = Reader(s, wire);
        var fetch = reader.RefreshAsync("old account");
        reader.Reset();
        s.UnifiedId = "fake-b";
        wire.Hold.SetResult();
        await fetch;
        Assert.Null(reader.Current);
        Assert.False(reader.HasSeenBlock);
    }

    [Fact]
    public async Task OfflineAndMissingCredentialsNeverReadTheProfile()
    {
        var s = new AppSettings { UnifiedId = "fake-user", AuthToken = "fake-token", OfflineMode = true };
        var wire = new Wire();
        var reader = Reader(s, wire);
        Assert.False(await reader.RefreshAsync("offline"));
        s.OfflineMode = false;
        s.AuthToken = null;
        Assert.False(await reader.RefreshAsync("before sign-in"));
        Assert.Equal(0, wire.Calls);
        s.AuthToken = "fake-token";
        Assert.True(await reader.RefreshAsync("signed in"));
        Assert.Equal(1, wire.Calls);
    }
}
