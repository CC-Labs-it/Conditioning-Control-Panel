using System;
using System.Linq;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class InstallDateRecorderTests
{
    [Fact]
    public void UsesEarliestEvidenceAndKeepsExistingDate()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "exe");
        var logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(exe);
        Directory.CreateDirectory(logs);
        var path = Path.Combine(root, "settings.json");
        var log = Path.Combine(logs, "log.txt");
        File.WriteAllText(path, "{}");
        File.WriteAllText(log, "log");
        SettingsService? settings = null;
        try
        {
            var expected = new[] { File.GetCreationTimeUtc(path), Directory.GetCreationTimeUtc(exe), File.GetCreationTimeUtc(log) }.Min().Date;
            Assert.Equal(expected, InstallDateRecorder.Resolve(root, exe));
            settings = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
            Assert.Equal(Timeout.Infinite, settings.SaveDebounceDueTimeMilliseconds);
            settings.Current.InstallDate = null;
            InstallDateRecorder.EnsureRecorded(settings, root, exe);
            Assert.Equal(expected.ToString("yyyy-MM-dd"), settings.Current.InstallDate);
            Assert.True(settings.HasPendingSave);
            settings.Current.InstallDate = "2021-01-01";
            InstallDateRecorder.EnsureRecorded(settings, root, exe);
            Assert.Equal("2021-01-01", settings.Current.InstallDate);
        }
        finally
        {
            settings?.SealForReset();
            Directory.Delete(root, true);
        }
        Assert.False(settings.HasPendingSave);
    }
}
