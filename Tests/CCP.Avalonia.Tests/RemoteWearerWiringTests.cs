using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Port 7.2 fix wave, lane B: this head's Remote Control end paths follow main 4adc4c476 + 2a712d9da
/// through Core RemoteWearer (the rules are tested in CCP.Core.Tests/RemoteRelayTests). Pinned here: the
/// crash heal runs at launch AFTER Lockdown's own recovery, the relay builds with Core's executor and stop
/// (so every end path reaches RemoteWearer), and the leash cut drops the unpushed leash time.</summary>
public sealed class RemoteWearerWiringTests
{
    [Fact]
    public void The_crash_heal_runs_after_lockdowns_recovery()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "App.axaml.cs"));
        int recover = app.IndexOf("LockdownService.RecoverIfNeeded();", StringComparison.Ordinal);
        int heal = app.IndexOf("RemoteWearer.HealOnLaunch();", StringComparison.Ordinal);
        Assert.True(recover > 0 && heal > recover, "RemoteWearer.HealOnLaunch must run after LockdownService.RecoverIfNeeded");
    }

    [Fact]
    public void The_relay_runs_cores_executor_and_stop_so_every_end_path_hands_back()
    {
        var tab = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Views", "Tabs", "RemoteControlTabView.axaml.cs"));
        Assert.Contains("RemoteCommands.Execute, RemoteCommands.StopEffects", tab);
        var commands = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "RemoteControl", "RemoteCommands.cs"));
        var stop = commands.Substring(commands.IndexOf("public static void StopEffects(bool force)", StringComparison.Ordinal));
        Assert.Contains("Remote.RemoteWearer.ReleaseSafetyState();", stop);
        Assert.DoesNotContain("s.StrictLockEnabled = false;", stop);
    }

    [Fact]
    public void The_leash_cut_drops_the_unpushed_leash_time()
    {
        var leash = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Platform", "LeashHead.cs"));
        Assert.Contains("ChasterHead.Service?.DropUnpushedLeash()", leash);
        Assert.DoesNotContain("DropUnpushedLeashBookings() => false", leash);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
