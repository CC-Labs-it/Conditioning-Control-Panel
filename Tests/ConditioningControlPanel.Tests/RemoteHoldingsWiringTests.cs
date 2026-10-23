using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Remote;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The WPF head's wiring of RemoteHoldings (main 4adc4c476, 2a712d9da): every end path hands the wearer's
/// own values back, every controller write is noted first, a running Lockdown keeps the panic key off
/// and its forced strict lock. The pure rule's tests are in CCP.Core.Tests/RemoteHoldingsTests.
/// </summary>
public class RemoteHoldingsWiringTests
{
    [Fact]
    public void Every_end_path_asks_the_panic_rule_through_the_one_release()
    {
        var src = Source("RemoteControlService.cs");
        var release = Body(src, "private void ReleaseRemoteSafetyState()");
        int ask = release.IndexOf("RemoteHoldings.TurnsPanicBackOn(", StringComparison.Ordinal);
        Assert.True(ask > 0 && ask < release.IndexOf("s.PanicKeyEnabled = true", StringComparison.Ordinal));
        Assert.Contains("App.Lockdown?.DisabledPanicKey == true", release);
        foreach (var sig in new[] { "private void StopAllRemoteEffects(RemoteEndCause cause)", "private void StopRemoteTriggeredEffects()",
                     "private void ReleaseRemoteHoldings()" })
            Assert.DoesNotContain("PanicKeyEnabled = true", CodeOnly(Body(src, sig)));
        Assert.Contains("ReleaseRemoteSafetyState();", Body(Source("Remote/RemoteControlService.V2.cs"), "public void ReleaseHoldingsForExit()"));
        var ld = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "Haptics", "LockdownService.cs"));
        Assert.Contains("DisabledPanicKey = settings.LockdownDisablePanicKey;", Body(ld, "public void Activate(TimeSpan duration)"));
    }
    // ---- wiring ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("RemoteControlService.cs", "private void StopAllRemoteEffects(RemoteEndCause cause)")]   // session end, leash cut, panic
    [InlineData("RemoteControlService.cs", "private void StopRemoteTriggeredEffects()")]                 // controller left (stop effects on)
    [InlineData("RemoteControlService.cs", "private void ReleaseRemoteHoldings()")]                      // over the wearer's own run
    [InlineData("Remote/RemoteControlService.V2.cs", "public void ReleaseHoldingsForExit()")]            // app exit
    public void Every_end_path_hands_back_and_never_writes_the_overlays_or_strict_off(string file, string signature)
    {
        var body = CodeOnly(Body(Source(file), signature));
        Assert.Contains("HandBackWearerVisuals();", body);
        foreach (var off in new[] { "SpiralEnabled = false", "PinkFilterEnabled = false", "StrictLockEnabled = false",
                     "EnableSpiral(false)", "EnablePinkFilter(false)" })
            Assert.DoesNotContain(off, body);
    }

    [Fact]
    public void Snapshot_hand_back_and_drop_run_in_order()
    {
        var src = Source("RemoteControlService.cs");
        foreach (var sig in new[] { "private void StopAllRemoteEffects(RemoteEndCause cause)", "private void ReleaseRemoteHoldings()",
                     "private void HandleControllerDisconnectCleanup()" })
            Assert.Contains("ReleaseRemoteSafetyState();", Body(src, sig));
        Assert.Contains("HandBackStrictLock()", Body(src, "private void ReleaseRemoteSafetyState()"));
        Assert.DoesNotContain("_remoteSetStrictLock", src);

        // The session stop restores its own snapshot first; the hand-back comes after it.
        var all = Body(src, "private void StopAllRemoteEffects(RemoteEndCause cause)");
        Assert.True(all.IndexOf("StopSessionFromRemote();", StringComparison.Ordinal)
                    < all.IndexOf("HandBackWearerVisuals();", StringComparison.Ordinal));

        var start = Body(src, "public async Task<string?> StartSessionAsync(string tier)");
        Assert.True(start.IndexOf("IsActive = true;", StringComparison.Ordinal) < start.IndexOf("TakeWearerSnapshot();", StringComparison.Ordinal));
        var cleanup = Body(src, "private void CleanupSession(RemoteEndCause cause = RemoteEndCause.SessionEnded)");
        Assert.True(cleanup.IndexOf("StopAllRemoteEffects(cause)", StringComparison.Ordinal)
                    < cleanup.IndexOf("ResetV2SessionState();", StringComparison.Ordinal));

        var v2 = Source("Remote/RemoteControlService.V2.cs");
        Assert.Contains("DropWearerSnapshot();", Body(v2, "private void ResetV2SessionState()"));
        var exit = Body(v2, "public void ReleaseHoldingsForExit()");
        Assert.True(exit.IndexOf("ReleaseRemoteSafetyState();", StringComparison.Ordinal) < exit.IndexOf("DropWearerSnapshot();", StringComparison.Ordinal));
        Assert.DoesNotContain("SubjectOriginal", v2);   // Easy's own restore is the snapshot's job now
        var easy = Body(v2, "private void ApplyEasy()");
        Assert.Contains("NoteRemoteWrite(RemoteHeld.SpiralOpacity, sv);", easy);
        Assert.Contains("NoteRemoteWrite(RemoteHeld.PinkOpacity, pv);", easy);
        Assert.Contains("NoteRemoteWrite(RemoteHeld.BrainDrain,", Body(v2, "private void SetRemoteBrainDrain(bool on)"));
    }

    [Theory]
    [InlineData("case \"show_pink_filter\":", "RemoteHeld.Pink", "PinkFilterEnabled = true")]
    [InlineData("case \"stop_pink_filter\":", "RemoteHeld.Pink", "PinkFilterEnabled = false")]
    [InlineData("case \"show_spiral\":", "RemoteHeld.Spiral", "SpiralEnabled = true")]
    [InlineData("case \"stop_spiral\":", "RemoteHeld.Spiral", "SpiralEnabled = false")]
    [InlineData("case \"set_pink_opacity\":", "RemoteHeld.PinkOpacity", "PinkFilterOpacity = ")]
    [InlineData("case \"set_spiral_opacity\":", "RemoteHeld.SpiralOpacity", "SpiralOpacity = ")]
    [InlineData("case \"enable_strict_lock\":", "RemoteHeld.StrictLock", "StrictLockEnabled = true")]
    [InlineData("case \"disable_strict_lock\":", "RemoteHeld.StrictLock", "StrictLockEnabled = false")]
    [InlineData("case \"start_session\":", "RemoteHeld.StrictLock", "StrictLockEnabled = true")]
    public void Every_controller_write_is_noted_before_it_lands(string arm, string field, string write)
    {
        var src = Source("RemoteControlService.cs");
        int at = src.IndexOf(arm, StringComparison.Ordinal);
        int note = src.IndexOf("NoteRemoteWrite(" + field, at, StringComparison.Ordinal);
        int lands = src.IndexOf("App.Settings.Current." + write, at, StringComparison.Ordinal);
        Assert.True(at > 0 && note > at && lands > note, arm);
        Assert.True(src.IndexOf("break;", note, StringComparison.Ordinal) > lands, arm + ": noted in the same arm");
        if (arm.Contains("disable_strict_lock"))
            Assert.InRange(src.IndexOf("RemoteHoldings.RefusesStrictOff(", at, StringComparison.Ordinal), at + 1, note);
    }

    [Fact]
    public void Lockdown_takes_the_correction_and_the_crash_heal_runs_after_its_recovery()
    {
        var root = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var ld = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "Haptics", "LockdownService.cs"));
        Assert.Contains("ForcedStrictLock = settings.LockdownForceStrictLock;", Body(ld, "public void Activate(TimeSpan duration)"));
        var correct = Body(ld, "public void CorrectRecordedStrictLock(bool wearersOwn)");
        Assert.Contains("if (!_isActive", correct);
        Assert.Contains("WriteRecoveryFile(", correct);

        var app = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
        int recover = app.IndexOf("LockdownService.RecoverIfNeeded();", StringComparison.Ordinal);
        Assert.True(recover > 0 && app.IndexOf("RemoteHoldings.HealOnLaunch(UserDataPath", StringComparison.Ordinal) > recover);
    }

    private static string Source(string relUnderServices)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel", "Services" }
            .Concat(relUnderServices.Split('/')).ToArray()));

    private static string CodeOnly(string body)
        => string.Join("\n", body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string Body(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        int depth = 0;
        for (int i = source.IndexOf('{', at); i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(at, i - at + 1);
        }
        throw new Xunit.Sdk.XunitException("unclosed method: " + signature);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }
}
