using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmiSpeechTests
{
    [Fact]
    public void LockedCadenceKeepsWordsInBubbleAndLandsReaction()
    {
        var frames = EmiSpeech.MakeSay("hello", "^_~", 1200).Seq;
        Assert.Equal(new[] { ".", "..", "...", "hello", null }, frames.Select(f => f.Bubble));
        Assert.Equal(new[] { "0_0", "0_0", "0_0", "^_~", "0_0" }, frames.Select(f => f.Text));
        Assert.Equal(new[] { 420, 420, 520, 1200, 200 }, frames.Select(f => f.Ms));
        Assert.All(frames, f => Assert.True(f.SetsBubble));
    }

    [Theory]
    [InlineData(null, 4050)]
    [InlineData("", 4050)]
    [InlineData("hello", 4050)]
    public void ShortLinesKeepThe4050MillisecondFloor(string? text, int expected)
        => Assert.Equal(expected, EmiSpeech.SayHoldMs(text));

    [Fact]
    public void LongLinesGrowWithoutCeilingAndEmptyFaceFallsBack()
    {
        Assert.Equal(141092, EmiSpeech.SayHoldMs(new string('x', 2282)));
        var landed = EmiSpeech.MakeSay(null, "", 1).Seq[3];
        Assert.Equal("^_^", landed.Text);
        Assert.Equal("", landed.Bubble);
        Assert.Equal(400, landed.Ms);
    }
}
