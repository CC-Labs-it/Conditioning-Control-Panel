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
        Assert.Equal(FloatingFirstFrame.Verdict.Reveal, FloatingFirstFrame.Decide(true, null, 0, null));
        Assert.Equal(FloatingFirstFrame.Verdict.Reveal, FloatingFirstFrame.Decide(true, 10, 0, 50));
    }

    [Fact]
    public void Without_a_frame_report_the_clip_must_have_played_a_while_before_it_is_shown()
    {
        var grace = FloatingFirstFrame.PlayingGraceMs;
        var moved = FloatingFirstFrame.AdvanceMs;
        Assert.Equal(FloatingFirstFrame.Verdict.Wait, FloatingFirstFrame.Decide(false, grace - 1, moved, 100));
        Assert.Equal(FloatingFirstFrame.Verdict.Reveal, FloatingFirstFrame.Decide(false, grace, moved, 100));
        // "Playing" alone is not a painted picture: a clip stuck on its first frame waits.
        Assert.Equal(FloatingFirstFrame.Verdict.Wait, FloatingFirstFrame.Decide(false, grace * 5, moved - 1, 100));
        Assert.True(grace >= 1000 && moved >= 300);
    }

    [Fact]
    public void A_clip_with_no_frame_in_time_is_skipped_and_never_shown()
    {
        Assert.Equal(FloatingFirstFrame.Verdict.Wait, FloatingFirstFrame.Decide(false, null, 0, FloatingFirstFrame.ReadyBudgetMs - 1));
        Assert.Equal(FloatingFirstFrame.Verdict.Skip, FloatingFirstFrame.Decide(false, null, 0, FloatingFirstFrame.ReadyBudgetMs));
        // Before the page is even ready, the WebView2 start-up has its own longer watch.
        Assert.Equal(FloatingFirstFrame.Verdict.Wait, FloatingFirstFrame.Decide(false, null, 0, null));
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

    [Fact]
    public void A_playing_clip_is_never_skipped_and_is_shown_by_the_budget_even_without_proof()
    {
        var budget = FloatingFirstFrame.ReadyBudgetMs;
        Assert.Equal(FloatingFirstFrame.Verdict.Wait, FloatingFirstFrame.Decide(false, 0, 0, budget - 1));
        // A page that stops reporting while cloaked: lifting the cloak is what lets it paint.
        Assert.Equal(FloatingFirstFrame.Verdict.Reveal, FloatingFirstFrame.Decide(false, 0, 0, budget));
        Assert.Equal(FloatingFirstFrame.Verdict.Reveal, FloatingFirstFrame.Decide(false, budget, 0, null));
    }

    [Fact]
    public void A_veiled_window_takes_no_sound_until_it_is_shown()
    {
        var src = Read("Services", "Video", "VideoService.FloatingInstances.cs");
        var spawn = Member(src, "private bool SpawnInstance(");
        Assert.Contains("AdoptSurface(c.Surface, screen, joinAudio: false);", spawn);
        Assert.Contains("c.Surface.SetMuted(true);", spawn);
        Assert.DoesNotContain("ApplyAudioFocus();", spawn);
        var reveal = Member(src, "private void RevealInstance(");
        Assert.Contains("_audioFocus.Add(c.Surface);", reveal);
        Assert.Contains("ApplyAudioFocus();", reveal);
        // A grace pause holds the first-frame clock.
        Assert.Contains("if (_gracePaused)", Member(src, "private void CheckReveal("));
        // The fallback counts played time from "playing" (a seek is not progress).
        Assert.Contains("var at = (long?)o[\"playedMs\"] ?? 0;", src);
        Assert.Contains("c.FirstTimeMs < 0 ? 0 : c.LastTimeMs - c.FirstTimeMs,", Member(src, "private void CheckReveal("));
        // Closing mid-reveal never shows the full window.
        Assert.Contains("if (_detached) return;", Member(Read("Services", "Video", "Floating", "FloatingVideoFrame.cs"), "private void EndVeil("));
    }

    [Fact]
    public void The_flash_rule_is_asked_again_with_the_real_presentation_and_the_presence_follows()
    {
        var main = Read("Services", "Video", "VideoService.cs");
        var start = Member(main, "private void StartVideoPlayback(");
        var decide = start.IndexOf("if (!forceLibVlc) DecidePresentation(strict);", StringComparison.Ordinal);
        var again = start.IndexOf("Video.VideoFlashRule.StopsFlashes(floating: IsFloating) && App.Flash?.IsRunning == true) App.Flash.Stop();", StringComparison.Ordinal);
        Assert.True(decide > 0 && again > decide);
        // Flash.Start returns early on a service a floating video never stopped: the presence is set by hand.
        Assert.Contains("if (flashWasRunning) App.DiscordRpc?.SetFlashActivity();", Member(main, "private void Cleanup("));
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
        // The veil is a DWM cloak (composed off screen, no clicks), lifted before the region grows;
        // a refused cloak falls back on one pixel, never an empty region while the page loads.
        Assert.Contains("DWMWA_CLOAK = 13", frame);
        var reveal = Member(frame, "public void Reveal(");
        Assert.True(reveal.IndexOf("Uncloak();", StringComparison.Ordinal) < reveal.IndexOf("Animate(", StringComparison.Ordinal));
        Assert.Contains("CreateRectRgn(w / 2, h / 2, w / 2 + 1, h / 2 + 1)", frame);
        // Every detach (close, skip, panic) before the reveal empties the window, then uncloaks it.
        Assert.Contains("LiftCloakHidden();", Member(frame, "public void Detach("));
        var lift = Member(frame, "private void LiftCloakHidden(");
        Assert.True(lift.IndexOf("CreateRectRgn(0, 0, 0, 0)", StringComparison.Ordinal) < lift.IndexOf("ApplyCloak();", StringComparison.Ordinal));
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
