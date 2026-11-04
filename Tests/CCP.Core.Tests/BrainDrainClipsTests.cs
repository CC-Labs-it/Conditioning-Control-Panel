using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class BrainDrainClipsTests
{
    [Fact]
    public void ScanCreatesPrimaryMergesCaseInsensitiveNamesAndRescansWithoutMovingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-brain-" + Guid.NewGuid());
        var primary = Path.Combine(root, "assets", "braindrain");
        var legacy = Path.Combine(root, "legacy");
        try
        {
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "Track.MP3"), "old");
            File.WriteAllText(Path.Combine(legacy, "other.ogg"), "old");
            Assert.Equal(2, BrainDrainClips.Scan(primary, legacy).Clips.Length);
            Assert.True(Directory.Exists(primary));
            File.WriteAllText(Path.Combine(primary, "track.mp3"), "new");
            File.WriteAllText(Path.Combine(primary, "third.WAV"), "new");
            File.WriteAllText(Path.Combine(primary, "ignore.txt"), "no");
            Directory.CreateDirectory(Path.Combine(primary, "nested"));
            File.WriteAllText(Path.Combine(primary, "nested", "ignored.mp3"), "no");
            var scan = BrainDrainClips.Scan(primary, legacy);
            Assert.Equal(3, scan.Clips.Length);
            Assert.Equal(2, scan.LegacyCount);
            Assert.Contains(Path.Combine(primary, "track.mp3"), scan.Clips);
            Assert.DoesNotContain(Path.Combine(legacy, "Track.MP3"), scan.Clips);
            Assert.Equal("old", File.ReadAllText(Path.Combine(legacy, "Track.MP3")));
            File.Delete(Path.Combine(legacy, "other.ogg"));
            Assert.Equal(2, BrainDrainClips.Scan(primary, legacy).Clips.Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AudioPathTracksAssetsAndFallsBackToLegacyWhenResolutionThrows()
    {
        var previous = CorePaths.EffectiveAssetsProvider;
        try
        {
            var assets = Path.Combine(Path.GetTempPath(), "ccp-assets");
            CorePaths.EffectiveAssetsProvider = () => assets;
            Assert.Equal(Path.Combine(assets, "braindrain"), BrainDrainClips.AudioFolderPath);
            CorePaths.EffectiveAssetsProvider = () => throw new InvalidOperationException();
            Assert.Equal(BrainDrainClips.LegacyFolderPath, BrainDrainClips.AudioFolderPath);
        }
        finally { CorePaths.EffectiveAssetsProvider = previous; }
    }

    [Fact]
    public void UnavailablePrimaryDoesNotHideLegacyClips()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-brain-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            var primary = Path.Combine(root, "file"); File.WriteAllText(primary, "not a directory");
            var legacy = Path.Combine(root, "old"); Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "sound.wav"), "clip");
            var scan = BrainDrainClips.Scan(primary, legacy);
            Assert.Single(scan.Clips); Assert.Equal(1, scan.LegacyCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void LegacyOnlyCountDoesNotCreatePrimaryAndAddedCountExcludesDuplicates()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-brain-seam-" + Guid.NewGuid());
        var primary = Path.Combine(root, "primary"); var legacy = Path.Combine(root, "legacy");
        try
        {
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "same.MP3"), "old");
            File.WriteAllText(Path.Combine(legacy, "other.ogg"), "old");
            Assert.Equal(2, BrainDrainClips.Count(legacy));
            Assert.False(Directory.Exists(primary));
            BrainDrainClips.Scan(primary, legacy, out var missingAdded, ensurePrimary: false);
            Assert.Equal(2, missingAdded); Assert.False(Directory.Exists(primary));
            Directory.CreateDirectory(primary);
            File.WriteAllText(Path.Combine(primary, "same.mp3"), "new");
            var scan = BrainDrainClips.Scan(primary, legacy, out var added);
            Assert.Equal(2, scan.LegacyCount); Assert.Equal(1, added); Assert.Equal(2, scan.Clips.Length);
            BrainDrainClips.Scan(legacy, legacy, out var sameFolderAdded);
            Assert.Equal(0, sameFolderAdded);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

}
