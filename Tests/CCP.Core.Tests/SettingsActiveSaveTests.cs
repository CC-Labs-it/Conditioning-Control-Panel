using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SettingsActiveSaveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadingDuringSaveDoesNotDeleteTheActiveTemporaryFile(bool existingFile)
    {
        var dir = Directory.CreateTempSubdirectory("settings-active-save-").FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var writer = new SettingsService(path);
            if (existingFile) Assert.True(writer.TrySaveImmediate(suppressCloudBackup: true));
            writer.Current.DeeperEditorTimelineHeight = 225;
            writer.BeforeAtomicPublishForTests = tempPath => _ = new SettingsService(path);
            Assert.True(writer.TrySaveImmediate(suppressCloudBackup: true));
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
    [Fact]
    public void InterruptedSaveIsStillRecovered()
    {
        var dir = Directory.CreateTempSubdirectory("settings-recovery-").FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var writer = new SettingsService(path);
            writer.Current.DeeperEditorTimelineHeight = 225;
            Assert.True(writer.TrySaveImmediate(suppressCloudBackup: true));
            File.Move(path, path + ".interrupted.tmp");
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".interrupted.tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
