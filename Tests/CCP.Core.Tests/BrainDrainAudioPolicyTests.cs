using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;
// WPF Services/LockCard/BrainDrainService.cs:103-114,162-181.
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
}
