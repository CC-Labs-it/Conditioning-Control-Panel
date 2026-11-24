using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EngineOffAudioLineTests
{
    [Fact]
    public void GatesAndRateLimitArePerPlayerAndAllowClockReversal()
    {
        var trace = new EngineOffAudioLine();
        var now = new DateTime(2026, 1, 1);
        Assert.Null(trace.LineFor("a", "x", true, false, now));
        Assert.Null(trace.LineFor("a", "x", false, true, now));
        Assert.Equal("engine off: a started 'x'", trace.LineFor("a", "x", false, false, now));
        Assert.Null(trace.LineFor("a", "x", false, false, now.AddSeconds(9)));
        Assert.NotNull(trace.LineFor("b", "x", false, false, now.AddSeconds(9)));
        Assert.NotNull(trace.LineFor("a", "x", false, false, now.AddSeconds(10)));
        Assert.NotNull(trace.LineFor("a", "x", false, false, now));
    }

    [Theory]
    [InlineData(null, "?")]
    [InlineData(" ", "?")]
    [InlineData("/private/clip.wav", "clip.wav")]
    [InlineData("C:\\private\\clip.wav\\", "clip.wav")]
    [InlineData("///", "?")]
    public void NamesNeverContainDirectories(string? path, string expected) => Assert.Equal(expected, EngineOffAudioLine.NameOf(path));

    [Fact]
    public void NoteTracesOnceAndAlwaysRecordsEvenWhenSinkThrows()
    {
        var lines = new List<string>();
        var ring = new RecentSounds { TraceSink = lines.Add };
        var now = new DateTime(2026, 1, 1);
        ring.Note("a", "/private/x.wav", false, false, now);
        ring.Note("a", "/private/y.wav", false, false, now.AddSeconds(1));
        ring.Note("b", "z.wav", false, false, now.AddSeconds(2));
        Assert.Equal(new[] { "engine off: a started 'x.wav'", "engine off: b started 'z.wav'" }, lines);
        ring.TraceSink = _ => throw new InvalidOperationException();
        ring.Note("c", "q.wav", false, false, now.AddSeconds(3));
        var entries = ring.Snapshot();
        Assert.Equal(3, entries.Count);
        Assert.Equal(2, entries[0].Count);
        Assert.Equal("c", entries[2].Player);
    }
}
