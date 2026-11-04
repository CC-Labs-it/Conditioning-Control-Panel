using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using Xunit;
namespace CCP.Core.Tests;
[Collection(CoreSecretsStatics.Name)]
public sealed class LobbyRemoteTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public string? Path, Caller, Token, Body, Version, Agent;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Response = "{}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Path = request.Method + " " + request.RequestUri!.AbsolutePath;
            Caller = request.Headers.GetValues("X-Caller-Unified-Id").First();
            Token = request.Headers.GetValues("X-Auth-Token").First();
            Version = request.Headers.TryGetValues("X-Client-Version", out var versions) ? versions.First() : null;
            Agent = request.Headers.UserAgent.ToString();
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Response) };
        }
    }
    private sealed class Timer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void OnUiThread(Action action) => action();
    }
    [Fact]
    public async Task DirectoryUsesAccountHeadersAndMapsOpenAndClaimedSeats()
    {
        var h = new Handler { Response = "{\"entries\":[{\"unified_id\":\"u_a\",\"display_name\":\"Ann\",\"level\":4,\"tier\":\"deep\",\"tags\":[\"trance\"],\"claimed\":false},{\"unified_id\":\"u_b\",\"claimed\":true}]}" };
        using var http = new HttpClient(h);
        var api = new RemoteLobbyApi(http, () => ("u_caller", "test-token"), "http://localhost");
        var result = await api.FetchAsync();
        Assert.True(result.Ok);
        Assert.Equal("GET /v2/directory/list", h.Path);
        Assert.Equal("u_caller", h.Caller);
        Assert.Equal("test-token", h.Token);
        Assert.Equal(CoreReleaseContent.AppVersion, h.Version);
        Assert.Equal("ConditioningControlPanel/" + CoreReleaseContent.AppVersion, h.Agent);
        Assert.Equal("Ann", result.Seats[0].Name);
        Assert.Equal(4, result.Seats[0].Level);
        Assert.Equal("deep", result.Seats[0].Tier);
        Assert.Equal("trance", Assert.Single(result.Seats[0].Tags));
        Assert.True(result.Seats[1].Claimed);
        Assert.Equal("Anonymous", result.Seats[1].Name);
        var signedOut = new RemoteLobbyApi(http, () => null, "http://localhost");
        Assert.False((await signedOut.FetchAsync()).Ok);
        Assert.Null((await signedOut.TryClaimAsync("u_a")).Url);
        Assert.Equal(1, h.Calls);
    }
    [Fact]
    public async Task ClaimUsesSubjectInBodyAndRefreshesOnSuccessOrOrdinaryConflict()
    {
        var h = new Handler { Response = "{\"session_url\":\"https://example.test/#pin\"}" };
        using var http = new HttpClient(h);
        var api = new RemoteLobbyApi(http, () => ("u_caller", "test-token"), "http://localhost");
        var success = await api.TryClaimAsync("u_subject");
        Assert.Equal("POST /v2/directory/claim", h.Path);
        Assert.Equal("u_caller", h.Caller);
        Assert.Equal("test-token", h.Token);
        Assert.Equal("{\"unified_id\":\"u_subject\"}", h.Body);
        Assert.Equal("https://example.test/#pin", success.Url);
        Assert.True(success.Refresh);
        h.Status = HttpStatusCode.Conflict; h.Response = "{\"error\":\"taken\"}";
        var conflict = await api.TryClaimAsync("u_subject");
        Assert.Null(conflict.Url);
        Assert.True(conflict.Refresh);
        h.Status = HttpStatusCode.InternalServerError;
        Assert.False((await api.TryClaimAsync("u_subject")).Refresh);
        // A merged-account conflict must not be treated as a taken table.
        try
        {
            MergedAccountRecovery.SettingsForTest = () => new AppSettings { UnifiedId = "u_canon456" };
            h.Status = HttpStatusCode.Conflict;
            h.Response = "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon456\"}";
            Assert.False((await api.TryClaimAsync("u_subject")).Refresh);
        }
        finally { MergedAccountRecovery.ResetForTest(); }
    }
    [Fact]
    public async Task RemoteSuccessFeedsMergeAndFailureKeepsLastDirectoryRows()
    {
        using var service = new LobbyService(new Timer()) { SignedIn = () => true };
        service.FetchChess = _ => Task.FromResult(PbpLobbyReply.Empty);
        service.FetchGoon = _ => Task.FromResult(OpenTablesReply.Empty);
        service.FetchRace = _ => Task.FromResult(RaceOpenReply.Empty);
        service.FetchRemote = _ => Task.FromResult<(IReadOnlyList<RemoteSeat>, bool)>((new[] {
            new RemoteSeat("u_open", "Open", 2, "light", Array.Empty<string>(), false),
            new RemoteSeat("u_taken", "Taken", 3, "deep", Array.Empty<string>(), true) }, true));
        await service.RefreshAsync();
        Assert.True(service.LastRoundOk);
        Assert.Equal("u_open", Assert.Single(service.Snapshot.Open).Key);
        Assert.Equal("Taken", Assert.Single(service.Snapshot.Playing).HostName);
        service.FetchRemote = _ => Task.FromResult<(IReadOnlyList<RemoteSeat>, bool)>((Array.Empty<RemoteSeat>(), false));
        await service.RefreshAsync();
        Assert.False(service.LastRoundOk);
        Assert.Equal("u_open", Assert.Single(service.Snapshot.Open).Key);
        Assert.Equal("Taken", Assert.Single(service.Snapshot.Playing).HostName);
    }
    [Fact]
    public async Task ClaimRefreshWaitsForTheCurrentRoundThenStartsAFreshOne()
    {
        using var service = new LobbyService(new Timer()) { SignedIn = () => true };
        var first = new TaskCompletionSource<PbpLobbyReply>();
        var started = new TaskCompletionSource();
        var secondStarted = new TaskCompletionSource();
        var second = new TaskCompletionSource<PbpLobbyReply>();
        int rounds = 0;
        service.FetchChess = _ => { rounds++; if (rounds == 1) { started.TrySetResult(); return first.Task; } secondStarted.TrySetResult(); return second.Task; };
        service.FetchGoon = _ => Task.FromResult(OpenTablesReply.Empty);
        service.FetchRace = _ => Task.FromResult(RaceOpenReply.Empty);
        service.FetchRemote = _ => Task.FromResult<(IReadOnlyList<RemoteSeat>, bool)>((Array.Empty<RemoteSeat>(), false));
        service.ClaimRemote = _ => Task.FromResult<(string?, bool)>((null, true));
        var prior = service.RefreshAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var claim = service.ClaimRemoteAsync("u_subject");
        Assert.Null(await claim); // the browser must not wait for the old round
        first.SetResult(PbpLobbyReply.Empty);
        await prior;
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, rounds);
        var refreshed = service.RefreshAsync();
        second.SetResult(PbpLobbyReply.Empty);
        await refreshed;
    }
}
