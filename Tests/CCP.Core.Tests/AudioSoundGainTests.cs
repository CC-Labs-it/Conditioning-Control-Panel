using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AudioSoundGainTests
{
    [Theory]
    [InlineData(100, 0, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(100, 50, 0.3535534)]
    [InlineData(100, 1, 0.05)]
    [InlineData(50, 50, 0.125)]
    [InlineData(200, 200, 1)]
    [InlineData(100, -1, 0)]
    public void SoundGainMatchesWpf(int level, int master, double expected) =>
        Assert.Equal(expected, CoreAudio.SoundGain(level, master), 6);
}
