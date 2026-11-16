using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Tests;
using Xunit;

namespace CCP.Core.Tests;

public class ReviewWiringTests
{
    private static string Source(params string[] path) => File.ReadAllText(Path.Combine(new[] { SourceRoots.RepoRoot }.Concat(path).ToArray()));
    private static string WpfSubliminal() => Source("ConditioningControlPanel", "Services", "Subliminal", "SubliminalService.cs");
    private static string Section(string source, string start, string end)
    {
        int i = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(i >= 0, start);
        int j = source.IndexOf(end, i, StringComparison.Ordinal);
        Assert.True(j > i, end);
        return source.Substring(i, j - i);
    }

    [Fact]
    public void Wpf_launcher_door_installs_lockdown_check()
        => Assert.Contains("PanelDoorLink.IsLockedDown = () => App.Lockdown?.IsActive == true", Source("ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs"));

    [Fact]
    public void Wpf_launcher_door_installs_fallback()
        => Assert.Contains("PanelDoorLink.Fallback = () => Services.Launcher.LauncherHost.BackToLauncher()", Source("ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs"));

    [Fact]
    public void Wpf_afterglow_receives_the_shown_phrase()
        => Assert.Contains("Compositor.AfterglowLineLayer.WordShown(text)", Section(WpfSubliminal(), "internal void FlashPhrase(", "public void FlashSubliminalCustom("));

    [Fact]
    public void Wpf_afterglow_driver_stops_on_teardown()
        => Assert.Contains("_afterglow?.Stop()", Section(WpfSubliminal(), "private void TearDownSurfaces()", "public void StopOneShotSubliminals()"));

    [Fact]
    public void Wpf_afterglow_line_stops_on_teardown()
        => Assert.Contains("AfterglowLineLayer.StopAll", Section(WpfSubliminal(), "private void TearDownSurfaces()", "public void StopOneShotSubliminals()"));

    [Fact]
    public void Scheduler_rechecks_afterglow_before_show_and_still_rearms()
    {
        var src = Source("CCP.Core", "CoreSubliminal.cs");
        var tick = src.Substring(src.IndexOf("private static void OnTick(", StringComparison.Ordinal));
        Assert.Contains("SuperAccess.ReplacesBase(SuperEffect.Afterglow)", tick);
        Assert.Contains("lock (Gate) ArmLocked()", tick);
    }
}
