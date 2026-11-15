using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SettingsServiceConcurrentLoadTests
{
    [Fact]
    public async Task LoadingDuringSaveDoesNotDeleteTheActiveTemporaryFile()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-settings-concurrent-").FullName;
        var path = Path.Combine(dir, "settings.json");
        var writer = new SettingsService(path);
        writer.Current.DeeperEditorTimelineHeight = 160;
        Assert.True(writer.TrySaveImmediate(suppressCloudBackup: true));
        writer.Current.DeeperEditorTimelineHeight = 225;
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        writer.BeforeAtomicPublishForTest = temp =>
        {
            ready.TrySetResult(temp);
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("save was not released");
        };
        var save = Task.Run(() => writer.TrySaveImmediate(suppressCloudBackup: true));
        Task<SettingsService>? reader = null;
        try
        {
            var temp = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            reader = Task.Run(() => { started.TrySetResult(); return new SettingsService(path); });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var early = await Task.WhenAny(reader, Task.Delay(750)) == reader;
            var activeTempSurvived = File.Exists(temp);
            // A different profile must not wait behind this one's disk publication.
            var other = await Task.Run(() => new SettingsService(Path.Combine(dir, "other.json")))
                .WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(other.WasSettingsFileMissing);
            release.Set();
            Assert.True(await save.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.False(early);
            Assert.True(activeTempSurvived);
            Assert.Equal(225, (await reader.WaitAsync(TimeSpan.FromSeconds(15))).Current.DeeperEditorTimelineHeight);
        }
        finally
        {
            release.Set();
            try { await save.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
            if (reader != null) { try { await reader.WaitAsync(TimeSpan.FromSeconds(15)); } catch { } }
            Directory.Delete(dir, recursive: true);
        }
    }
}
