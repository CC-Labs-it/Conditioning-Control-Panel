using System;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Startup.Conversion;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public class PostCodeFlowTests : IDisposable
{
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private string? _token;
    public PostCodeFlowTests()
    {
        CoreSecrets.RetrieveProvider = _ => _token;
        CoreSecrets.StoreProvider = (_, value) => _token = value;
    }
    public void Dispose() { CoreSecrets.RetrieveProvider = _oldGet; CoreSecrets.StoreProvider = _oldSet; }

    private static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private static AppSettings Member() => new() { PatreonMember = true, AuthToken = "test" };
    private static PostCodeLive Live => new(true, "dtrh", Now.AddDays(1), false);

    [Fact]
    public async Task PollWaitsThirtyMinutesAndNeverOverlaps()
    {
        var flow = new PostCodeFlow();
        var s = Member();
        var pending = new TaskCompletionSource<PostCodeLive?>();
        int calls = 0;
        Task<PostCodeLive?> Fetch() { calls++; return pending.Task; }
        var first = flow.CheckAsync(s, Now, Fetch);
        Assert.Null(await flow.CheckAsync(s, Now.AddHours(1), Fetch));
        pending.SetResult(Live);
        Assert.Equal(Live, await first);
        Assert.Null(await flow.CheckAsync(s, Now.AddMinutes(29), Fetch));
        Assert.Equal(Live, await flow.CheckAsync(s, Now.AddMinutes(30), Fetch));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false, false, "test")]
    [InlineData(true, true, "test")]
    [InlineData(true, false, "")]
    public async Task PollRequiresOnlineMemberToken(bool member, bool offline, string token)
    {
        var s = new AppSettings { PatreonMember = member, OfflineMode = offline, AuthToken = token };
        Assert.Null(await new PostCodeFlow().CheckAsync(s, Now, () => throw new Exception("must not send")));
    }

    [Fact]
    public void RowOnlyForUnheldUnredeemedLiveFeatureOncePerDay()
    {
        var s = Member();
        Assert.False(PostCodeFlow.StampRow(s, Live, true, Now));
        Assert.False(PostCodeFlow.StampRow(s, Live with { Redeemed = true }, false, Now));
        Assert.False(PostCodeFlow.StampRow(s, Live with { Live = false }, false, Now));
        Assert.True(PostCodeFlow.StampRow(s, Live, false, Now));
        Assert.False(PostCodeFlow.StampRow(s, Live, false, Now));
        Assert.True(PostCodeFlow.StampRow(s, Live, false, Now.AddDays(1)));
    }

    [Fact]
    public async Task RedeemSerializesPressesAndSavesWholeGrantBeforeRepaint()
    {
        var s = Member();
        var original = s.PostGrants;
        var flow = new PostCodeFlow();
        var pending = new TaskCompletionSource<PostCodeRedeem>();
        int calls = 0, saves = 0, paints = 0;
        Task<PostCodeRedeem> Send(string? code) { calls++; return pending.Task; }
        var first = flow.RedeemAsync(s, "CODE", Send, () => saves++, () => { Assert.Equal(1, saves); paints++; });
        Assert.Null(await flow.RedeemAsync(s, "CODE", Send, () => saves++, () => paints++).WaitAsync(TimeSpan.FromSeconds(3)));
        pending.SetResult(new(true, "dtrh", Now.AddDays(1), null));
        Assert.True((await first)!.Ok);
        Assert.Equal(1, calls);
        Assert.Equal(1, paints);
        Assert.NotSame(original, s.PostGrants);
        Assert.True(ProfileExtras.GrantOpen(s.PostGrants, "dtrh", Now));
        Assert.False(ProfileExtras.GrantOpen(s.PostGrants, "dtrh", Now.AddDays(2)));
    }

    [Fact]
    public async Task InvalidOrRefusedCodeDoesNotSaveAGrant()
    {
        var s = Member();
        var flow = new PostCodeFlow();
        void Unexpected() => throw new Exception("must not save or repaint");
        var invalid = await flow.RedeemAsync(s, " ", _ => throw new Exception("must not send"), Unexpected, Unexpected);
        Assert.Equal("bad_code", invalid!.Reason);
        var refused = await flow.RedeemAsync(s, "CODE", _ => Task.FromResult(new PostCodeRedeem(false, null, null, "expired")), Unexpected, Unexpected);
        Assert.Equal("expired", refused!.Reason);
        Assert.Empty(s.PostGrants);
    }
}
