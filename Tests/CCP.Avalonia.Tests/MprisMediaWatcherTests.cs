using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Tmds.DBus.Protocol;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MprisMediaWatcherTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankTitleIsNotASession(string? title)
    {
        var metadata = new Dictionary<string, VariantValue>();
        if (title != null) metadata["xesam:title"] = title;
        Assert.Null(MprisMediaWatcher.FromMetadata(metadata, "Playing", 12));
    }

    [Fact]
    public void ConvertsMicrosecondsAndDefaultsOptionalMetadata()
    {
        var sample = MprisMediaWatcher.FromMetadata(new() { ["xesam:title"] = "Track" }, null, 12_345_678)!;
        Assert.Equal("Track", sample.Title);
        Assert.Null(sample.Artist);
        Assert.Equal("Unknown", sample.PlaybackState);
        Assert.Equal(TimeSpan.FromTicks(123_456_780), sample.Position);
    }

    [Theory]
    [InlineData(-1L, 0L)]
    [InlineData(long.MaxValue, 9223372036854775800L)]
    public void UntrustedPositionCannotOverflow(long position, long ticks) =>
        Assert.Equal(ticks, MprisMediaWatcher.FromMetadata(new() { ["xesam:title"] = "Track" }, "Playing", position)!.Position.Ticks);

    [Fact]
    public void MalformedMetadataIsAnAbsentSignal()
    {
        Assert.Null(MprisMediaWatcher.FromProperties(new()));
        Assert.Null(MprisMediaWatcher.FromProperties(new() { ["Metadata"] = true }));
        Assert.Null(MprisMediaWatcher.FromMetadata(new() { ["xesam:title"] = true }, "Playing", 0));
    }

    private static Dictionary<string, VariantValue> Props(string title, string state) => new()
    {
        ["Metadata"] = VariantValue.Variant(new Dict<string, VariantValue>
        {
            ["xesam:title"] = title,
            ["xesam:artist"] = VariantValue.Array(new[] { "Artist", "Other" })
        }),
        ["PlaybackStatus"] = VariantValue.Variant(state),
        ["Position"] = 2_000_000L
    };

    [Fact]
    public void ReadsNestedMprisMetadataAndOptionalFailuresIndependently()
    {
        var props = Props("Track", "Paused");
        var sample = MprisMediaWatcher.FromProperties(props)!;
        Assert.Equal("Artist", sample.Artist);
        Assert.Equal("Paused", sample.PlaybackState);
        Assert.Equal(TimeSpan.FromSeconds(2), sample.Position);
        props["PlaybackStatus"] = true;
        props["Position"] = "bad";
        sample = MprisMediaWatcher.FromProperties(props)!;
        Assert.Equal("Track", sample.Title);
        Assert.Equal("Unknown", sample.PlaybackState);
        Assert.Equal(TimeSpan.Zero, sample.Position);
    }

    [Fact]
    public async Task PrefersPlayingAndSurvivesVanishedPlayers()
    {
        var visited = new List<string>();
        var sample = await MprisMediaWatcher.ReadPlayersAsync(new[]
        {
            "org.mpris.MediaPlayer2.c", "unrelated", "org.mpris.MediaPlayer2.b", "org.mpris.MediaPlayer2.a"
        }, name =>
        {
            visited.Add(name);
            if (name.EndsWith(".a")) throw new InvalidOperationException("vanished");
            return Task.FromResult(Props(name, name.EndsWith(".c") ? "Playing" : "Paused"));
        });
        Assert.Equal(new[] { "org.mpris.MediaPlayer2.a", "org.mpris.MediaPlayer2.b", "org.mpris.MediaPlayer2.c" }, visited);
        Assert.Equal("org.mpris.MediaPlayer2.c", sample!.Title);
        sample = await MprisMediaWatcher.ReadPlayersAsync(new[] { "org.mpris.MediaPlayer2.b", "org.mpris.MediaPlayer2.a" },
            name => Task.FromResult(Props(name, "Paused")));
        Assert.Equal("org.mpris.MediaPlayer2.a", sample!.Title);
        Assert.Null(await MprisMediaWatcher.ReadPlayersAsync(new[] { "unrelated" }, _ => throw new Exception()));
    }

    [Fact]
    public async Task ConstructionAndSandboxStartNeverObserveDesktop()
    {
        Assert.True(SandboxNet.Active);
        using var watcher = new MprisMediaWatcher();
        Assert.False(watcher.IsAvailable);
        Assert.Null(watcher.Current);
        watcher.Start();
        await watcher.StartCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(watcher.IsAvailable);
        watcher.Stop();
        Assert.Null(watcher.Current);
        watcher.Dispose();
        watcher.Start();
        Assert.False(watcher.IsAvailable);
        Assert.Null(watcher.Current);
    }
}
