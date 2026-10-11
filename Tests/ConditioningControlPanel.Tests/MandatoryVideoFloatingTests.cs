using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Floating mandatory videos (7.1.6, a player asked): the same player in a small topmost window.
/// Full screen stays the default, and nothing that covers the screen on purpose may float.
/// </summary>
public class MandatoryVideoFloatingTests
{
    [Fact]
    public void Full_screen_is_the_default() =>
        Assert.False(new AppSettings().MandatoryVideoFloating);

    [Theory]
    // setting, strict, lockdown, chaos -> floats
    [InlineData(true, false, false, false, true)]
    [InlineData(false, false, false, false, false)]   // not asked for: full screen, as always
    [InlineData(true, true, false, false, false)]     // strict covers the screen on purpose
    [InlineData(true, false, true, false, false)]     // so does a Lockdown
    [InlineData(true, false, false, true, false)]     // a Chaos run draws over the full-screen clip
    [InlineData(true, true, true, true, false)]
    [InlineData(false, true, true, true, false)]
    public void Only_an_ordinary_video_floats(bool setting, bool strict, bool lockdown, bool chaos, bool expected) =>
        Assert.Equal(expected, MandatoryVideoPresentation.ShouldFloat(setting, strict, lockdown, chaos));

    [Fact]
    public void The_setting_alone_never_floats_a_locked_video()
    {
        // Every combination with a lock on stays full screen, whatever the setting says.
        foreach (var strict in new[] { false, true })
            foreach (var lockdown in new[] { false, true })
                foreach (var chaos in new[] { false, true })
                    if (strict || lockdown || chaos)
                        Assert.False(MandatoryVideoPresentation.ShouldFloat(true, strict, lockdown, chaos));
    }

    [Fact]
    public void The_floating_window_always_lands_wholly_on_the_screen()
    {
        var r = new Random(7);
        foreach (var percent in new[] { 15, 33, 60 })
            foreach (var (mw, mh) in new[] { (1920, 1040), (2560, 1400), (1280, 680), (3440, 1400), (1080, 1880), (400, 300), (300, 900) })
                for (var i = 0; i < 100; i++)
                {
                    var (x, y, w, h) = MandatoryVideoPresentation.FloatingRect(mw, mh, r, percent);
                    Assert.True(x >= 0 && y >= 0 && x + w <= mw && y + h <= mh, $"{mw}x{mh}: {x},{y} {w}x{h}");
                    Assert.True(w > 0 && h > 0 && w <= mw && h <= mh);
                    if (mw >= 240 && mh >= 135) Assert.True(w >= MandatoryVideoPresentation.MinWidth, $"{mw}x{mh}: {w} wide");
                }
    }

    /// <summary>Source pin: both engines ask ONE decision, once per clip, and the strict handlers,
    /// the grace pause and the teardown carry no floating branch of their own (a floating video
    /// runs the full-screen code for every stop rule).</summary>
    [Fact]
    public void Both_engines_take_the_one_decision_and_no_stop_rule_branches_on_it()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video");
        var main = File.ReadAllText(Path.Combine(dir, "VideoService.cs"));
        var browser = File.ReadAllText(Path.Combine(dir, "VideoService.Browser.cs"));
        var floating = File.ReadAllText(Path.Combine(dir, "VideoService.Floating.cs"));

        Assert.Contains("if (!forceLibVlc) DecidePresentation(strict);", main);
        Assert.Contains("MandatoryVideoPresentation.ShouldFloat(", floating);
        Assert.Contains("App.Lockdown?.IsActive == true", floating);
        Assert.Contains("FloatingBounds = IsFloating ? _floatBounds : null", browser);

        foreach (var method in new[] { "private void SetupStrictHandlers(", "public bool TryGracePauseFromPanic(",
                     "public bool TryEscapeFromGlobalKey(", "public void ForceCleanup(", "private void CloseAll(" })
        {
            var at = main.IndexOf(method, StringComparison.Ordinal);
            Assert.True(at >= 0, method);
            var next = main.IndexOf("\n        p", at + method.Length, StringComparison.Ordinal);   // next member
            var body = next > at ? main[at..next] : main[at..];
            Assert.DoesNotContain("IsFloating", body);
            Assert.DoesNotContain("_floatBounds", body);
        }
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
