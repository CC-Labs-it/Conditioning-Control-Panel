using System;
using System.IO;
using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.PrizeCounter;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The 2D counter sheet (owner 2026-11-17): which door slides it down, where it sits inside the
/// door window, the wallet's second press, and the slide itself.</summary>
public class CounterSheetRulesTests
{
    [Theory]
    [InlineData("launcher", false, true)]
    [InlineData("wallet", false, true)]       // the panel's wallet, and EMI's options
    [InlineData("wallet", true, false)]       // a game paused under it keeps the window
    [InlineData("glassroad", false, false)]
    [InlineData("piecebypiece", false, false)]
    [InlineData("backroom", false, false)]
    [InlineData("rain-wardrobe", false, false)]
    [InlineData(null, false, false)]
    public void WalletDoorsGetTheSheet_GameDoorsKeepTheWindow(string? from, bool hasOpener, bool sheet)
        => Assert.Equal(sheet, CounterSheetRules.GetsSheet(from, hasOpener));

    [Fact]
    public void EveryGameDoor_KeepsTheWindow()
    {
        foreach (var door in PrizeCounterHostService.GameDoors)
        {
            Assert.False(CounterSheetRules.GetsSheet(door, hasOpener: false));
            Assert.Equal(CounterSheetRules.Door.None, CounterSheetRules.PickDoor(door, true, true));
        }
    }

    [Theory]
    [InlineData("launcher", true, true, "Launcher")]
    [InlineData("launcher", false, true, "Panel")]
    [InlineData("wallet", true, true, "Panel")]
    [InlineData("wallet", true, false, "Launcher")]   // EMI's options with the panel in the tray
    [InlineData("wallet", false, false, "None")]      // nothing on screen: the window opens
    [InlineData("backroom", true, true, "None")]
    public void TheSheetLivesInTheDoorsOwnWindow(string from, bool launcherUp, bool panelUp, string door)
        => Assert.Equal(door, CounterSheetRules.PickDoor(from, launcherUp, panelUp).ToString());

    [Fact]
    public void Frame_RunsFromUnderTheBar_InsideTheWindowEdge()
    {
        // The launcher: a 48 DIP title bar across the whole 1280 x 800 window.
        var f = CounterSheetRules.Frame(new Size(1280, 800), new Rect(0, 0, 1280, 48), 8, 12, 360, 240);
        Assert.Equal(new Rect(8, 48, 1264, 800 - 48 - 12), f!.Value);
    }

    [Fact]
    public void Frame_StartsAtTheBarsLeftEdge_WhenTheBarDoesNot()
    {
        // The panel: the header sits right of the nav rail.
        var f = CounterSheetRules.Frame(new Size(1500, 900), new Rect(100, 40, 1400, 50), 8, 12, 360, 240);
        Assert.Equal(100, f!.Value.Left);
        Assert.Equal(90, f.Value.Top);
        Assert.Equal(1500 - 8, f.Value.Right);
        Assert.Equal(900 - 12, f.Value.Bottom);
    }

    [Fact]
    public void Frame_NeverTouchesTheWindowsRoundedCorners()
    {
        var client = new Size(1000, 700);
        var f = CounterSheetRules.Frame(client, new Rect(0, 0, 1000, 48), CounterSheetRules.SideInsetDip,
            CounterSheetRules.BottomInsetDip, 360, 240)!.Value;
        // Windows 11 rounds the frame at 8 DIPs: the sheet's bottom corners sit inside that curve's box.
        Assert.True(f.Left >= 8 && client.Width - f.Right >= 8);
        Assert.True(client.Height - f.Bottom >= 8);
    }

    [Theory]
    [InlineData(800, 300, true)]     // 300 - 48 - 12 = 240: room
    [InlineData(800, 299, false)]    // no room: the counter window opens instead
    [InlineData(370, 600, false)]    // too narrow once inset
    public void Frame_RefusesAWindowWithNoRoom(double w, double h, bool fits)
        => Assert.Equal(fits, CounterSheetRules.Frame(new Size(w, h), new Rect(0, 0, w, 48), 8, 12, 360, 240) != null);

    [Fact]
    public void Frame_RefusesNonsense()
    {
        Assert.Null(CounterSheetRules.Frame(new Size(0, 0), new Rect(0, 0, 10, 10), 8, 12, 360, 240));
        Assert.Null(CounterSheetRules.Frame(new Size(800, 600), Rect.Empty, 8, 12, 360, 240));
        Assert.Null(CounterSheetRules.Frame(new Size(800, 600), new Rect(0, 900, 800, 10), 8, 12, 360, 240));
        Assert.Null(CounterSheetRules.Frame(new Size(double.NaN, 600), new Rect(0, 0, 800, 48), 8, 12, 360, 240));
    }

    [Fact]
    public void TheSecondPress_ClosesTheSheet()
    {
        Assert.Equal(CounterSheetRules.Press.Close, CounterSheetRules.OnPress(sheetOpen: true));
        Assert.Equal(CounterSheetRules.Press.Open, CounterSheetRules.OnPress(sheetOpen: false));
    }

    [Theory]
    [InlineData(MotionLevel.Full, false, 280, 280)]
    [InlineData(MotionLevel.Reduced, false, 140, 140)]   // less halves
    [InlineData(MotionLevel.Off, false, 0, 0)]           // off cuts
    [InlineData(MotionLevel.Off, true, 0, 0)]
    [InlineData(MotionLevel.Reduced, true, 0, 300)]      // Gentle never slides, the scrim fades at its floor
    public void TheSlide_FollowsTheMotionLevel(MotionLevel level, bool gentle, int slideMs, int scrimMs)
    {
        Assert.Equal(slideMs, CounterSheetRules.SlideDurationMs(level, gentle));
        Assert.Equal(scrimMs, CounterSheetRules.ScrimMs(level, gentle));
    }

    [Fact]
    public void EaseOut_StartsFast_EndsSoft_AndClamps()
    {
        Assert.Equal(0, CounterSheetRules.EaseOut(0));
        Assert.Equal(1, CounterSheetRules.EaseOut(1));
        Assert.Equal(1, CounterSheetRules.EaseOut(3));
        Assert.Equal(0, CounterSheetRules.EaseOut(-1));
        Assert.True(CounterSheetRules.EaseOut(0.5) > 0.8);
        Assert.Equal(1, CounterSheetRules.EaseOut(double.NaN));
    }

    [Fact]
    public void TheSheet_SlidesAsOnePiece_FromWhollyAboveTheBar()
    {
        Assert.Equal(600, CounterSheetRules.SlideOffset(0, 600));   // closed: all of it above the bar line
        Assert.Equal(150, CounterSheetRules.SlideOffset(0.75, 600));
        Assert.Equal(0, CounterSheetRules.SlideOffset(1, 600));
        Assert.Equal(0, CounterSheetRules.SlideOffset(2, 600));
        Assert.Equal(0, CounterSheetRules.SlideOffset(double.NaN, double.NaN));
    }

    [Fact]
    public void TheSheet_IsATabInsideTheWindow_WithARoundedFramedEdge()
    {
        // Owner 2026-11-17: smaller, smooth edges, a clear border.
        Assert.True(CounterSheetRules.SideInsetDip >= 24 && CounterSheetRules.BottomInsetDip >= 24);
        Assert.True(CounterSheetRules.GapUnderBarDip > 0);
        Assert.True(CounterSheetRules.BorderDip >= 2);
        // The page's HWND region is rounded concentric with the frame's inside: no square corner past the curve.
        Assert.Equal(CounterSheetRules.CornerRadiusDip - CounterSheetRules.BorderDip - CounterSheetRules.PaddingDip,
            CounterSheetRules.InnerRadiusDip);
        Assert.True(CounterSheetRules.InnerRadiusDip > 0);
    }

    [Theory]
    [InlineData(MotionLevel.Full, false, 120)]
    [InlineData(MotionLevel.Reduced, false, 60)]
    [InlineData(MotionLevel.Off, false, 0)]
    [InlineData(MotionLevel.Reduced, true, 300)]
    public void TheEdge_LightsUpOnLanding_AtTheMotionLevel(MotionLevel level, bool gentle, int ms)
    {
        Assert.Equal(ms, CounterSheetRules.EdgeFadeMs(level, gentle));
    }

    [Fact]
    public void TheSheetSpeaksTheSameSeam_OnItsOwnPage()
    {
        Assert.Equal("https://ccp.game/prizecounter/sheet.html", CounterSheetRules.SheetUrl);
        var src = Read("ConditioningControlPanel", "Services", "PrizeCounter", "PrizeCounterHostService.cs");
        Assert.Contains("if (_isSheet) init[\"sheet\"] = true;", src);
        Assert.Contains("if (wantSheet && TryLaunchSheet(generation)) return;", src);
        Assert.Contains("_host = new WindowCounterSurface(window);", src);
        // Reload and sign-in stay on the page that is up.
        Assert.DoesNotContain("NavigatePage(StartUrl,", src);
        var sheet = Read("ConditioningControlPanel", "Services", "PrizeCounter", "PrizeCounterHostService.Sheet.cs");
        Assert.Contains("_host.NavigatePage(_startUrl, () => Post(BuildInit()), m => OnPageMessage(m, generation));", sheet);
        Assert.Contains("CloseFor(CloseBack)", sheet);
        // The window-sheet is gone: the counter window is the game doors' window again.
        Assert.DoesNotContain("SheetOwner", Read("ConditioningControlPanel", "Chaos", "ChaosWebViewHost.cs"));
    }

    [Fact]
    public void BothWalletWindows_HoldTheirOwnSheet_InTheirOwnTree()
    {
        var launcher = Read("ConditioningControlPanel", "Windows", "Launcher", "LauncherWindow.xaml.cs");
        Assert.Contains("CounterSheetView.Mount(this, RootGrid, TitleBar,", launcher);
        Assert.Contains("insertAt: RootGrid.Children.IndexOf(LockdownVeil)", launcher);
        var panel = Read("ConditioningControlPanel", "MainWindow", "MainWindow.SparkleWallet.cs");
        Assert.Contains("(HeaderSparkleWallet.Parent as FrameworkElement)?.Parent is not FrameworkElement bar", panel);
        Assert.Contains("CounterSheetView.Mount(this, RootGrid, bar,", panel);
        // The wallet sits in a Grid directly inside the header Border (row 1), so its grandparent is that bar.
        var xaml = Read("ConditioningControlPanel", "MainWindow", "MainWindow.xaml");
        int bar = xaml.IndexOf("<Border Grid.Row=\"1\" Grid.Column=\"1\" Background=\"Transparent\" Padding=\"15,8\">", StringComparison.Ordinal);
        int grid = xaml.IndexOf("<Grid>", bar, StringComparison.Ordinal);
        int wallet = xaml.IndexOf("x:Name=\"HeaderSparkleWallet\"", StringComparison.Ordinal);
        Assert.True(bar > 0 && grid > bar && wallet > grid, "the wallet sits in the header bar's Grid");
        Assert.DoesNotContain("<Border", xaml.Substring(bar + 10, grid - bar - 10));
        var between = xaml.Substring(grid + 6, xaml.LastIndexOf('<', wallet) - grid - 6);
        Assert.DoesNotContain("<Grid>", between);
        Assert.DoesNotContain("<Grid ", between);
        Assert.DoesNotContain("</Grid>", between);
    }

    [Fact]
    public void TheSheetsPage_IsShapedByARegion_AndKeepsOneArgumentString()
    {
        var view = Read("ConditioningControlPanel", "Controls", "CounterSheetView.cs");
        Assert.Contains("SetWindowRgn(hwnd, band, true)", view);
        Assert.Contains("BorderBrush = new SolidColorBrush(Gold)", view);
        Assert.Contains("CreateRoundRectRgn(0, 0, w + 1, h + 1, r, r)", view);
        Assert.Contains("ScrolllerWebGate.Attach(core)", view);
        Assert.Contains("public const string BrowserArguments =", view);
        Assert.Contains("AdditionalBrowserArguments = BrowserArguments", view);
        // Its own profile: never the counter window's, whose arguments follow the motion setting.
        Assert.NotEqual("browser_data_prizecounter", ConditioningControlPanel.Controls.CounterSheetView.UserDataFolderName);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
