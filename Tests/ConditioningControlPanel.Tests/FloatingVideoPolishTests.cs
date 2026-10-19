using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Video;
using ConditioningControlPanel.Services.Video.Floating;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Floating video polish (owner, 2026-10-19): a floating video never clears the flashes, its
/// window is never seen black while the clip loads, and it opens and closes with a short animation
/// under full motion only.
/// </summary>
public class FloatingVideoPolishTests
{
    // ---- flashes ----

    [Fact]
    public void Only_a_full_screen_video_takes_the_flashes_down()
    {
        Assert.True(VideoFlashRule.StopsFlashes(floating: false));
        Assert.False(VideoFlashRule.StopsFlashes(floating: true));
    }

    [Fact]
    public void Neither_floating_path_stops_the_flash_service()
    {
        // The equal instances (browser) never call it; the LibVLC route goes through the prologue,
        // which asks the rule with the same floating decision DecidePresentation makes later.
        var instances = Read("Services", "Video", "VideoService.FloatingInstances.cs");
        Assert.DoesNotContain("Flash?.Stop()", instances);
        Assert.DoesNotContain("Flash.Stop()", instances);

        var main = Read("Services", "Video", "VideoService.cs");
        var gate = main.IndexOf("Video.VideoFlashRule.StopsFlashes(floating: FloatingWanted(strict))", StringComparison.Ordinal);
        Assert.True(gate > 0);
        var stop = main.IndexOf("App.Flash?.Stop();", gate, StringComparison.Ordinal);
        Assert.True(stop > gate && stop - gate < 200, "the prologue's Flash.Stop sits inside the rule's gate");
        Assert.Equal(1, Count(main, "App.Flash?.Stop()"));
    }

    // ---- first frame ----

    [Fact]
    public void A_window_is_shown_on_its_first_frame()
    {
        Assert.Equal(FloatingFirstFrame.Verdict.Reveal, FloatingFirstFrame.Decide(true, null, null));
        Assert.Equal(FloatingFirstFrame.Verdict.Reveal, FloatingFirstFrame.Decide(true, 10, 50));
    }

    [Fact]
    public void Playing_without_a_frame_report_counts_after_a_short_grace()
    {
        Assert.Equal(FloatingFirstFrame.Verdict.Wait, FloatingFirstFrame.Decide(false, FloatingFirstFrame.PlayingGraceMs - 1, 100));
        Assert.Equal(FloatingFirstFrame.Verdict.Reveal, FloatingFirstFrame.Decide(false, FloatingFirstFrame.PlayingGraceMs, 100));
    }

    [Fact]
    public void A_clip_with_no_frame_in_time_is_skipped_and_never_shown()
    {
        Assert.Equal(FloatingFirstFrame.Verdict.Wait, FloatingFirstFrame.Decide(false, null, FloatingFirstFrame.ReadyBudgetMs - 1));
        Assert.Equal(FloatingFirstFrame.Verdict.Skip, FloatingFirstFrame.Decide(false, null, FloatingFirstFrame.ReadyBudgetMs));
        // Before the page is even ready, the WebView2 start-up has its own longer watch.
        Assert.Equal(FloatingFirstFrame.Verdict.Wait, FloatingFirstFrame.Decide(false, null, null));
        Assert.Equal(4000, FloatingFirstFrame.ReadyBudgetMs);
    }

    [Fact]
    public void The_window_is_veiled_before_it_is_shown_and_its_buttons_come_with_the_reveal()
    {
        var src = Read("Services", "Video", "VideoService.FloatingInstances.cs");
        var veil = src.IndexOf("c.Surface.Veil();", StringComparison.Ordinal);
        var show = src.IndexOf("win.Show();", StringComparison.Ordinal);
        Assert.True(veil > 0 && show > veil, "veiled before the first show");
        Assert.Contains("case \"firstFrame\":", src);
        Assert.Contains("CloseInstance(c, \"no first frame in time\")", src);
        // The mute and close buttons wait for the window to be fully shown.
        var reveal = Member(src, "private void RevealInstance(");
        Assert.Contains("ShowInstanceControls(c.Window,", reveal);
        Assert.DoesNotContain("ShowInstanceControls", Between(src, "case \"playing\":", "case \"firstFrame\":"));
    }

    // ---- spawn and close look ----

    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, false)]   // Gentle visuals always caps here
    [InlineData(MotionLevel.Off, false)]
    public void Only_full_motion_animates(MotionLevel level, bool expected)
        => Assert.Equal(expected, FloatingSpawnLook.Animates(level));

    [Fact]
    public void The_reveal_grows_out_and_the_close_shrinks_in()
    {
        Assert.Equal(FloatingSpawnLook.RevealFrom, FloatingSpawnLook.RevealScale(0), 6);
        Assert.Equal(1.0, FloatingSpawnLook.RevealScale(FloatingSpawnLook.RevealMs), 6);
        Assert.Equal(1.0, FloatingSpawnLook.RevealScale(FloatingSpawnLook.RevealMs * 3), 6);
        // Ease-out: past half way at the half time.
        Assert.True(FloatingSpawnLook.RevealScale(FloatingSpawnLook.RevealMs / 2.0) > (FloatingSpawnLook.RevealFrom + 1) / 2);

        Assert.Equal(1.0, FloatingSpawnLook.VanishScale(0), 6);
        Assert.Equal(0.0, FloatingSpawnLook.VanishScale(FloatingSpawnLook.VanishMs), 6);
        // Ease-in: still mostly there at the half time.
        Assert.True(FloatingSpawnLook.VanishScale(FloatingSpawnLook.VanishMs / 2.0) > 0.5);
        Assert.InRange(FloatingSpawnLook.RevealMs, 200, 300);
    }

    [Fact]
    public void The_visible_rectangle_is_centred()
    {
        Assert.Equal((0, 0, 400, 300), FloatingSpawnLook.Visible(400, 300, 1));
        Assert.Equal((100, 75, 300, 225), FloatingSpawnLook.Visible(400, 300, 0.5));
        var (l, t, r, b) = FloatingSpawnLook.Visible(400, 300, 0);
        Assert.Equal(l, r);
        Assert.Equal(t, b);
    }

    [Fact]
    public void The_frame_animates_the_region_never_opacity_or_an_effect()
    {
        var frame = Read("Services", "Video", "Floating", "FloatingVideoFrame.cs");
        Assert.DoesNotContain("SetLayeredWindowAttributes", frame);
        Assert.DoesNotContain(".Opacity", frame);
        Assert.DoesNotContain("Effect =", frame);
        Assert.Contains("SetWindowRgn(_hwnd, rgn, true)", frame);
    }

    [Fact]
    public void Panic_and_every_stop_close_a_closing_window_at_once()
    {
        var src = Read("Services", "Video", "VideoService.FloatingInstances.cs");
        var closeAll = Member(src, "private void CloseFloatingInstances(");
        Assert.Contains("if (!_spareVanishing)", closeAll);
        Assert.Contains("_vanishing.Values", closeAll);
        // Only the run's own natural end spares the last window's animation.
        Assert.Contains("_spareVanishing = true;", Member(src, "private void EndFloatingRun("));
        // Only natural ends and the limit animate.
        Assert.Equal(2, Count(src, "animate: true)"));
    }

    private static int Count(string s, string what)
    {
        int n = 0, at = 0;
        while ((at = s.IndexOf(what, at, StringComparison.Ordinal)) >= 0) { n++; at += what.Length; }
        return n;
    }

    private static string Between(string s, string from, string to)
    {
        var a = s.IndexOf(from, StringComparison.Ordinal);
        var b = s.IndexOf(to, a + 1, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a, from);
        return s[a..b];
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
