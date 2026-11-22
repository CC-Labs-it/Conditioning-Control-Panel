using System;
using System.IO;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>A hand fullscreen goes to the content screen unless the panel's screen already shows
/// content (owner 2026-11-22, ccp-bugs #1720; review: "all monitors" must not jump the video away).</summary>
public class BrowserFullscreenScreenTests
{
    [Theory]
    [InlineData(@"\.\DISPLAY2", new[] { @"\.\DISPLAY1", @"\.\DISPLAY2" }, true)]   // all monitors: stays
    [InlineData(@"\.\DISPLAY2", new[] { @"\.\DISPLAY2" }, true)]                    // panel screen is the content screen
    [InlineData(@"\.\DISPLAY2", new[] { @"\.\DISPLAY1" }, false)]                   // content elsewhere: moves
    [InlineData(@"\.\display1", new[] { @"\.\DISPLAY1" }, true)]                    // device names ignore case
    [InlineData("", new[] { @"\.\DISPLAY1" }, false)]
    public void Stays_only_where_content_already_shows(string panel, string[] content, bool stays)
        => Assert.Equal(stays, BrowserFullscreenScreen.PanelShowsContent(panel, content));

    [Fact]
    public void The_host_asks_the_rule_for_hand_fullscreens_only()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "ConditioningControlPanel", "Resources"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "ConditioningControlPanel", "MainWindow", "MainWindow.Browser.cs"));
        Assert.Contains("!takeover && Services.Browser.BrowserFullscreenScreen.PanelShowsContent(", source);
        Assert.DoesNotContain("takeover ? App.GetPrimaryContentScreen() : null", source);
    }
}
