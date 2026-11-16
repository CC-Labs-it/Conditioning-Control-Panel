using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LockdownDoseIsolationTests
{
    [Theory]
    [InlineData(false, "none")]
    [InlineData(true, "none")]
    [InlineData(false, "leave")]
    [InlineData(false, "stop")]
    [InlineData(false, "panic")]
    public Task KeeperRegressionLeavesCallerSettingsAndFileUntouched(bool keeperFirst, string interruption) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var dir = Directory.CreateTempSubdirectory("ccp-dose-caller-").FullName;
        var path = Path.Combine(dir, "settings.json");
        var caller = new SettingsService(path);
        Func<SettingsService?> provider = () => caller;
        try
        {
            CoreSettings.ServiceProvider = provider;
            var before = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            caller.Current.TotalSessions = 41;
            caller.Current.RecentSessionStartsUtc = new List<DateTime> { before };
            caller.Current.LastSessionModId = "sentinel";
            caller.Current.SameModRun = 7;
            caller.SaveImmediate();
            var saved = File.ReadAllBytes(path);
            await new LockdownDoseEngineTests().KeeperDoesNotSupersedeRemoteSessionWaitingForPortal(keeperFirst, interruption);
            Assert.Multiple(
                () => Assert.Same(provider, CoreSettings.ServiceProvider),
                () => Assert.Equal(41, caller.Current.TotalSessions),
                () => Assert.Equal(new[] { before }, caller.Current.RecentSessionStartsUtc),
                () => Assert.Equal("sentinel", caller.Current.LastSessionModId),
                () => Assert.Equal(7, caller.Current.SameModRun),
                () => Assert.Equal(saved, File.ReadAllBytes(path)));
        }
        finally
        {
            try { caller.SealForReset(); }
            finally { CoreSettings.ServiceProvider = oldProvider; Directory.Delete(dir, recursive: true); }
        }
    });
}
