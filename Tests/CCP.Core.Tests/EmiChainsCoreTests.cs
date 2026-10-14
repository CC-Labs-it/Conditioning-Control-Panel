using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// EmiChains' data and rules, now in Core. The art-on-disk half (BodyPath / OverPath finding the
/// sheets beside the exe) stays pinned by the WPF suite, which ships the art; these are the
/// lookups and builders that need no file.
/// </summary>
public class EmiChainsCoreTests
{
    [Fact]
    public void EveryChainPoseIsOnTheSheetAndEveryChainHasFrames()
    {
        foreach (var (id, chain) in EmiChains.Chains)
        {
            Assert.Equal(id, chain.Id);
            Assert.NotEmpty(chain.Seq);
            Assert.Equal(chain.BodyFrame, EmiChains.FrameKey(chain.BodyFrame));
        }
        Assert.All(EmiChains.FaceBodyFrame.Values, f => Assert.NotNull(EmiChains.FrameKey(f)));
        Assert.All(EmiChains.SwayCycle, f => Assert.NotNull(EmiChains.FrameKey(f)));
    }

    [Fact]
    public void LookupsNeverThrowOnJunk()
    {
        Assert.Null(EmiChains.Get(null));
        Assert.Null(EmiChains.Get("no-such-chain"));
        Assert.Equal("wink", EmiChains.Get("wink")!.Id);
        Assert.Null(EmiChains.FrameKey("body.png"));
        Assert.Equal("idle", EmiChains.FrameForFace(null));
        Assert.Equal("idle", EmiChains.FrameForFace("unpaired"));
        Assert.Equal("sad", EmiChains.FrameForFace("T_T"));
    }

    [Fact]
    public void OutfitGateOnlyAdmitsShippedNames()
    {
        Assert.Null(EmiChains.OutfitName(null));
        Assert.Null(EmiChains.OutfitName("../../escape"));
        Assert.Null(EmiChains.OutfitName("swim/../.."));
        Assert.Equal("swim", EmiChains.OutfitName(" swim "));
        Assert.Equal(string.Empty, EmiChains.OutfitDir("no-such-outfit"));
        Assert.Equal("over-body-sad.png", EmiChains.OverFileName("sad"));
        Assert.Equal("over-body-idle.png", EmiChains.OverFileName("junk"));
        Assert.Null(EmiChains.OverPath(null, "idle"));
    }

    [Fact]
    public void MakeSayRidesTheLockedCadence()
    {
        var say = EmiChains.MakeSay("hi", "", 10);
        Assert.Equal(new[] { 420, 420, 520, 400, 200 }, say.Seq.Select(f => f.Ms));
        Assert.Equal(new[] { ".", "..", "...", "hi", null }, say.Seq.Select(f => f.Bubble));
        Assert.All(say.Seq, f => Assert.True(f.SetsBubble));
        Assert.Equal("^_^", say.Seq[3].Text);
        Assert.Equal(EmiChains.RestFace, say.Seq[4].Text);
    }

    [Fact]
    public void SayHoldIsTheCampusFormula()
    {
        Assert.Equal(4050, EmiChains.SayHoldMs(null));
        Assert.Equal(4050, EmiChains.SayHoldMs(new string('x', 35)));
        Assert.Equal(1890 + 61 * 100, EmiChains.SayHoldMs(new string('x', 100)));
    }
}
