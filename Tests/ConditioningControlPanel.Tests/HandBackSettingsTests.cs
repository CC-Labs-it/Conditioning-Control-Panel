using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Commands;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// One bug class (Oct 22 hunt): a feature writes a value into the auto-saved settings and either
/// never hands it back, or hands it back after the last save. Each fact names the path.
/// </summary>
public class HandBackSettingsTests
{
    // ---- panic: stops what is on screen, never the user's saved switches --------------------------

    [Theory]
    [InlineData("MainWindow.xaml.cs", "private void StopAdHocEffects()")]
    [InlineData("MainWindow.RemoteControl.cs", "internal void TriggerPanicFromRemote()")]
    public void Panic_never_writes_the_users_spiral_or_pink_switch(string file, string signature)
    {
        var body = CodeOnly(Body(Source("MainWindow", file), signature));
        Assert.DoesNotContain("EnablePinkFilter(false)", body);
        Assert.DoesNotContain("EnableSpiral(false)", body);
        Assert.DoesNotContain("PinkFilterEnabled = false", body);
        Assert.DoesNotContain("SpiralEnabled = false", body);
        // The service stops instead, so no reconcile tick can repaint them.
        Assert.Contains("App.Overlay?.Stop();", body);
    }

    // ---- exit: every borrowed value goes back before the last save -------------------------------

    [Fact]
    public void Exit_settles_temporary_values_after_the_session_and_before_the_save()
    {
        var closing = Body(Source("MainWindow", "MainWindow.WindowChrome.cs"),
            "protected override void OnClosing(System.ComponentModel.CancelEventArgs e)");
        int session = closing.IndexOf("EnsureSessionRestoredForExit();", StringComparison.Ordinal);
        int settle = closing.IndexOf("SettleTemporarySettingsForExit();", StringComparison.Ordinal);
        int save = closing.IndexOf("SaveSettings();", StringComparison.Ordinal);
        Assert.True(session > 0 && settle > session && save > settle,
            "OnClosing: session restore, then settle, then SaveSettings");

        var onExit = Source("App.xaml.cs");
        int appSettle = onExit.IndexOf("MainWindowRef?.SettleTemporarySettingsForExit();", StringComparison.Ordinal);
        int appSave = onExit.IndexOf("Settings?.SaveImmediate();", appSettle < 0 ? 0 : appSettle, StringComparison.Ordinal);
        Assert.True(appSettle > 0 && appSave > appSettle && appSave - appSettle < 300,
            "App.OnExit: settle right before the final SaveImmediate");
    }

    [Theory]
    [InlineData("StopRampTimer();")]
    [InlineData("App.Autonomy?.CancelActivePulses();")]
    [InlineData("App.RemoteControl?.ReleaseHoldingsForExit();")]
    public void Exit_settle_covers_ramp_takeover_and_remote(string call)
        => Assert.Contains(call, Body(Source("MainWindow", "MainWindow.WindowChrome.cs"),
            "internal void SettleTemporarySettingsForExit()"));

    [Fact]
    public void Remote_exit_release_hands_back_the_controllers_holdings_only_while_a_session_is_live()
    {
        var body = Body(Source("Services", "Remote", "RemoteControlService.V2.cs"), "public void ReleaseHoldingsForExit()");
        int gate = body.IndexOf("if (!IsActive) return;", StringComparison.Ordinal);
        Assert.True(gate > 0);
        foreach (var call in new[] { "RestoreRemoteBrainDrainSetting();", "ReleaseRemoteSafetyState();", "RestoreEasedOpacities();" })
            Assert.True(body.IndexOf(call, StringComparison.Ordinal) > gate, call);
    }

    // ---- Takeover pulse: an engine started inside the 30 s owns the overlay service ----------------

    [Fact]
    public void Pulse_end_leaves_the_overlay_service_to_a_run_started_meanwhile()
    {
        Assert.False(AutonomyService.ShouldStopOverlayAfterPulse(
            overlayWasRunningBeforePulse: false, otherPulseActive: false, engineRunning: true));
        Assert.True(AutonomyService.ShouldStopOverlayAfterPulse(
            overlayWasRunningBeforePulse: false, otherPulseActive: false, engineRunning: false));
        var src = Source("Services", "AutonomyService.cs");
        Assert.Equal(2, CountOf(src, ", App.IsEngineRunning))"));
    }

    // ---- AI spiral / pink: "off" carries no opacity ---------------------------------------------

    [Theory]
    [InlineData(false, 0)]   // { "On": false } - Intensity missing, deserialises to 0
    [InlineData(false, 25)]
    [InlineData(true, 0)]    // "on" with no intensity keeps the user's own opacity
    [InlineData(true, -5)]
    public void Ai_overlay_command_leaves_the_users_opacity_alone(bool on, int intensity)
        => Assert.Null(AiOverlayOpacity.ToWrite(on, intensity, 30));

    [Theory]
    [InlineData(25, 25)]
    [InlineData(1, 1)]
    [InlineData(90, 30)]
    public void Ai_overlay_command_on_writes_the_clamped_intensity(int intensity, int expected)
        => Assert.Equal(expected, AiOverlayOpacity.ToWrite(true, intensity, 30));

    [Theory]
    [InlineData("SpiralCommand.cs", "SpiralOpacity")]
    [InlineData("PinkCommand.cs", "PinkFilterOpacity")]
    public void Ai_overlay_commands_go_through_the_rule(string file, string field)
    {
        // The port: the commands live in CCP.Core and the WPF head's surface does the write.
        var core = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "Commands", file));
        Assert.Contains("AiOverlayOpacity.ToWrite(", CodeOnly(core));
        var src = CodeOnly(Source("App.AiEffects.cs"));
        Assert.Contains($"if (opacity is int o) settings.{field} = o;", src);
        Assert.Equal(1, CountOf(src, $"settings.{field} ="));
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static int CountOf(string haystack, string needle)
    {
        int n = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
        return n;
    }

    private static string CodeOnly(string body)
    {
        const char lf = (char)10;
        return string.Join(lf.ToString(), body.Split(lf)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    private static string Source(params string[] parts)
    {
        var all = new string[parts.Length + 2];
        all[0] = RepoRoot();
        all[1] = "ConditioningControlPanel";
        Array.Copy(parts, 0, all, 2, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }

    private static string Body(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        int open = source.IndexOf('{', at);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
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
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
