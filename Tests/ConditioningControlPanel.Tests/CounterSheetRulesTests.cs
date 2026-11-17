using System;
using System.IO;
using System.Windows;
using ConditioningControlPanel.Services.PrizeCounter;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The 2D counter sheet (owner 2026-11-17): which door drops it, where it sits, and the wallet's second press.</summary>
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
    public void TheSheetHangsFromTheDoorsOwnWindow(string from, bool launcherUp, bool panelUp, string door)
        => Assert.Equal(door, CounterSheetRules.PickDoor(from, launcherUp, panelUp).ToString());

    [Fact]
    public void Bounds_RunFromUnderTheBar_ToNearTheBottom_EdgeToEdge()
    {
        var client = new Rect(100, 50, 1200, 800);
        var r = CounterSheetRules.Bounds(client, barBottom: 98, Rect.Empty, bottomInset: 8, minWidth: 360, minHeight: 240);
        Assert.NotNull(r);
        Assert.Equal(new Int32Rect(100, 98, 1200, 800 + 50 - 98 - 8), r!.Value);
    }

    [Fact]
    public void Bounds_AreClampedToTheMonitor_ForAMaximizedOverhang()
    {
        // A maximized borderless window overhangs its monitor by its resize border.
        var client = new Rect(-8, -8, 1936, 1096);
        var monitor = new Rect(0, 0, 1920, 1080);
        var r = CounterSheetRules.Bounds(client, barBottom: 52, monitor, bottomInset: 10, minWidth: 360, minHeight: 240);
        Assert.Equal(new Int32Rect(0, 52, 1920, 1080 - 52 - 10), r!.Value);
    }

    [Fact]
    public void Bounds_NeverStartAboveTheClient()
    {
        var r = CounterSheetRules.Bounds(new Rect(0, 200, 800, 600), barBottom: 10, Rect.Empty, 0, 360, 240);
        Assert.Equal(200, r!.Value.Y);
    }

    [Theory]
    [InlineData(800, 300, 240, true)]     // room
    [InlineData(800, 290, 240, false)]    // 300 - 48 bar - 8 inset < 240: the window opens instead
    [InlineData(300, 600, 240, false)]    // too narrow
    public void Bounds_RefuseAWindowWithNoRoom(double w, double h, double minH, bool fits)
    {
        var r = CounterSheetRules.Bounds(new Rect(0, 0, w, h), barBottom: 48, Rect.Empty, 8, 360, minH);
        Assert.Equal(fits, r != null);
    }

    [Fact]
    public void Bounds_RefuseNonsense()
    {
        Assert.Null(CounterSheetRules.Bounds(Rect.Empty, 10, Rect.Empty, 8, 360, 240));
        Assert.Null(CounterSheetRules.Bounds(new Rect(0, 0, 800, 600), double.NaN, Rect.Empty, 8, 360, 240));
        Assert.Null(CounterSheetRules.Bounds(new Rect(0, 0, 800, 600), 900, Rect.Empty, 8, 360, 240));
    }

    [Fact]
    public void TheSecondPress_ClosesTheSheet_AndThePressThatClosedItOpensNothing()
    {
        var t = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(CounterSheetRules.Press.Close, CounterSheetRules.OnPress(sheetOpen: true, null, t));
        Assert.Equal(CounterSheetRules.Press.Open, CounterSheetRules.OnPress(false, null, t));
        // The press-down on the chip took focus off the sheet (closed on deactivation); its click follows.
        Assert.Equal(CounterSheetRules.Press.Swallow, CounterSheetRules.OnPress(false, t, t.AddMilliseconds(180)));
        Assert.Equal(CounterSheetRules.Press.Open, CounterSheetRules.OnPress(false, t, t + CounterSheetRules.PressWindow + TimeSpan.FromMilliseconds(1)));
        Assert.Equal(CounterSheetRules.Press.Open, CounterSheetRules.OnPress(false, t, t.AddSeconds(-1)));
    }

    [Fact]
    public void TheSheetSpeaksTheSameSeam_OnItsOwnPage()
    {
        Assert.Equal("https://ccp.game/prizecounter/sheet.html", CounterSheetRules.SheetUrl);
        var src = Read("ConditioningControlPanel", "Services", "PrizeCounter", "PrizeCounterHostService.cs");
        Assert.Contains("if (_isSheet) init[\"sheet\"] = true;", src);
        Assert.Contains("StartUrl = _startUrl,", src);
        Assert.Contains("OnSheetLost = why => OnSheetLost(me, why),", src);
        // Reload and sign-in stay on the page that is up.
        Assert.DoesNotContain("NavigatePage(StartUrl,", src);
        var sheet = Read("ConditioningControlPanel", "Services", "PrizeCounter", "PrizeCounterHostService.Sheet.cs");
        Assert.Contains("if (why == \"deactivated\" && _signingIn) return;", sheet);
        Assert.Contains("CloseFor(CloseBack)", sheet);
    }

    [Fact]
    public void BothWalletWindows_AreSheetDoors()
    {
        Assert.Contains("ICounterSheetDoor.CounterSheetBar => TitleBar;",
            Read("ConditioningControlPanel", "Windows", "Launcher", "LauncherWindow.xaml.cs"));
        Assert.Contains("=> (HeaderSparkleWallet.Parent as FrameworkElement)?.Parent as FrameworkElement;",
            Read("ConditioningControlPanel", "MainWindow", "MainWindow.SparkleWallet.cs"));
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
        // The sheet never takes the game windows' topmost pulse.
        Assert.Contains("if (IsSheet) { _window.Activate(); _web?.Focus(); return; }",
            Read("ConditioningControlPanel", "Chaos", "ChaosWebViewHost.cs"));
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
