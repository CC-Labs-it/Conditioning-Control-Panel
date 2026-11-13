using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using static ConditioningControlPanel.Services.Safety.PanicPolicy;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1594 #1595: with panic on F12 and pause on F11, Escape typed anywhere paused a
/// FLOATING mandatory video and the next Escape closed it, even after a resume. A floating video
/// sits beside the user's other apps, so Escape is a video key there only when Escape IS the panic
/// key. A full-screen video keeps its hardcoded Escape dismiss, and the panic route is unchanged.
/// </summary>
public class FloatingVideoEscapeTests
{
    [Theory]
    [InlineData("F12")]
    [InlineData("F8")]
    [InlineData(null)]
    [InlineData("")]
    public void FloatingVideo_IgnoresEscape_WhenEscapeIsNotThePanicKey(string? panicKey)
        => Assert.False(VideoTakesEscape(floating: true, panicKeyEnabled: true, panicKey: panicKey));

    [Fact]
    public void FloatingVideo_IgnoresEscape_WhenThePanicKeyIsOff()
        => Assert.False(VideoTakesEscape(floating: true, panicKeyEnabled: false, panicKey: "Escape"));

    /// <summary>Panic is never weaker: Escape as the panic key still reaches a floating video.</summary>
    [Theory]
    [InlineData("Escape")]
    [InlineData(" escape ")]
    public void FloatingVideo_KeepsEscape_WhenEscapeIsThePanicKey(string panicKey)
        => Assert.True(VideoTakesEscape(floating: true, panicKeyEnabled: true, panicKey: panicKey));

    [Theory]
    [InlineData(true, "F12")]
    [InlineData(true, "Escape")]
    [InlineData(false, "F12")]
    [InlineData(false, "Escape")]
    public void FullScreenVideo_KeepsItsEscapeDismiss(bool panicKeyEnabled, string panicKey)
        => Assert.True(VideoTakesEscape(floating: false, panicKeyEnabled: panicKeyEnabled, panicKey: panicKey));

    /// <summary>
    /// With Escape as the panic key, a floating video still wants the global Escape, so the hook's
    /// decision is today's: the press goes to the panic handler, not to the video dismiss.
    /// </summary>
    [Fact]
    public void EscapeAsPanicKey_OverAFloatingVideo_StillPanics()
    {
        bool wants = VideoTakesEscape(floating: true, panicKeyEnabled: true, panicKey: "Escape");
        Assert.False(EscapeDismissesVideo(videoWantsEscape: wants, lockCardOpen: false, paletteOpen: false,
            panicKeyEnabled: true, panicKey: "Escape", lockdownActive: false));
    }

    /// <summary>Every Escape door into a non-strict video asks the rule: the global hook, the
    /// LibVLC window and the browser page.</summary>
    [Fact]
    public void EveryEscapeDoor_AsksTheRule()
    {
        var video = Read("VideoService.cs");
        Assert.Matches(new Regex(@"WantsGlobalEscape\s*=>[^;]*VideoTakesEscape\(\)"), video);
        Assert.Contains("if (e.Key == Key.Escape && !VideoTakesEscape()) return;", video);

        var browser = Read("VideoService.Browser.cs");
        Assert.Contains("if (isEscape && !VideoTakesEscape()) return;", browser);

        var floating = Read("VideoService.Floating.cs");
        Assert.Matches(new Regex(@"FloatingOnScreen\s*=>\s*IsFloating\s*\|\|\s*\(_floatRunOpen && _floaters\.Count > 0\)"), floating);
    }

    private static string Read(string file)
        => File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
