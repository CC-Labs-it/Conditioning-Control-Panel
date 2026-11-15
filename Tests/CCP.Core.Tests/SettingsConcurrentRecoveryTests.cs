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
    public async Task LoadingWhileAnotherInstancePublishesDoesNotDeleteItsTempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var readerStarted = new ManualResetEventSlim();
        Task<bool>? write = null;
        Task<SettingsService>? read = null;
        try
        {
            var writer = new SettingsService(path);
            writer.Current.DeeperEditorTimelineHeight = 160;
            Assert.True(writer.TrySaveImmediate(suppressCloudBackup: true));
            writer.Current.DeeperEditorTimelineHeight = 225;
            writer.BeforeAtomicPublishForTest = () => { ready.Set(); release.Wait(); };
            write = Task.Run(() => writer.TrySaveImmediate(suppressCloudBackup: true));
            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
            read = Task.Run(() => { readerStarted.Set(); return new SettingsService(path); });
            Assert.True(readerStarted.Wait(TimeSpan.FromSeconds(10)));
            // Give the loader an opportunity to run while the completed temp is still unpublished.
            await Task.WhenAny(read, Task.Delay(500));
            release.Set();
            Assert.True(await write.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(225, (await read.WaitAsync(TimeSpan.FromSeconds(10))).Current.DeeperEditorTimelineHeight);
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
        }
        finally
        {
            release.Set();
            if (write != null) await write;
            if (read != null) await read;
            Directory.Delete(dir, true);
        }
    }
}
