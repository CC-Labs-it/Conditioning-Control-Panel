using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SettingsConcurrentRecoveryTests
{
    [Fact]
    public async Task LoadingDuringPublicationDoesNotDeleteTheLiveSave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        var writer = new SettingsService(path);
        using var readerStarted = new ManualResetEventSlim();
        Task<SettingsService>? reader = null;
        try
        {
            writer.Current.DeeperEditorTimelineHeight = 160;
            Assert.True(writer.TrySaveImmediate(suppressCloudBackup: true));
            writer.Current.DeeperEditorTimelineHeight = 225;
            writer.BeforePublishForTest = () =>
            {
                reader = Task.Factory.StartNew(() =>
                {
                    readerStarted.Set();
                    return new SettingsService(path);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Assert.True(readerStarted.Wait(TimeSpan.FromSeconds(5)));
                // Allow recovery to run while the flushed save is awaiting publication.
                reader.Wait(TimeSpan.FromSeconds(2));
            };
            var saved = await Task.Run(() => writer.TrySaveImmediate(suppressCloudBackup: true));
            Assert.True(saved, "a concurrent settings load must not delete the live save");
            Assert.NotNull(reader);
            await reader!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
        }
        finally
        {
            writer.BeforePublishForTest = null;
            writer.SealForReset();
            if (reader != null) await reader.WaitAsync(TimeSpan.FromSeconds(10));
            Directory.Delete(dir, recursive: true);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryStillHandlesInterruptedTemps(bool mainFileMissing)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var writer = new SettingsService(path);
            writer.Current.DeeperEditorTimelineHeight = 225;
            Assert.True(writer.TrySaveImmediate(suppressCloudBackup: true));
            var temp = path + ".interrupted.tmp";
            File.Copy(path, temp);
            if (mainFileMissing) File.Delete(path);
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(temp));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
