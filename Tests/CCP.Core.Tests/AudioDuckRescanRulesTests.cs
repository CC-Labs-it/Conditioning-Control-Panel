using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AudioDuckRescanRulesTests
{
    [Theory]
    [InlineData(0f, true)]
    [InlineData(0.0009f, true)]
    [InlineData(0.001f, true)]
    [InlineData(0.0011f, false)]
    [InlineData(1f, false)]
    public void SilentRescanSessionsAreNotRecapturedAsOriginals(float volume, bool silent)
        => Assert.Equal(silent, CoreAudio.IsSilentForDuckRescan(volume));
}
