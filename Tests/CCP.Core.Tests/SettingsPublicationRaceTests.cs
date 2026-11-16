using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class SettingsPublicationRaceTests
{
    [Fact]
    public async Task LoadingDuringPublicationDoesNotDeleteLiveTempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-race-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(dir);
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            var writer = new SettingsService(path);
            writer.Current.DeeperEditorTimelineHeight = 160;
            Assert.True(writer.TrySaveImmediate(true));
            writer.Current.DeeperEditorTimelineHeight = 225;
            writer.BeforePublishForTest = () => { ready.Set(); release.Wait(TimeSpan.FromSeconds(10)); };
            var save = Task.Run(() => writer.TrySaveImmediate(true));
            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
            var readerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var read = Task.Run(() => { readerStarted.SetResult(); return new SettingsService(path); });
            await readerStarted.Task;
            // Let the reader reach recovery while the writer's completed temp is still unpublished.
            await Task.WhenAny(read, Task.Delay(500));
            release.Set();
            Assert.True(await save);
            await read;
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
        }
        finally { release.Set(); Directory.Delete(dir, true); }
    }
}
