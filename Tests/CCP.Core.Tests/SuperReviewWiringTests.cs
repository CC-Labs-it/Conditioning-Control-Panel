using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class SuperReviewWiringTests
{
    private static string Read(params string[] path) => File.ReadAllText(Path.Combine(SourceRoots.RepoRoot, Path.Combine(path)));

    [Fact]
    public void WpfPanelDoorChecksLockdownAndHasFallback()
    {
        var src = Read("ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs");
        Assert.Contains("PanelDoorLink.IsLockedDown = () => App.Lockdown?.IsActive == true", src);
        Assert.Contains("PanelDoorLink.Fallback = () =>", src);
    }

    [Fact]
    public void WpfAfterglowReceivesPhrasesAndStopsWithSurfaces()
    {
        var src = Read("ConditioningControlPanel", "Services", "Subliminal", "SubliminalService.cs");
        var phrase = src.Substring(src.IndexOf("internal void FlashPhrase("), src.IndexOf("public void FlashSubliminalCustom(") - src.IndexOf("internal void FlashPhrase("));
        var teardown = src.Substring(src.IndexOf("private void TearDownSurfaces()"), src.IndexOf("public void StopOneShotSubliminals()") - src.IndexOf("private void TearDownSurfaces()"));
        Assert.Contains("AfterglowLineLayer.WordShown(text)", phrase);
        Assert.Contains("_afterglow?.Stop()", teardown);
        Assert.Contains("AfterglowLineLayer.StopAll", teardown);
    }
}
