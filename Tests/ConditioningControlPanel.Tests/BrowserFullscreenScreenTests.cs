using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>A browser video in fullscreen sits on the content screen whoever asked for it (owner
/// 2026-11-22, ccp-bugs #1720): the effects follow that screen, so a hand fullscreen left on the
/// panel's screen had them drawing on another monitor.</summary>
public class BrowserFullscreenScreenTests
{
    [Fact]
    public void Hand_and_takeover_fullscreen_both_use_the_content_screen()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "ConditioningControlPanel", "Resources"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "ConditioningControlPanel", "MainWindow", "MainWindow.Browser.cs"));

        Assert.DoesNotContain("takeover ? App.GetPrimaryContentScreen() : null", source);
        Assert.Contains("var screen = App.GetPrimaryContentScreen()", source);
    }
}
