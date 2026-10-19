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
    public void Mute_shows_on_every_video_strict_ones_too_but_never_inside_a_chaos_run()
    {
        foreach (var strict in new[] { false, true })
            foreach (var lockdown in new[] { false, true })
            {
                Assert.True(MandatoryVideoControls.ShowMute(strict, lockdown, chaosRunning: false));
                // The run's HUD owns the corner and the DtRH dive holds its own mute.
                Assert.False(MandatoryVideoControls.ShowMute(strict, lockdown, chaosRunning: true));
            }
    }

    [Fact]
    public void A_chaos_run_gets_no_buttons_and_a_refused_X_goes_away()
    {
        var controls = Read("Services", "Video", "VideoService.Controls.cs");
        Assert.Contains("if (!ControlsAllowed(strict)) return;", Member(controls, "private void ShowVideoControls("));
        Assert.Contains("MandatoryVideoControls.ShowMute(", Member(controls, "private bool ControlsAllowed("));
        Assert.Contains("HideCloseButtons();", Member(controls, "private void CloseVideoFromButton("));
        Assert.Contains("HideCloseButtons();", Member(controls, "private void ShowInstanceControls("));
        Assert.Contains("if (!c.IsClosed) _videoControls.Add(c);", controls);
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
        Assert.Contains("ResetUserMute();", Member(main, "public void Stop()"));   // engine stop, session pause, remote stop

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
    public void A_mute_press_during_teardown_does_nothing_and_CloseAll_takes_the_buttons_first()
    {
        var controls = Read("Services", "Video", "VideoService.Controls.cs");
        Assert.Contains("if (!_videoPlaying || _isCleaningUp) return;", Member(controls, "private void ToggleUserMute("));
        var closeAll = Member(Read("Services", "Video", "VideoService.cs"), "private void CloseAll(");
        var at = closeAll.IndexOf("CloseVideoControls();", StringComparison.Ordinal);
        Assert.True(at >= 0 && at < closeAll.IndexOf("GentleCurtain", StringComparison.Ordinal), "CloseAll must close the buttons first");
    }

    [Fact]
    public void A_DtRH_run_counts_as_a_chaos_run_for_float_buttons_and_the_credit_link()
    {
        var floating = Read("Services", "Video", "VideoService.Floating.cs");
        var instances = Read("Services", "Video", "VideoService.FloatingInstances.cs");
        var browser = Read("Services", "Video", "VideoService.Browser.cs");
        var credit = Read("Services", "Video", "ScrolllerCredit.cs");
        foreach (var src in new[] { floating, instances })
        {
            Assert.Contains("LockedChaosRun());", src);
            Assert.DoesNotContain("App.Chaos?.IsRunning == true);", src);
        }
        foreach (var src in new[] { instances, browser })
            foreach (var line in src.Split('\n').Where(l => l.Contains("CanOpenLink(") || l.Contains("App.Lockdown?.IsActive == true, App.Chaos")))
                Assert.DoesNotContain("App.Chaos?.IsRunning", line);
        Assert.Contains("VideoService.LockedChaosRun()", credit);
        Assert.Contains("DtrhHostService.IsRunActive", Read("Services", "Video", "VideoService.Controls.cs"));
        Assert.Contains("if (!ControlsAllowed(strict: false)) return;", Member(Read("Services", "Video", "VideoService.Controls.cs"), "private void ShowInstanceControls("));
    }

    [Fact]
    public void A_Lockdown_that_starts_mid_video_takes_the_X_down()
    {
        var controls = Read("Services", "Video", "VideoService.Controls.cs");
        Assert.Contains("lockdown.LockdownActivated += OnLockdownActivated;", controls);
        Assert.Contains("HideCloseButtons();", Member(controls, "private void OnLockdownActivated("));
        Assert.Contains("WatchForLocks();", Member(controls, "private void AttachVideoControls("));
    }

    [Fact]
    public void The_buttons_stay_directly_above_their_own_window_never_above_other_videos()
    {
        var overlay = Read("Windows", "VideoControlsOverlay.cs");
        Assert.DoesNotContain("Topmost = true", overlay);
        Assert.Contains("Topmost = host.Topmost", overlay);
        Assert.Contains("GetWindow(host, GW_HWNDPREV)", overlay);
        Assert.Contains("WM_WINDOWPOSCHANGED", overlay);
        Assert.Contains("Restack();", Member(overlay, "private void TryShow("));
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
