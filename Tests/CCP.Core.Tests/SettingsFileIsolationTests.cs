using System;
using System.IO;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SettingsFileIsolationTests
{
    [Fact]
    public void SharedProfileSnapshotExcludesAtomicSaveFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-settings-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = Path.Combine(root, "settings.json");
            var backup = Path.Combine(root, "settings.json.bak");
            var temp = Path.Combine(root, "settings.json.123.tmp");
            var legacyTemp = Path.Combine(root, "settings.json.tmp");
            foreach (var file in new[] { settings, backup, temp, legacyTemp }) File.WriteAllText(file, "test");

            var files = RoadmapTestProfile.SettingsFiles(root);
            Assert.Equal(2, files.Length);
            Assert.Contains(settings, files);
            Assert.Contains(backup, files);
            Assert.DoesNotContain(temp, files);
            Assert.DoesNotContain(legacyTemp, files);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
