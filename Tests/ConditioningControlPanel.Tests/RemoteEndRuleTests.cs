using System;
using System.IO;
using ConditioningControlPanel.Services.Remote;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1405: ending a Remote Control session while the subject's own preset or session ran
/// stopped every service under the engine and cleared the overlays, so the run read as stopped or
/// paused. The end of a remote session leaves a run the subject started alone. The leash cut and
/// the controller's panic keep their own rules.
/// </summary>
public class RemoteEndRuleTests
{
    [Fact]
    public void RemoteEnding_LeavesTheSubjectsOwnRunAlone()
        => Assert.True(RemoteEndRule.LeavesLocalRun(RemoteEndCause.SessionEnded,
            engineRunning: true, sessionRemoteStarted: false));

    [Fact]
    public void RemoteEnding_StillEndsARunTheControllerStarted()
        => Assert.False(RemoteEndRule.LeavesLocalRun(RemoteEndCause.SessionEnded,
            engineRunning: true, sessionRemoteStarted: true));

    [Fact]
    public void RemoteEnding_WithNoEngine_ClearsWhatTheControllerStarted()
        => Assert.False(RemoteEndRule.LeavesLocalRun(RemoteEndCause.SessionEnded,
            engineRunning: false, sessionRemoteStarted: false));

    [Theory]
    [InlineData(RemoteEndCause.LeashCut, true, false)]
    [InlineData(RemoteEndCause.LeashCut, true, true)]
    [InlineData(RemoteEndCause.Panic, true, false)]
    [InlineData(RemoteEndCause.Panic, true, true)]
    [InlineData(RemoteEndCause.Panic, false, false)]
    public void LeashCutAndPanic_KeepTheFullTeardown(RemoteEndCause cause, bool engine, bool remoteStarted)
        => Assert.False(RemoteEndRule.LeavesLocalRun(cause, engine, remoteStarted));

    [Fact]
    public void TheService_AsksTheRuleOnBothTeardowns_AndTheLeashCutSaysSo()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services",
            "RemoteControlService.cs"));

        // Both teardowns bail out before they stop a single service.
        var all = Body(src, "private void StopAllRemoteEffects(RemoteEndCause cause)");
        Assert.True(all.IndexOf("LeavesLocalRun(cause)", StringComparison.Ordinal) is >= 0 and var a
                    && a < all.IndexOf("App.KillAllAudio()", StringComparison.Ordinal),
            "StopAllRemoteEffects must ask the rule before it stops anything");
        var onLeave = Body(src, "private void StopRemoteTriggeredEffects()");
        Assert.True(onLeave.IndexOf("LeavesLocalRun(", StringComparison.Ordinal) is >= 0 and var b
                    && b < onLeave.IndexOf("App.Flash?.Stop()", StringComparison.Ordinal),
            "StopRemoteTriggeredEffects must ask the rule before it stops anything");

        // The leash cut names itself, panic names itself, nothing else does.
        Assert.Contains("CleanupSession(RemoteEndCause.LeashCut)", Body(src, "public void EndSessionNow()"));
        Assert.Contains("StopAllRemoteEffects(RemoteEndCause.Panic)", src);

        // The hand-back never reaches for the engine's own services.
        var release = Body(src, "private void ReleaseRemoteHoldings()");
        foreach (var banned in new[] { "KillAllAudio", "App.Flash", "App.Subliminal", "App.Bubbles",
                     "App.Video", "InteractionQueue", "PinkFilterEnabled", "SpiralEnabled", "StopSessionFromRemote" })
            Assert.DoesNotContain(banned, release);
        Assert.Contains("ReleaseRemoteSafetyState()", release);
    }

    /// <summary>The text of one method: from its signature to the next member's doc comment.</summary>
    private static string Body(string src, string signature)
    {
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "missing: " + signature);
        var end = src.IndexOf("\n        /// <summary>", start, StringComparison.Ordinal);
        var next = src.IndexOf("\n        private ", start + signature.Length, StringComparison.Ordinal);
        if (next >= 0 && (end < 0 || next < end)) end = next;
        return end < 0 ? src.Substring(start) : src.Substring(start, end - start);
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
