using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SettingsConcurrentLoadTests
{
    [Fact]
    public async Task LoadingWhileSavePublishesDoesNotDeleteTheLiveTempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        using var flushed = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var writer = new SettingsService(path);
        Assert.True(writer.TrySaveImmediate(true));
        writer.Current.DeeperEditorTimelineHeight = 225;
        writer.BeforeAtomicPublishForTests = _ => { flushed.Set(); release.Wait(TimeSpan.FromSeconds(10)); };
        var saving = Task.Run(() => writer.TrySaveImmediate(true));
        try
        {
            Assert.True(flushed.Wait(TimeSpan.FromSeconds(10)));
            var readerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading = Task.Run(() => { readerStarted.SetResult(); return new SettingsService(path); });
            await readerStarted.Task;
            // Give the reader a chance to reach recovery while publication is paused.
            await Task.WhenAny(loading, Task.Delay(500));
            release.Set();
            Assert.True(await saving);
            await loading;
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
        }
        finally
        {
            release.Set();
            await saving;
            writer.SealForReset();
            Directory.Delete(dir, true);
        }
    }
}
