using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;
namespace CCP.Core.Tests;
public sealed class EmiSayFramesTests
{
    [Fact]
    public void SpeechHasThreeDotsThenReactionThenClearWithTheExactWpfTiming()
    {
        var frames = EmiSayFrames.Create("pin a card", "^_~", 100);
        Assert.Equal(new[] { ".", "..", "...", "pin a card", null }, frames.Select(f => f.Bubble));
        Assert.Equal(new[] { 420, 420, 520, 400, 200 }, frames.Select(f => f.Ms));
        Assert.Equal(new[] { "0_0", "0_0", "0_0", "^_~", "0_0" }, frames.Select(f => f.Face));
        Assert.Equal(4050, EmiSayFrames.HoldMs(null));
        Assert.Equal(7990, EmiSayFrames.HoldMs(new string('a', 100)));
    }
}
