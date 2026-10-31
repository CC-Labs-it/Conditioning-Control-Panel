using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class Sync7bGentlePulseTests
{
    [Fact]
    public void GentlePulseTakesThreeHundredMillisecondsToRiseAndKeepsItsFall()
    {
        var gentle = ChaosFxEnvelope.Pulse(0, 1, gentle: true);
        Assert.Equal(0.36, gentle.Sample(150), 10);
        Assert.Equal(0.72, gentle.Sample(300), 10);
        Assert.Equal(0.36, gentle.Sample(430), 10);
        Assert.Equal(0, gentle.Sample(560));
        var normal = ChaosFxEnvelope.Pulse(0, 1);
        Assert.Equal(0.72, normal.Sample(40), 10);
        Assert.Equal(300, normal.DurationMs);
    }
}
