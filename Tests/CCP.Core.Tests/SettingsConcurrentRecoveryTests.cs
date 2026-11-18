using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SettingsConcurrentRecoveryTests
{
    [Fact]
    public async Task LoadingDoesNotDeleteAnInFlightSaveFromAnotherInstance()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        var temp = path + ".writer.tmp";
        Task<SettingsService>? loading = null;
        using var started = new ManualResetEventSlim();
        try
        {
            var writer = new SettingsService(path);
            writer.Current.DeeperEditorTimelineHeight = 160;
            Assert.True(writer.TrySaveImmediate());
            var gate = typeof(SettingsService).GetField("_saveLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(writer)!;
            lock (gate)
            {
                // The writer has flushed its temp and has not published it yet.
                File.WriteAllText(temp, "{\"DeeperEditorTimelineHeight\":225}");
                loading = Task.Run(() => { started.Set(); return new SettingsService(path); });
                Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
                Assert.False(loading.Wait(TimeSpan.FromMilliseconds(500)), "recovery ran while the save was publishing");
                Assert.True(File.Exists(temp));
                File.Move(temp, path, overwrite: true);
            }
            var loaded = await loading.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(225, loaded.Current.DeeperEditorTimelineHeight);
        }
        finally
        {
            if (loading != null) await loading.WaitAsync(TimeSpan.FromSeconds(10));
            Directory.Delete(dir, recursive: true);
        }
    }
    [Fact]
    public void LoadingStillRecoversAnInterruptedSave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        var temp = path + ".interrupted.tmp";
        try
        {
            File.WriteAllText(temp, "{\"DeeperEditorTimelineHeight\":225}");
            var loaded = new SettingsService(path);
            Assert.Equal(225, loaded.Current.DeeperEditorTimelineHeight);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(temp));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

}
