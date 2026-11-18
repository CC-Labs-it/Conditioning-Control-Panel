using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ConditioningControlPanel.Services.PrizeCounter;
using ConditioningControlPanel.Services.Safety;
using Xunit;
using S = ConditioningControlPanel.Services.PrizeCounter.CounterSheetRules.Surface;

namespace ConditioningControlPanel.Tests;

/// <summary>The game doors (owner 2026-11-17: "retire the 3D counter"): Back Room, Racing Thoughts, chess and
/// Rain's wardrobe open the 2D sheet inside their own window; the 3D window is only the fallback.</summary>
public class GameCounterDoorTests
{
    [Theory]
    [InlineData("backroom")]
    [InlineData("glassroad")]
    [InlineData("piecebypiece")]
    [InlineData("rain-wardrobe")]
    public void EveryGameDoor_GetsTheSheetOverItsGame_ByDefault(string from)
    {
        Assert.Equal(S.GameSheet, CounterSheetRules.PickSurface(from, hasOpener: true, force3D: false));
        Assert.Equal(S.GameSheet, CounterSheetRules.PickSurface(from, hasOpener: false, force3D: false));
        // The switch gives the old window back, for that door only.
        Assert.Equal(S.Window, CounterSheetRules.PickSurface(from, hasOpener: true, force3D: true));
    }

    [Fact]
    public void TheGameDoorList_IsTheOneTheRulesRead()
    {
        foreach (var door in PrizeCounterHostService.GameDoors) Assert.True(CounterSheetRules.IsGameDoor(door));
        Assert.False(CounterSheetRules.IsGameDoor("wallet"));
        Assert.False(CounterSheetRules.IsGameDoor(null));
    }

    [Theory]
    [InlineData("launcher", false, false, "DoorSheet")]
    [InlineData("wallet", false, true, "DoorSheet")]   // the switch is for game doors only
    [InlineData("wallet", true, false, "Window")]
    [InlineData("somewhere", false, false, "Window")]
    public void WalletDoors_KeepTheirSheet(string from, bool hasOpener, bool force3D, string want)
        => Assert.Equal(want, CounterSheetRules.PickSurface(from, hasOpener, force3D).ToString());

    [Theory]
    [InlineData("None", "GameSheet", false, false)]
    [InlineData("GameSheet", "GameSheet", true, false)]    // the same game: stays, a new tab reloads it
    [InlineData("GameSheet", "GameSheet", false, true)]    // another game's sheet goes first
    [InlineData("GameSheet", "DoorSheet", false, true)]    // a wallet press elsewhere takes it over
    [InlineData("GameSheet", "Window", false, true)]
    [InlineData("DoorSheet", "GameSheet", false, true)]
    [InlineData("DoorSheet", "Window", false, true)]
    [InlineData("Window", "GameSheet", false, true)]
    [InlineData("Window", "DoorSheet", false, false)]      // as before: the wallet brings the window forward
    [InlineData("Window", "Window", false, false)]
    public void OneCounterAtATime_AcrossWindows(string open, string want, bool sameView, bool closes)
        => Assert.Equal(closes, CounterSheetRules.ClosesOpenFirst(Enum.Parse<S>(open), Enum.Parse<S>(want), sameView));

    [Theory]
    [InlineData("scrim", "back")]
    [InlineData("door-hidden", "back")]
    [InlineData("door-closed", "back")]
    [InlineData("no-room", "back")]
    [InlineData(null, "back")]
    [InlineData("failed", "failed")]
    public void ASheetThatAsksToGo_ClosesWithAReasonTheGameKnows(string? why, string reason)
        => Assert.Equal(reason, CounterSheetRules.SheetCloseReason(why));

    [Fact]
    public void EachGameUnderTheCounter_HearsTheCloseOnce_OnEveryPath()
    {
        var list = (List<Action<string>>)typeof(PrizeCounterHostService)
            .GetField("_onClosed", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        foreach (var reason in new[] { PrizeCounterHostService.CloseBack, PrizeCounterHostService.ClosePanic,
                     PrizeCounterHostService.CloseWear, PrizeCounterHostService.CloseAppExit, PrizeCounterHostService.CloseFailed })
        {
            var heard = new List<string>();
            list.Add(r => heard.Add(r));
            PrizeCounterHostService.CloseFor(reason);
            PrizeCounterHostService.CloseFor(PrizeCounterHostService.CloseBack);   // a second close (scrim after Back) is silent
            Assert.Equal(new[] { reason }, heard);
        }
    }

    [Fact]
    public void TheCounterKeepsEscape_EvenWithItsGameInFront()
    {
        // The sheet lives inside the game window, so that game is the foreground window too.
        Assert.Equal(PanicPolicy.KeptEscapePage.Counter, PanicPolicy.KeptEscapeGoesTo(true,
            false, false, true, true, false, false, roomInFront: true, roomReady: true,
            counterInFront: true, counterReady: true, glassroadInFront: true, glassroadReady: true));
        Assert.Equal(PanicPolicy.KeptEscapePage.None, PanicPolicy.KeptEscapeGoesTo(true,
            false, false, true, true, false, false, counterInFront: true, counterReady: false));
        Assert.Equal(PanicPolicy.KeptEscapePage.Board, PanicPolicy.KeptEscapeGoesTo(true,
            false, false, true, true, false, false));
    }

    [Fact]
    public void TheDefaultDoorIsTheSheet_TheWindowOnlyBehindADebugSwitch()
    {
        var sheet = Read("ConditioningControlPanel", "Services", "PrizeCounter", "PrizeCounterHostService.Sheet.cs");
        int s = sheet.IndexOf("internal static bool Force3D", StringComparison.Ordinal);
        Assert.True(s > 0);
        var body = sheet.Substring(s, sheet.IndexOf("GameHostOf", s, StringComparison.Ordinal) - s);
        Assert.Contains("#if DEBUG", body);
        Assert.Contains("\"CCP_COUNTER_3D\") == \"1\"", body);
        Assert.Contains("#else", body);
        Assert.Contains("return false;", body);
        // Every game door is looked up, and a game sheet leaves init.sheet out (the page draws its own wallet).
        foreach (var door in PrizeCounterHostService.GameDoors) Assert.Contains("\"" + door + "\" => ", sheet);
        Assert.Contains("_isSheet = game == null;", sheet);
        Assert.Contains("game?.Freeze();", sheet);
        var launch = Read("ConditioningControlPanel", "Services", "PrizeCounter", "PrizeCounterHostService.cs");
        Assert.Contains("CounterSheetRules.PickSurface(from, onClosed != null, Force3D)", launch);
        Assert.Contains("if (game != null && TryLaunchSheet(game.View, generation, game)) return;", launch);
        foreach (var path in new[] { new[] { "BackRoom", "BackRoomHostService.cs" }, new[] { "Glassroad", "GlassroadHostService.cs" },
                     new[] { "PieceByPiece", "PieceByPieceHostService.cs" }, new[] { "Companion", "RainWardrobeHostService.cs" } })
            Assert.Contains("internal static ChaosWebViewHost? CounterHost => _host;", Read("ConditioningControlPanel", "Services", path[0], path[1]));
    }

    [Fact]
    public void TheGameSheet_CutsTheGameOut_AndGivesItBackWhenUp()
    {
        var src = Read("ConditioningControlPanel", "Controls", "GameCounterSheet.cs");
        Assert.Contains("View.FocusDoorOnClose = false;", src);
        Assert.Contains("View.Retracted += Thaw;", src);
        Assert.Contains("SetWindowRgn(_cutHwnd, IntPtr.Zero, true)", src);
        var view = Read("ConditioningControlPanel", "Controls", "CounterSheetView.cs");
        Assert.Contains("try { Retracted?.Invoke(); }", view);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
