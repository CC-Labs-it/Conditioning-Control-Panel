using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SettingsPublicationConcurrencyTests
{
    [Fact]
    public async Task LoadingDoesNotDeleteAnActiveSaveButStillRemovesAnOrphan()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-settings-publication-").FullName;
        var path = Path.Combine(dir, "settings.json");
        using var release = new ManualResetEventSlim();
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new SettingsService(path);
        writer.Current.DeeperEditorTimelineHeight = 160;
        Assert.True(writer.TrySaveImmediate(suppressCloudBackup: true));
        writer.Current.DeeperEditorTimelineHeight = 225;
        writer.BeforeAtomicPublishForTests = temp => { ready.TrySetResult(temp); release.Wait(); };
        var save = Task.Run(() => writer.TrySaveImmediate(suppressCloudBackup: true));
        try
        {
            var active = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var orphan = path + ".orphan.tmp";
            File.WriteAllText(orphan, "orphan");
            Assert.Equal(160, new SettingsService(path).Current.DeeperEditorTimelineHeight);
            Assert.True(File.Exists(active), "a live save's temp must survive another load");
            Assert.False(File.Exists(orphan), "abandoned temps must still be cleaned");
            release.Set();
            Assert.True(await save.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
        }
        finally
        {
            release.Set();
            await save.WaitAsync(TimeSpan.FromSeconds(10));
            writer.SealForReset();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OpenSettingsReaderAllowsAtomicReplacement()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-settings-reader-").FullName;
        var path = Path.Combine(dir, "settings.json");
        var writer = new SettingsService(path);
        try
        {
            writer.Current.DeeperEditorTimelineHeight = 160;
            Assert.True(writer.TrySaveImmediate(suppressCloudBackup: true));
            using (var reader = SettingsService.OpenSettingsRead(path))
            {
                writer.Current.DeeperEditorTimelineHeight = 225;
                Assert.True(writer.TrySaveImmediate(suppressCloudBackup: true));
            }
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
        }
        finally { writer.SealForReset(); Directory.Delete(dir, recursive: true); }
    }
}
