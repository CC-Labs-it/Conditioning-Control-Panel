using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CompanionVoiceLinesTests
{
    [Fact]
    public void PackagedVoiceFilesKeepWpfExtensionsAndFilenameOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-voice-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "resources", "sounds", "flashes_audio");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in new[] { "z.mp3", "B.wav", "a.ogg", "c.flac", "ignored.txt" })
                File.WriteAllBytes(Path.Combine(dir, name), Array.Empty<byte>());
            Assert.Equal(new[] { "a.ogg", "B.wav", "c.flac", "z.mp3" },
                CompanionVoiceLines.FindFiles("test-mod", root).ConvertAll(Path.GetFileName));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MigrationWaitsForAudioPreservesNumericFilenamesAndDropsMissingIndices()
    {
        var disabled = new HashSet<string> { "VoiceLine:0", "VoiceLine:99" };
        var removed = new HashSet<string> { "VoiceLine:1", "Idle:0" };
        Assert.False(CompanionVoiceLines.MigrateIds(new(), disabled, removed));
        Assert.Contains("VoiceLine:99", disabled);
        var files = new List<string> { "12.mp3", "hello.wav" };
        disabled.Add("VoiceLine:12");
        Assert.True(CompanionVoiceLines.MigrateIds(files, disabled, removed));
        Assert.Equal(new HashSet<string> { "VoiceLine:12" }, disabled);
        Assert.Contains("VoiceLine:hello", removed);
        Assert.Contains("Idle:0", removed);
        Assert.False(CompanionVoiceLines.MigrateIds(files, disabled, removed));
    }
}
