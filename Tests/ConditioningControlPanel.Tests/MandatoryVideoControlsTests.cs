using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The mute and close buttons on a mandatory video (owner, 2026-10-19): the X is the Esc door with
/// a mouse and hides under every lock; mute ends nothing and is always there.
/// </summary>
public class MandatoryVideoControlsTests
{
    [Theory]
    // strict, lockdown, chaos -> X shown
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]    // Strict Lock, Lock Card, Possession, a leash
    [InlineData(false, true, false, false)]    // a Lockdown
    [InlineData(false, false, true, false)]    // a Chaos or Down the Rabbit Hole run
    [InlineData(true, true, true, false)]
    public void The_close_button_hides_under_every_lock(bool strict, bool lockdown, bool chaos, bool expected) =>
        Assert.Equal(expected, MandatoryVideoControls.ShowClose(strict, lockdown, chaos));

    [Fact]
    public void Mute_shows_on_every_video_strict_ones_too()
    {
        foreach (var strict in new[] { false, true })
            foreach (var lockdown in new[] { false, true })
                foreach (var chaos in new[] { false, true })
                    Assert.True(MandatoryVideoControls.ShowMute(strict, lockdown, chaos));
    }

    [Fact]
    public void The_X_hides_exactly_where_a_video_covers_the_screen_on_purpose()
    {
        // Same list as the floating rule: whatever may not float may not be closed either.
        foreach (var strict in new[] { false, true })
            foreach (var lockdown in new[] { false, true })
                foreach (var chaos in new[] { false, true })
                    Assert.Equal(MandatoryVideoPresentation.ShouldFloat(true, strict, lockdown, chaos),
                        MandatoryVideoControls.ShowClose(strict, lockdown, chaos));
    }

    [Fact]
    public void The_X_goes_through_the_Esc_dismiss_and_never_a_stop()
    {
        var controls = Read("Services", "Video", "VideoService.Controls.cs");
        var body = Member(controls, "private void CloseVideoFromButton(");
        Assert.Contains("if (!CloseAllowed(strict))", body);
        Assert.Contains("Cleanup();", body);
        Assert.DoesNotContain("ForceCleanup", body);
        Assert.DoesNotContain("Stop()", body);
        Assert.DoesNotContain("OnEnded", body);   // a close is a skip, never a natural end (no XP)

        var instance = Member(controls, "private void ShowInstanceControls(");
        Assert.Contains("CloseInstanceOf(win, \"close button\")", instance);
        Assert.DoesNotContain("watchedToEnd: true", instance);

        Assert.Contains("MandatoryVideoControls.ShowClose(strict || IsStrictActive, App.Lockdown?.IsActive == true, LockedChaosRun())", controls);
        Assert.Contains("App.Chaos?.IsRunning == true || ConditioningControlPanel.Services.Chaos.DtrhHostService.IsRunActive", controls);
    }

    [Fact]
    public void Mute_rides_the_effective_volume_and_never_the_mixer()
    {
        var main = Read("Services", "Video", "VideoService.cs");
        Assert.Contains("if (_externalMute || _userMuted) return 0;", main);
        Assert.Contains("ResetUserMute();", Member(main, "private void Cleanup("));
        Assert.Contains("ResetUserMute();", Member(main, "public void ForceCleanup("));

        foreach (var src in new[] { Read("Services", "Video", "VideoService.Controls.cs"), Read("Windows", "VideoControlsOverlay.cs") })
        {
            Assert.DoesNotContain("waveOutSetVolume", src);
            Assert.DoesNotContain("WaveOutEvent", src);
            Assert.DoesNotContain(".Volume =", src);
            Assert.DoesNotContain("SetExternalMute", src);   // the DtRH dive's switch stays its own
        }
        var toggle = Member(Read("Services", "Video", "VideoService.Controls.cs"), "private void ToggleUserMute(");
        Assert.Contains("UpdatePlayingVideosVolume();", toggle);
    }

    [Fact]
    public void The_buttons_are_an_owned_window_that_never_takes_focus()
    {
        var overlay = Read("Windows", "VideoControlsOverlay.cs");
        Assert.Contains("_win.Owner = _host", overlay);
        Assert.Contains("ShowActivated = false", overlay);
        Assert.Contains("WS_EX_NOACTIVATE", overlay);
        Assert.Contains("MA_NOACTIVATE", overlay);
        Assert.Contains("GentleCurtain.Covers(_host)", overlay);   // never over a black curtain
    }

    [Fact]
    public void Every_engine_puts_the_buttons_up_once_the_clip_plays()
    {
        Assert.Contains("ShowVideoControls(strict);", Read("Services", "Video", "VideoService.cs"));   // LibVLC + MediaElement
        Assert.Contains("ShowVideoControls(_browserStrict,", Read("Services", "Video", "VideoService.Browser.cs"));
        Assert.Contains("ShowInstanceControls(c.Window,", Read("Services", "Video", "VideoService.FloatingInstances.cs"));
    }

    [Fact]
    public void The_tooltips_exist_in_every_language()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            foreach (var key in new[] { "video_btn_mute", "video_btn_unmute", "video_btn_close" })
                Assert.True(text.Contains($"\"{key}\":"), $"{Path.GetFileName(f)} lacks {key}");
        }
    }

    private static string Member(string src, string signature)
    {
        var at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        var next = src.IndexOf("\n        p", at + signature.Length, StringComparison.Ordinal);
        return next > at ? src[at..next] : src[at..];
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
