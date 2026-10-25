using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner 2026-10-22: loading a preset (or a recalled config) during a Lockdown never changes the
/// panic key. Every built-in preset writes PanicKeyEnabled = true.
/// </summary>
public class LockdownPanicHoldTests
{
    [Theory]
    // lockdown, before, incoming, expected
    [InlineData(true, false, true, false)]   // Lockdown switched panic off: a preset cannot switch it on
    [InlineData(true, true, false, true)]    // nor off: Lockdown hands its own value back at the end
    [InlineData(true, true, true, true)]
    [InlineData(false, false, true, true)]   // no Lockdown: the preset wins
    [InlineData(false, true, false, false)]
    public void A_wholesale_write_keeps_the_panic_key_during_a_lockdown(bool lockdown, bool before, bool incoming, bool expected)
        => Assert.Equal(expected, LockdownPanicHold.Keep(lockdown, before, incoming));

    [Fact]
    public void Every_built_in_preset_writes_the_panic_key_on()
    {
        // Why the hold is needed: a preset load mid-Lockdown would switch the panic key back on.
        foreach (var preset in Preset.GetDefaultPresets())
        {
            var s = new AppSettings { PanicKeyEnabled = false };
            preset.ApplyTo(s);
            Assert.True(s.PanicKeyEnabled, preset.Name);
        }
    }

    [Fact]
    public void Outside_a_lockdown_the_restore_leaves_the_presets_value()
    {
        var s = new AppSettings { PanicKeyEnabled = true };
        LockdownPanicHold.RestoreAfterApply(s, panicBefore: false);   // no App.Lockdown in tests
        Assert.True(s.PanicKeyEnabled);
    }

    [Theory]
    [InlineData("MainWindow.Presets.cs", "private void LoadPreset(", "cur")]
    [InlineData("MainWindow.Remember.cs", "rc.Preset.ApplyTo(s);", "s")]
    public void Every_wholesale_preset_write_holds_the_panic_key(string file, string anchor, string target)
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", file))
            .Replace("\r\n", "\n");
        int at = src.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at >= 0, "missing: " + anchor);
        int apply = src.IndexOf($".ApplyTo({target});", Math.Max(0, at - 400), StringComparison.Ordinal);
        int before = src.LastIndexOf($"bool panicBefore = {target}.PanicKeyEnabled;", apply, StringComparison.Ordinal);
        int restore = src.IndexOf($"Services.LockdownPanicHold.RestoreAfterApply({target}, panicBefore);", apply, StringComparison.Ordinal);
        Assert.True(apply > 0 && before > 0 && apply - before < 300, "read the panic key just before the write");
        Assert.True(restore > apply && restore - apply < 300, "restore it right after the write");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
