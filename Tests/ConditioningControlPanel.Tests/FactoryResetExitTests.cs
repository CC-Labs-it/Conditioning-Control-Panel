using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Diagnostics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The factory reset (Settings, Data, Danger Zone) called Environment.Exit on the UI thread: the
/// same freeze as ccp-bugs #1468 (ProcessExit's SystemEvents.Shutdown waits on a Send to that
/// thread). It now leaves through UiThreadExit, with every deletion done first and settings sealed
/// for the whole exit window, and a reset during a session no longer leaves the session journal
/// behind to write the old values back at the next launch.
/// </summary>
public sealed class FactoryResetExitTests
{
    [Fact]
    public void Sweep_removes_temps_marker_and_both_session_journals_and_nothing_else()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-reset-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string[] gone =
            {
                "settings.json.0a1b.tmp", "settings.json.tmp", "settings.json.restored",
                SessionSettingsJournal.FileName, SessionSettingsJournal.RampFileName,
                ConditioningControlPanel.Services.Remote.RemoteHoldings.FileName, "lockdown_recovery.json",
            };
            string[] kept = { "settings.pre-reset-20261022_010101.json", "settings.bak-1.json", "achievements.json" };
            foreach (var name in gone.Concat(kept)) File.WriteAllText(Path.Combine(dir, name), "{}");

            Assert.Equal(gone.Length, FactoryResetSweep.Leftovers(dir).Count);
            Assert.Empty(FactoryResetSweep.Delete(dir));

            foreach (var name in gone) Assert.False(File.Exists(Path.Combine(dir, name)), name);
            foreach (var name in kept) Assert.True(File.Exists(Path.Combine(dir, name)), name);
            Assert.Empty(FactoryResetSweep.Leftovers(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Sweep_of_a_missing_folder_is_quiet()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-reset-none-" + Guid.NewGuid().ToString("N"));
        Assert.Empty(FactoryResetSweep.Leftovers(dir));
        Assert.Empty(FactoryResetSweep.Delete(dir));
    }

    [Fact]
    public void Relaunch_waits_longer_than_the_exit_window()
    {
        Assert.True(FactoryResetSweep.RelaunchDelaySeconds * 1000 > UiThreadExit.GraceMs);
    }

    [Fact]
    public void Reset_seals_sweeps_then_leaves_off_the_ui_thread()
    {
        var src = File.ReadAllText(Path.Combine(SourceRoot(), "Views", "Controls", "AppSettings", "DataSettingsSection.xaml.cs"));
        int start = src.IndexOf("private void PerformFactoryReset()", StringComparison.Ordinal);
        Assert.True(start >= 0, "PerformFactoryReset not found");
        int end = src.IndexOf("private static void LogSweep(", start, StringComparison.Ordinal);
        Assert.True(end > start, "PerformFactoryReset end not found");
        var body = src.Substring(start, end - start);

        Assert.DoesNotContain("Environment.Exit(", body);
        var seal = body.IndexOf("App.Settings?.SealForReset();", StringComparison.Ordinal);
        var move = body.IndexOf("File.Move(settingsPath, backupPath", StringComparison.Ordinal);
        var lastSweep = body.LastIndexOf("FactoryResetSweep.Delete(dir", StringComparison.Ordinal);
        var exit = body.IndexOf("UiThreadExit.Begin(", StringComparison.Ordinal);
        Assert.True(seal >= 0 && move > seal && lastSweep > move && exit > lastSweep,
            "seal, move aside, sweep, then exit");
        Assert.Contains("FactoryResetSweep.RelaunchDelaySeconds", src);
    }

    [Fact]
    public void Seal_waits_out_a_write_in_flight_and_the_write_rechecks_it()
    {
        var src = SourceRoots.ReadProductFile("Services", "Settings", "SettingsService.cs");   // CCP.Core since the split
        int seal = src.IndexOf("public void SealForReset()", StringComparison.Ordinal);
        int sealEnd = src.IndexOf("public void UnsealAfterFailedReset()", seal, StringComparison.Ordinal);
        Assert.Contains("lock (_saveLock) { }", src.Substring(seal, sealEnd - seal));

        int save = src.IndexOf("public void SaveImmediate(", StringComparison.Ordinal);
        int writeLock = src.IndexOf("lock (_saveLock)", save, StringComparison.Ordinal);
        int recheck = src.IndexOf("if (_sealed) return;", writeLock, StringComparison.Ordinal);
        int write = src.IndexOf("File.Move(tempPath, _settingsPath", writeLock, StringComparison.Ordinal);
        Assert.True(writeLock > save && recheck > writeLock && write > recheck, "the write re-checks the seal inside the lock");
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("ConditioningControlPanel project not found above " + AppContext.BaseDirectory);
    }
}
