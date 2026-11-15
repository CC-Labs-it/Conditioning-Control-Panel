using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SettingsConcurrentRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadingDuringAnAtomicSaveDoesNotStealItsTemporaryFile(bool hasExistingSettings)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var writer = new SettingsService(path);
        Task<bool>? save = null;
        try
        {
            try
            {
                if (hasExistingSettings) Assert.True(writer.TrySaveImmediate());
                writer.Current.DeeperEditorTimelineHeight = 225;
                writer.BeforeAtomicPublishForTests = () =>
                {
                    ready.TrySetResult(true);
                    if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("reader did not finish");
                };
                save = Task.Run(() => writer.TrySaveImmediate());
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                var temp = Assert.Single(Directory.GetFiles(dir, "settings.json.*.tmp"));

                var reader = new SettingsService(path);
                Assert.Equal(160, reader.Current.DeeperEditorTimelineHeight);
                Assert.Equal(hasExistingSettings, File.Exists(path));
                Assert.True(File.Exists(temp), "loading removed the active save");
            }
            finally
            {
                release.Set();
                if (save != null) await save;
                writer.SealForReset();
            }
            Assert.True(await save!);
            Assert.Equal(225, new SettingsService(path).Current.DeeperEditorTimelineHeight);
            Assert.Empty(Directory.GetFiles(dir, "settings.json.*.tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData(false, 230)]
    [InlineData(true, 190)]
    public void AbandonedTempIsRecoveredOrDiscarded(bool hasExistingSettings, int expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var temp = path + ".abandoned.tmp";
            if (hasExistingSettings) File.WriteAllText(path, "{\"DeeperEditorTimelineHeight\":190}");
            File.WriteAllText(temp, "{\"DeeperEditorTimelineHeight\":230}");
            Assert.Equal(expected, new SettingsService(path).Current.DeeperEditorTimelineHeight);
            Assert.False(File.Exists(temp));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void FailedPublicationDoesNotLeaveTheTempMarkedActive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-settings-failed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var writer = new SettingsService(path);
            string? temp = null;
            writer.BeforeAtomicPublishForTests = () =>
            {
                temp = Assert.Single(Directory.GetFiles(dir, "settings.json.*.tmp"));
                throw new IOException("simulated publication failure");
            };
            Assert.False(writer.TrySaveImmediate());
            Assert.NotNull(temp);
            File.WriteAllText(temp!, "{\"DeeperEditorTimelineHeight\":230}");
            Assert.Equal(230, new SettingsService(path).Current.DeeperEditorTimelineHeight);
            Assert.False(File.Exists(temp));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

}
