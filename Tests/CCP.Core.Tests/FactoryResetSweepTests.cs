using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Main baf86f0ac + b2317b818: the factory reset (both heads' Danger Zone) also removes every file that
/// hands old settings back at the next launch (the session / ramp journals, the remote and Lockdown
/// recovery files), the settings seal waits out a write in flight and the write re-checks it, and the
/// relaunch waits past the exit window.
/// </summary>
public sealed class FactoryResetSweepTests
{
    [Fact]
    public void Sweep_removes_temps_marker_journals_and_recovery_files_and_nothing_else()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-reset-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string[] gone =
            {
                "settings.json.0a1b.tmp", "settings.json.tmp", "settings.json.restored",
                "session_restore.json", "ramp_restore.json",
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
    public void The_journal_names_match_the_journal_and_lockdowns_file()
    {
        // Literal in the sweep so it does not wait on the SessionSettingsJournal port: once that type is
        // in Core its constants must say the same.
        var journal = typeof(FactoryResetSweep).Assembly.GetType("ConditioningControlPanel.Services.SessionSettingsJournal");
        if (journal != null)
        {
            Assert.Equal(FactoryResetSweep.SessionJournalFileName, journal.GetField("FileName")?.GetValue(null));
            Assert.Equal(FactoryResetSweep.RampJournalFileName, journal.GetField("RampFileName")?.GetValue(null));
        }
        var ld = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "Haptics", "LockdownService.cs"));
        Assert.Contains("\"" + FactoryResetSweep.LockdownRecoveryFileName + "\"", ld);
    }

    [Fact]
    public void Relaunch_waits_longer_than_the_exit_window()
        => Assert.True(FactoryResetSweep.RelaunchDelaySeconds * 1000 > FactoryResetSweep.ExitGraceMs);

    [Fact]
    public void Seal_waits_out_a_write_in_flight_and_the_write_rechecks_it()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "Settings", "SettingsService.cs"));
        int seal = src.IndexOf("public void SealForReset()", StringComparison.Ordinal);
        int sealEnd = src.IndexOf("public void UnsealAfterFailedReset()", seal, StringComparison.Ordinal);
        Assert.Contains("lock (_saveLock) { }", src.Substring(seal, sealEnd - seal));

        int save = src.IndexOf("public void SaveImmediate(", StringComparison.Ordinal);
        int writeLock = src.IndexOf("lock (_saveLock)", save, StringComparison.Ordinal);
        int recheck = src.IndexOf("if (_sealed) return;", writeLock, StringComparison.Ordinal);
        int write = src.IndexOf("File.Move(", writeLock, StringComparison.Ordinal);
        Assert.True(writeLock > save && recheck > writeLock && write > recheck, "the write re-checks the seal inside the lock");
    }

    [Theory]
    [InlineData("ConditioningControlPanel/Views/Controls/AppSettings/DataSettingsSection.xaml.cs", "private void PerformFactoryReset()")]
    [InlineData("CCP.Avalonia/Views/Controls/AppSettings/DataSettingsSection.axaml.cs", "private bool PerformFactoryReset(out string failure)")]
    public void Both_heads_seal_move_then_sweep_and_relaunch_past_the_exit_window(string file, string signature)
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), file));
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        int end = src.IndexOf("private static void LogSweep(", start, StringComparison.Ordinal);
        Assert.True(end > start, "LogSweep follows PerformFactoryReset");
        var body = src.Substring(start, end - start);
        var seal = body.IndexOf("SealForReset();", StringComparison.Ordinal);
        var move = body.IndexOf("File.Move(settingsPath, backupPath", StringComparison.Ordinal);
        var lastSweep = body.LastIndexOf("FactoryResetSweep.Delete(dir", StringComparison.Ordinal);
        Assert.True(seal >= 0 && move > seal && lastSweep > move, "seal, move aside, then sweep");
        // baf86f0ac: disable the window, then leave off the UI thread; never Environment.Exit on it.
        var disable = body.IndexOf("host.IsEnabled = false", StringComparison.Ordinal);
        var exit = body.IndexOf("UiThreadExit.Begin(0)", StringComparison.Ordinal);
        Assert.True(disable > lastSweep && exit > disable, "sweep, disable the window, then exit off the UI thread");
        Assert.DoesNotContain("Environment.Exit(", body);
        Assert.DoesNotContain("SafeGlob(", src);
        Assert.Contains("FactoryResetSweep.RelaunchDelaySeconds", src);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CCP.Core", "CCP.Core.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root above " + AppContext.BaseDirectory);
    }
}
