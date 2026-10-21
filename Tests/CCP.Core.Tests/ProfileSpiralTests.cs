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
