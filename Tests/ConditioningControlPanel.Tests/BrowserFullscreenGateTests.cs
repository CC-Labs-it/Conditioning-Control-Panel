using System;
using System.IO;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1565, #1570, #1585, #1589, #1596: maximizing a web video froze or crashed the app.
/// The switch ran inside the WebView2 callback, pumped the dispatcher at Render priority in the
/// middle, and flipped the Windows display topology to clone on every multi-monitor fullscreen.
/// </summary>
public sealed class BrowserFullscreenGateTests
{
    [Fact]
    public void FirstRequest_PostsOneDrain_LaterOnesRideOnIt()
    {
        var gate = new BrowserFullscreenGate();
        Assert.True(gate.Request(true));
        Assert.False(gate.Request(false));
        Assert.False(gate.Request(true));
    }

    [Fact]
    public void LatestWishWins_AndTheSlotFreesAfterTheDrain()
    {
        var gate = new BrowserFullscreenGate();
        gate.Request(true);
        gate.Request(false);
        Assert.False(gate.Take());
        Assert.Null(gate.Take());
        Assert.True(gate.Request(true));
    }

    [Fact]
    public void AnExit_DropsAQueuedEnter()
    {
        var gate = new BrowserFullscreenGate();
        gate.Request(true);
        gate.Clear();
        var wish = gate.Take();
        Assert.Null(wish);
        Assert.False(BrowserFullscreenGate.ShouldApply(wish, isFullscreen: false));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void OnlyAChangeIsApplied(bool wish, bool isFullscreen, bool expected)
        => Assert.Equal(expected, BrowserFullscreenGate.ShouldApply(wish, isFullscreen));

    [Fact]
    public void ANewTransition_MakesOlderDeferredStepsStale()
    {
        var gate = new BrowserFullscreenGate();
        var enter = gate.BeginTransition();
        Assert.True(gate.IsCurrent(enter));
        gate.BeginTransition();
        Assert.False(gate.IsCurrent(enter));
    }

    [Fact]
    public void TheSwitch_NeverPumpsNorClonesTheDisplays()
    {
        var browser = File.ReadAllText(Path.Combine(SourceRoot(), "MainWindow", "MainWindow.Browser.cs"));
        Assert.DoesNotContain("Dispatcher.Invoke(() => { }, DispatcherPriority.Render)", browser);
        Assert.DoesNotContain("ScreenMirror.EnableMirror", browser);
        Assert.DoesNotContain("Dispatcher.Invoke(() => HandleBrowserFullscreenChanged", browser);
        Assert.Contains("_browser.FullscreenChanged += (s, isFullscreen) => RequestBrowserFullscreen(isFullscreen);", browser);
    }

    // ---- ccp-bugs #1649: takeover web videos stopped going fullscreen in 7.2.4 ----------------
    // The reparent into the host now comes after Chromium's own enter, knocks the page out of
    // fullscreen, and the page's exit (fsExit / Chromium false / ccp_exit_fullscreen) undid the
    // enter. A takeover's page exits are held around the enter; nobody else's are.

    /// <summary>Plays the MainWindow sequence: page exits ask the gate first, then queue a wish.</summary>
    private static void PageRequest(BrowserFullscreenGate gate, bool fullscreen, long now)
    {
        if (fullscreen) gate.NoteEnterWish(now);
        else if (gate.HoldsExit(now)) return;
        gate.Request(fullscreen);
    }

    [Fact]
    public void TakeoverEnter_ThenPageExitInsideTheTransition_KeepsTheHost()
    {
        var gate = new BrowserFullscreenGate();
        var isFullscreen = false;

        gate.ArmTakeover(0);                       // the takeover script is injected
        PageRequest(gate, true, 1_500);            // Chromium: the page went fullscreen
        var wish = gate.Take();                    // the drain
        Assert.True(BrowserFullscreenGate.ShouldApply(wish, isFullscreen));
        isFullscreen = true;
        gate.BeginTransition();
        Assert.True(gate.NoteEnterApplied(1_550)); // the host is built: a takeover's enter

        PageRequest(gate, false, 1_900);           // the reparent knocked the page out: fsExit
        Assert.False(BrowserFullscreenGate.ShouldApply(gate.Take(), isFullscreen)); // host stays

        // Once the hold is over, a real exit (double-click, video end) goes through.
        PageRequest(gate, false, 1_550 + BrowserFullscreenGate.TakeoverHoldMs + 1);
        Assert.True(BrowserFullscreenGate.ShouldApply(gate.Take(), isFullscreen));
    }

    [Fact]
    public void TakeoverExit_BeforeTheDrain_DoesNotUndoTheQueuedEnter()
    {
        var gate = new BrowserFullscreenGate();
        gate.ArmTakeover(0);
        PageRequest(gate, true, 1_500);
        PageRequest(gate, false, 1_520);
        Assert.True(gate.Take());
    }

    [Fact]
    public void AFullscreenTheUserStarted_HoldsNothing()
    {
        var gate = new BrowserFullscreenGate();
        PageRequest(gate, true, 0);
        Assert.True(gate.Take());
        Assert.False(gate.NoteEnterApplied(10));
        Assert.False(gate.HoldsExit(20));
        PageRequest(gate, false, 20);
        Assert.False(gate.Take());
    }

    [Fact]
    public void TheTakeoverArm_ExpiresAndIsUsedOnce()
    {
        var gate = new BrowserFullscreenGate();
        gate.ArmTakeover(0);
        Assert.False(gate.NoteEnterApplied(BrowserFullscreenGate.TakeoverArmMs + 1));

        gate.ArmTakeover(0);
        Assert.True(gate.NoteEnterApplied(100));
        Assert.False(gate.NoteEnterApplied(200));
    }

    [Fact]
    public void AnExit_EndsTheHoldAndTheArm()
    {
        var gate = new BrowserFullscreenGate();
        gate.ArmTakeover(0);
        gate.NoteEnterApplied(100);
        Assert.True(gate.HoldsExit(200));
        gate.Clear();                              // Esc, panic, teardown, EndWebVideoTakeover
        Assert.False(gate.HoldsExit(200));
        Assert.False(gate.NoteEnterApplied(300));
    }

    [Fact]
    public void OnlyPageExitsAreHeld_TheUsersWayOutIsNot()
    {
        var browser = File.ReadAllText(Path.Combine(SourceRoot(), "MainWindow", "MainWindow.Browser.cs"))
            .Replace("\r\n", "\n");
        // The takeover script arms the hold; page exits ask it before queuing a wish.
        Assert.Contains("_browserFsGate.ArmTakeover(Environment.TickCount64);", browser);
        Assert.Contains("else if (_browserFsGate.HoldsExit(now))", browser);
        // Esc / F11 on the host exits directly, never through the held path.
        Assert.Contains("args.Handled = true;\n                try { ExitBrowserFullscreen(); } catch { }", browser);
        // Every direct exit starts by clearing the gate, which ends the hold.
        Assert.Contains("private void ExitBrowserFullscreen()\n        {\n            // A queued enter from the page dies with any exit (panic, teardown, Esc), and a pending\n            // maximize of the host stands down.\n            _browserFsGate.Clear();", browser);
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

        throw new DirectoryNotFoundException(
            "ConditioningControlPanel project not found above " + AppContext.BaseDirectory);
    }
}
