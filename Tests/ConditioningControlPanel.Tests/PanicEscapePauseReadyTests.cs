using System;
using System.IO;
using Xunit;
using static ConditioningControlPanel.Services.Safety.PanicPolicy;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Bug hunt 2026-10-22, the Escape pause wave (#2463-#2467). Two ways the pause swallowed a press
/// that should have been a panic:
///
///   1. A game window in front whose page was still booting (or reloading, or never came up) kept
///      the Escape as its pause, but nothing could pause: the press did nothing, and taps more than
///      2 s apart never reached the double-tap panic.
///   2. The Remote HUD's Stop button runs the panic path. The HUD never takes focus, so the game
///      under it stayed in front, the click was read as the game's Escape pause and nothing stopped.
/// </summary>
public class PanicEscapePauseReadyTests
{
    private static readonly DateTime T0 = new(2026, 10, 22, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AReadyGameInFront_StillKeepsTheFirstEscape()
        => Assert.True(GameClaimsEscapeAsPause("Escape", true, false, false, null, T0,
            frontGameReady: true, fromPanicKey: true));

    [Fact]
    public void ABootingGameInFront_IsAFullPanic()
        => Assert.False(GameClaimsEscapeAsPause("Escape", true, false, false, null, T0,
            frontGameReady: false, fromPanicKey: true));

    [Fact]
    public void SlowTapsOnAPageThatNeverLoads_AreEachAPanic()
    {
        DateTime? last = null;
        for (int i = 0; i < 5; i++)
        {
            var now = T0.AddSeconds(3 * i); // every tap outside the 2 s double tap
            bool claim = GameClaimsEscapeAsPause("Escape", true, false, false, last, now, frontGameReady: false);
            Assert.False(claim);
            last = claim ? now : null;
        }
    }

    [Fact]
    public void AStopButton_IsNeverAGamePause()
        => Assert.False(GameClaimsEscapeAsPause("Escape", true, false, false, null, T0,
            frontGameReady: true, fromPanicKey: false));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void OnlyAKeyPressOnAReadyGame_IsThePause(bool ready, bool fromKey, bool expected)
        => Assert.Equal(expected, GameClaimsEscapeAsPause("Escape", true, false, false, null, T0,
            frontGameReady: ready, fromPanicKey: fromKey));

    [Fact]
    public void TheClaim_AsksWhetherTheGameInFrontIsUp_AndWhoPressed()
    {
        var source = Read("MainWindow", "MainWindow.xaml.cs");
        var start = source.IndexOf("private bool TryRacePauseOnEscape()", StringComparison.Ordinal);
        var end = source.IndexOf("private void HandlePanicKeyPress()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "TryRacePauseOnEscape moved - update this test with it");
        var body = source[start..end];
        Assert.Contains("frontGameReady: frontGameReady", body);
        Assert.Contains("fromPanicKey: !_panicFromStopButton", body);
        // The readiness answer is the same routing the kept frame uses, asked before the claim.
        int ready = body.IndexOf("bool frontGameReady = Services.Safety.PanicPolicy.KeptEscapeGoesTo(true,", StringComparison.Ordinal);
        int claim = body.IndexOf("GameClaimsEscapeAsPause(", StringComparison.Ordinal);
        Assert.True(ready > 0 && claim > ready);
        Assert.Contains("try { HandlePanicKeyPress(); }", body);
        Assert.Contains("finally { _panicFromStopButton = false; }", body);
    }

    [Fact]
    public void AStopButton_IsNeverEatenByTheSettingsPalette()
    {
        // Escape with the Ctrl+K palette open only closes the palette; a Stop click is not an Escape.
        var source = Read("MainWindow", "MainWindow.xaml.cs");
        Assert.Contains("bool paletteClaimed = !lockCardOpen && !_panicFromStopButton", source);
    }

    [Theory]
    [InlineData("MainWindow.RemoteHud.cs", "private void StopFromRemoteHud()")]
    [InlineData("MainWindow.Leash.cs", "private void PanicFromLeashGate()")]
    public void StopButtons_RunThePanicAsAButton(string file, string method)
    {
        var source = Read("MainWindow", file);
        var start = source.IndexOf(method, StringComparison.Ordinal);
        Assert.True(start >= 0, method + " moved - update this test with it");
        var end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
        var body = source[start..end];
        Assert.Contains("HandlePanicFromStopButton();", body);
        Assert.DoesNotContain("HandlePanicKeyPress();", body);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        var path = Path.Combine(dir!.FullName, "ConditioningControlPanel");
        foreach (var p in parts) path = Path.Combine(path, p);
        return File.ReadAllText(path);
    }
}
