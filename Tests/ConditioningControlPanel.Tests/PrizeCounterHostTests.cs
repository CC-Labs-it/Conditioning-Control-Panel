using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.PrizeCounter;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Prize Counter window: panic closes it (GameSurfaces), a kept Escape is the page's back only once
/// it is up, the init motion is the page's three levels, the browser argument string is constant and
/// the shared WebView2 host (which attaches ScrolllerWebGate) is the one in use, and the wallet chip is its door.
/// </summary>
[Collection(GameSurfacesCollection.Name)]
public class PrizeCounterHostTests
{
    [Theory]
    [InlineData(MotionLevel.Full, false, "full")]
    [InlineData(MotionLevel.Full, true, "less")]
    [InlineData(MotionLevel.Reduced, false, "less")]
    [InlineData(MotionLevel.Off, true, "off")]
    public void Motion_IsThePagesThreeLevels(MotionLevel m, bool gentle, string wire)
        => Assert.Equal(wire, PrizeCounterHostService.MotionWire(m, gentle));

    [Fact]
    public void TheCounter_IsOnThePanicRegistry()
        => Assert.Contains(PrizeCounterHostService.GameId, GameSurfaces.Ids());

    [Fact]
    public void AKeptEscape_GoesToTheCounterOnlyWhenItIsUp()
    {
        Assert.Equal(PanicPolicy.KeptEscapePage.Counter, PanicPolicy.KeptEscapeGoesTo(true,
            false, false, false, false, false, false, counterInFront: true, counterReady: true));
        Assert.Equal(PanicPolicy.KeptEscapePage.None, PanicPolicy.KeptEscapeGoesTo(true,
            false, false, false, false, false, false, counterInFront: true, counterReady: false));
        Assert.Equal(PanicPolicy.KeptEscapePage.None, PanicPolicy.KeptEscapeGoesTo(false,
            false, false, false, false, false, false, counterInFront: true, counterReady: true));
    }

    [Fact]
    public void TheEscapeClaim_CountsTheCounterAsAGameInFront()
    {
        var body = Read("ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs");
        Assert.Contains("|| intakeInFront || counterInFront,", body);
        Assert.Contains("PrizeCounterHostService.PostKeptEscape(); break;", body);
    }

    [Fact]
    public void TheHost_UsesAConstantBrowserArgument_AndTheSharedWebViewHost()
    {
        var src = Read("ConditioningControlPanel", "Services", "PrizeCounter", "PrizeCounterHostService.cs");
        Assert.Contains("new ChaosWebViewHost(", src);
        Assert.Contains("ExtraBrowserArguments = BrowserArguments,", src);
        Assert.DoesNotContain("EnsureCoreWebView2Async", src);
        // The page plays the kept press (host-escape.js), wired before the page mounts.
        var page = Read("ConditioningControlPanel", "Resources", "web", "prizecounter", "index.html");
        Assert.True(page.IndexOf("createHostEscape()", StringComparison.Ordinal) < page.IndexOf("mountPrizeCounter(createHost()", StringComparison.Ordinal));
    }

    [Fact]
    public void TheWalletChip_OpensTheCounter()
    {
        var src = Read("ConditioningControlPanel", "MainWindow", "MainWindow.SparkleWallet.cs");
        Assert.Contains("PrizeCounterHostService.Launch(from: \"wallet\")", src);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
