using System;
using System.IO;
using ConditioningControlPanel.Services.Companion.Rain;
using Xunit;

namespace CCP.Core.Tests;

public class RainPackPlaybackTests
{
    [Fact]
    public void CoreDoesNotShadowWpfPlaybackType()
    {
        Assert.Null(typeof(RainPackPlayback).Assembly.GetType("ConditioningControlPanel.Services.Companion.RainPackPlayback"));
    }

    [Fact]
    public void V2UsesReadablePackAndTrimsHash()
    {
        var pack = Path.GetFullPath(Path.Combine("rain-packs", "abc"));
        Assert.Equal(pack, RainPackPlayback.ResolveTubeFolder(" V2 ", "rain_emotes", " abc ",
            hash => { Assert.Equal("abc", hash); return pack; }, () => throw new Exception("V1 was queried")));
    }

    [Fact]
    public void V1AndModsWithoutV2NeverQueryTheStore()
    {
        var calls = 0;
        string? Fail(string _) { calls++; return null; }
        Assert.Equal("v1", RainPackPlayback.ResolveTubeFolder("v1", "rain_emotes", "abc", Fail, () => "v1"));
        Assert.Equal("v1", RainPackPlayback.ResolveTubeFolder("v2", null, "abc", Fail, () => "v1"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void MissingOrBrokenPackFallsBackToEmbeddedV2()
    {
        var calls = 0;
        foreach (var hash in new string?[] { null, "  " })
            Assert.Equal("rain_emotes", RainPackPlayback.ResolveTubeFolder("v2", "rain_emotes", hash,
                _ => { calls++; return null; }, () => "v1"));
        Assert.Equal(0, calls);
        foreach (var path in new string?[] { null, "", "relative" })
            Assert.Equal("rain_emotes", RainPackPlayback.ResolveTubeFolder("v2", "rain_emotes", "abc", _ => path, () => "v1"));
        Assert.Equal("rain_emotes", RainPackPlayback.ResolveTubeFolder("v2", "rain_emotes", "abc",
            _ => throw new IOException("broken pack"), () => "v1"));
    }
}
