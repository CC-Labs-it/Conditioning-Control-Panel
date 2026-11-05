using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;
public class BrainDrainAudioPolicyTests
{
    [Fact]
    public void DiscoveryMergesPrimaryFirstWithoutMigratingOrRecursing()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var primary = Path.Combine(root, "assets"); var legacy = Path.Combine(root, "legacy");
        Directory.CreateDirectory(primary); Directory.CreateDirectory(legacy);
        try
        {
            File.WriteAllText(Path.Combine(primary, "same.MP3"), "");
            File.WriteAllText(Path.Combine(legacy, "SAME.mp3"), "");
            File.WriteAllText(Path.Combine(legacy, "other.ogg"), "");
            File.WriteAllText(Path.Combine(primary, "readme.txt"), "");
            Directory.CreateDirectory(Path.Combine(primary, "nested"));
            File.WriteAllText(Path.Combine(primary, "nested", "ignored.wav"), "");
            var clips = BrainDrainAudioPolicy.Discover(primary, legacy);
            Assert.Equal(2, clips.Length);
            Assert.Contains(Path.Combine(primary, "same.MP3"), clips);
            Assert.Contains(Path.Combine(legacy, "other.ogg"), clips);
            Assert.Equal(2, Directory.GetFiles(legacy).Length);
            Assert.Empty(BrainDrainAudioPolicy.Enumerate(Path.Combine(root, "absent")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false, 5)] [InlineData(true, 0.5)]
    public void CadenceAndProbabilityMatchWpf(bool high, double seconds)
    {
        var interval = BrainDrainAudioPolicy.Interval(high);
        Assert.Equal(seconds, interval.TotalSeconds);
        Assert.Equal(0.5 * seconds / 60, BrainDrainAudioPolicy.Probability(50, interval), 10);
    }
    [Theory]
    [InlineData(50, 40, 0.2)] [InlineData(-10, 100, 0)] [InlineData(200, 200, 1)]
    public void VolumeMultipliesAndClamps(int master, int own, double gain)
        => Assert.Equal(gain, BrainDrainAudioPolicy.Volume(master, own), 8);
}
