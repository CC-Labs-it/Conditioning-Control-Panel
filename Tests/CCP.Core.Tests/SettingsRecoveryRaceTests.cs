using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SettingsRecoveryRaceTests
{
    [Fact]
    public async Task ReaderCannotDeleteAnotherServicesUnpublishedSave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var readerStarted = new ManualResetEventSlim();
        try
        {
            var writer = new SettingsService(path);
            Assert.True(writer.TrySaveImmediate(true));
            writer.Current.DeeperEditorTimelineHeight = 225;
            writer.BeforeAtomicPublishForTests = _ => { ready.Set(); release.Wait(TimeSpan.FromSeconds(10)); };
            var save = Task.Run(() => writer.TrySaveImmediate(true));
            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
            var read = Task.Run(() => { readerStarted.Set(); return new SettingsService(path); });
            Assert.True(readerStarted.Wait(TimeSpan.FromSeconds(10)));
            await Task.WhenAny(read, Task.Delay(1000));
            release.Set();
            Assert.True(await save);
            await read;
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
        }
        finally { release.Set(); try { Directory.Delete(dir, true); } catch { } }
    }
}
