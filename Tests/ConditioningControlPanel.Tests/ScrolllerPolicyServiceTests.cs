using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScrolllerPolicyCollection
{
    public const string Name = "ScrolllerPolicy";
}

/// <summary>
/// The Scrolller licence switch (GET /config/scrolller, 2026-10-18): the body parse, the apply
/// through ScrolllerPolicy (cap clamp) and the url gate the native fetchers use. Runs alone:
/// ScrolllerPolicy is process-wide, and every test puts the shipped default back.
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]
public sealed class ScrolllerPolicyServiceTests : System.IDisposable
{
    public void Dispose() => ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));

    [Fact]
    public void Parse_ServerDefault_IsOnWithNoCap()
    {
        var a = ScrolllerPolicyService.Parse("{\"enabled\":true,\"max_clip_short_side\":null,\"min_version\":null}");
        Assert.Equal(new ScrolllerPolicyService.Answer(true, null, null), a);
    }

    [Fact]
    public void Parse_FullAnswer()
    {
        var a = ScrolllerPolicyService.Parse("{\"enabled\":false,\"max_clip_short_side\":480,\"min_version\":\" 7.1.12 \"}");
        Assert.Equal(new ScrolllerPolicyService.Answer(false, 480, "7.1.12"), a);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>502</html>")]
    [InlineData("[1,2]")]
    public void Parse_Garbage_IsNoAnswer(string? body) => Assert.Null(ScrolllerPolicyService.Parse(body));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"enabled\":\"false\"}")]
    [InlineData("{\"enabled\":0}")]
    [InlineData("{\"enabled\":null}")]
    public void Parse_OnlyAnExplicitFalseSwitchesOff(string body) => Assert.True(ScrolllerPolicyService.Parse(body)!.Enabled);

    [Theory]
    [InlineData("{\"max_clip_short_side\":\"480\"}")]
    [InlineData("{\"max_clip_short_side\":480.5}")]
    [InlineData("{\"max_clip_short_side\":-1}")]
    public void Parse_BadCap_IsNoCap(string body) => Assert.Null(ScrolllerPolicyService.Parse(body)!.MaxClipShortSide);

    [Fact]
    public void Parse_BadVersion_IsNoMinimum()
    {
        Assert.Null(ScrolllerPolicyService.Parse("{\"min_version\":7.1}")!.MinVersion);
        Assert.Null(ScrolllerPolicyService.Parse("{\"min_version\":\"  \"}")!.MinVersion);
    }

    [Fact]
    public void Parse_OfficialTrue_IsOfficial()
        => Assert.True(ScrolllerPolicyService.Parse("{\"enabled\":true,\"official\":true}")!.Official);

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"official\":false}")]
    [InlineData("{\"official\":\"true\"}")]
    [InlineData("{\"official\":1}")]
    [InlineData("{\"official\":null}")]
    [InlineData("{\"official\":{}}")]
    public void Parse_OnlyAnExplicitTrueIsOfficial(string body) => Assert.False(ScrolllerPolicyService.Parse(body)!.Official);

    [Fact]
    public void Accept_SetsOfficial_AndAnAnswerWithoutItClearsIt()
    {
        Assert.False(ScrolllerPolicy.Official);
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null, Official: true));
        Assert.True(ScrolllerPolicy.Official);
        ScrolllerPolicyService.Accept(ScrolllerPolicyService.Parse("{\"enabled\":true}")!);
        Assert.False(ScrolllerPolicy.Official);
    }

    [Fact]
    public void Accept_RaisesTheAllowanceChangedSignal_WithOfficialAlreadySet()
    {
        bool? seen = null;
        void OnChanged(object? s, System.EventArgs e) => seen = ScrolllerPolicy.Official;
        ScrolllerAllowance.Changed += OnChanged;
        try { ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null, Official: true)); }
        finally { ScrolllerAllowance.Changed -= OnChanged; }
        Assert.True(seen);
    }

    [Fact]
    public void Accept_SetsSwitchCapAndMinVersion()
    {
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, 480, "7.2.0"));
        Assert.False(ScrolllerPolicy.Enabled);
        Assert.Equal(480, ScrolllerPolicy.MaxClipShortSide);
        Assert.Equal("7.2.0", ScrolllerPolicyService.MinVersion);
    }

    [Theory]
    [InlineData(null, 720)]     // no cap = the shipped 720
    [InlineData(1080, 720)]     // the server can only lower it
    [InlineData(100, 240)]      // floor
    [InlineData(360, 360)]
    public void Accept_CapIsClamped(int? cap, int expected)
    {
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, cap, null));
        Assert.Equal(expected, ScrolllerPolicy.MaxClipShortSide);
    }

    [Fact]
    public void Blocks_OnlyScrolllerHosts_OnlyWhileOff()
    {
        const string media = "https://v.scrolller.com/x-854x480.mp4";
        Assert.False(ScrolllerPolicyService.Blocks(media));

        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));
        Assert.True(ScrolllerPolicyService.Blocks(media));
        Assert.True(ScrolllerPolicyService.Blocks("https://scrolller.com/r/x"));
        Assert.False(ScrolllerPolicyService.Blocks("https://notscrolller.com/a.mp4"));
        Assert.False(ScrolllerPolicyService.Blocks("https://cdn.example.com/a.mp4"));
        Assert.False(ScrolllerPolicyService.Blocks(null));
    }

    [Fact]
    public void Off_RemoteMediaCacheRefusesScrolller()
    {
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));
        // No network is touched: the gate answers before any download starts.
        var bytes = RemoteMediaCache.PrefetchAsync("https://v.scrolller.com/x-854x480.mp4", default).GetAwaiter().GetResult();
        Assert.False(bytes);
    }

    [Fact]
    public void Off_ProbeSaysOffline()
    {
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));
        var probe = FypOnlineCoordinator.ProbeSubAsync("EroticHypnosis", default).GetAwaiter().GetResult();
        Assert.False(probe.Ok);
        Assert.Equal("offline", probe.Error);
    }
}
