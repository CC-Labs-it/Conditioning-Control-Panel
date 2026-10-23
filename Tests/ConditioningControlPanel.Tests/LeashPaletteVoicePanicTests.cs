using System;
using System.IO;
using ConditioningControlPanel.Controls.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The leash part of a panic stood aside whenever the Ctrl+K palette was open with Escape as the panic
/// key, even for the voice panic: under Lockdown, "red" then left the leash task running. Only a physical
/// Escape the palette takes may skip it now; the voice, the blink stop and the Stop buttons never do.
/// </summary>
public class LeashPaletteVoicePanicTests
{
    [Theory]
    [InlineData(true, true, false, "Escape", true)]
    [InlineData(true, true, false, "escape", true)]
    [InlineData(false, true, false, "Escape", false)]   // voice, blink stop, Stop buttons
    [InlineData(true, true, true, "Escape", false)]     // a lock card takes the press first
    [InlineData(true, false, false, "Escape", false)]   // no palette
    [InlineData(true, true, false, "F8", false)]        // a rebound key never closes the palette
    [InlineData(true, true, false, null, false)]
    public void Only_a_physical_escape_the_palette_takes_skips_the_leash(bool fromKey, bool paletteOpen, bool lockCard,
                                                                          string? key, bool skips) =>
        Assert.Equal(skips, LeashUiRules.PaletteTakesPanicPress(fromKey, paletteOpen, lockCard, key));

    [Fact]
    public void The_leash_part_asks_the_rule_and_only_the_key_paths_say_fromKey()
    {
        var leash = Source("MainWindow", "MainWindow.Leash.cs");
        var body = Body(leash, "private void LeashOnPanicPress(bool panicRuns, bool fromKey = false)");
        Assert.Contains("LeashUiRules.PaletteTakesPanicPress(fromKey, SettingsPaletteWindow.IsOpen, LockCardWindow.IsAnyOpen(),", body);
        Assert.DoesNotContain("if (SettingsPaletteWindow.IsOpen", body);   // the old unconditional skip
        Assert.Contains("private void LeashOnPanicPress() => LeashOnPanicPress(panicRuns: true);", leash);

        // The panic key: a real press may be the palette's Escape, a Stop / Panic button never is.
        var key = Body(Source("MainWindow", "MainWindow.xaml.cs"), "private void HandlePanicKeyPress()");
        Assert.Contains("LeashOnPanicPress(panicRuns: true, fromKey: !_panicFromStopButton);", key);
        Assert.Contains("LeashOnPanicPress(panicRuns: false, fromKey: true);",
            Body(leash, "private bool LeashPanicKeyWhilePanicOff(System.Windows.Input.Key key)"));

        // Everything else is no Escape: the voice panic (whole or leash only), the HUD Stop, the blink stop.
        var voice = Body(Source("MainWindow", "MainWindow.RemoteControl.cs"), "internal bool TriggerPanicFromRemote()");
        Assert.DoesNotContain("fromKey", voice);
        Assert.Contains("LeashOnPanicPress(panicRuns: false);", voice);
        Assert.Contains("LeashOnPanicPress();", voice);
        Assert.DoesNotContain("fromKey", Source("MainWindow", "MainWindow.RemoteHud.cs"));
        Assert.DoesNotContain("fromKey", Source("MainWindow", "MainWindow.LabTab.cs"));
    }

    private static string Source(params string[] parts)
    {
        var all = new string[parts.Length + 2];
        all[0] = RepoRoot();
        all[1] = "ConditioningControlPanel";
        Array.Copy(parts, 0, all, 2, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }

    /// <summary>The text of one method: from its signature to the brace that closes it.</summary>
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
