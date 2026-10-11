using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Floating videos, juice pass (owner, 2026-10-11): size, how many, exactly one sound, a Test
/// button that adds a window. With the defaults nothing changes from the first cut.
/// </summary>
public class FloatingVideoMultiTests
{
    // ---- the one sound ----

    [Fact]
    public void The_last_spawned_window_has_the_sound()
    {
        var f = new FloatingAudioFocus<string>();
        Assert.Null(f.Holder);
        f.Add("primary");
        Assert.Equal("primary", f.Holder);
        f.Add("b");
        f.Add("c");
        Assert.Equal("c", f.Holder);
        AssertExactlyOne(f);
    }

    [Fact]
    public void A_touched_window_takes_the_sound()
    {
        var f = Focus("primary", "b", "c");
        Assert.True(f.Touch("primary"));
        Assert.Equal("primary", f.Holder);
        Assert.False(f.Touch("primary"));        // already has it: nothing changes
        Assert.False(f.Touch("never-opened"));   // unknown: nothing changes
        Assert.Equal("primary", f.Holder);
        AssertExactlyOne(f);
    }

    [Fact]
    public void When_the_holder_closes_the_most_recent_survivor_takes_the_sound()
    {
        var f = Focus("primary", "b", "c");
        f.Touch("b");                              // recency now: primary, c, b
        Assert.True(f.Remove("b"));
        Assert.Equal("c", f.Holder);               // most recently spawned survivor
        f.Touch("primary");                        // recency: c, primary
        Assert.False(f.Remove("c"));               // a silent window closing moves nothing
        Assert.Equal("primary", f.Holder);
        Assert.True(f.Remove("primary"));
        Assert.Null(f.Holder);
        Assert.Equal(0, f.Count);
    }

    [Fact]
    public void Exactly_one_window_has_the_sound_through_any_sequence()
    {
        var r = new Random(11);
        var f = new FloatingAudioFocus<string>();
        var up = new List<string>();
        for (var i = 0; i < 2000; i++)
        {
            var roll = r.Next(3);
            if (roll == 0 || up.Count == 0) { var n = "w" + i; up.Add(n); f.Add(n); }
            else if (roll == 1) f.Touch(up[r.Next(up.Count)]);
            else { var n = up[r.Next(up.Count)]; up.Remove(n); f.Remove(n); }
            Assert.Equal(up.Count, f.Count);
            if (up.Count > 0) AssertExactlyOne(f); else Assert.Null(f.Holder);
        }
    }

    private static FloatingAudioFocus<string> Focus(params string[] items)
    {
        var f = new FloatingAudioFocus<string>();
        foreach (var i in items) f.Add(i);
        return f;
    }

    private static void AssertExactlyOne(FloatingAudioFocus<string> f) =>
        Assert.Equal(1, f.Items.Count(f.HasSound));

    // ---- options ----

    [Fact]
    public void Defaults_are_the_first_cut_one_medium_window()
    {
        var s = new AppSettings();
        Assert.Equal(1, s.MandatoryVideoFloatingCount);
        Assert.Equal("medium", s.MandatoryVideoFloatingSize);
        var a = MandatoryVideoPresentation.FloatingRect(1920, 1040, new Random(3));
        var b = MandatoryVideoPresentation.FloatingRect(1920, 1040, new Random(3),
            MandatoryVideoPresentation.SizeDivisor(s.MandatoryVideoFloatingSize), MandatoryVideoPresentation.PrimaryMinWidth, null);
        Assert.Equal(a, b);
        Assert.Equal((640, 360), (a.W, a.H));
    }

    [Fact]
    public void The_settings_refuse_values_outside_the_options()
    {
        var s = new AppSettings { MandatoryVideoFloatingCount = 9, MandatoryVideoFloatingSize = "huge" };
        Assert.Equal(4, s.MandatoryVideoFloatingCount);
        Assert.Equal("medium", s.MandatoryVideoFloatingSize);
        s.MandatoryVideoFloatingCount = 0;
        Assert.Equal(1, s.MandatoryVideoFloatingCount);
        s.MandatoryVideoFloatingSize = "large";
        Assert.Equal("large", s.MandatoryVideoFloatingSize);
    }

    [Theory]
    [InlineData("small", 480, 270)]
    [InlineData("medium", 640, 360)]
    [InlineData("large", 960, 540)]
    [InlineData(null, 640, 360)]
    public void Size_is_a_share_of_the_work_area(string? size, int w, int h)
    {
        var r = MandatoryVideoPresentation.FloatingRect(1920, 1040, new Random(1),
            MandatoryVideoPresentation.SizeDivisor(size), MandatoryVideoPresentation.PrimaryMinWidth, null);
        Assert.Equal((w, h), (r.W, r.H));
    }

    [Fact]
    public void Every_window_lands_wholly_on_the_screen_and_new_ones_do_not_start_stacked()
    {
        var r = new Random(5);
        foreach (var size in new[] { "small", "medium", "large" })
            foreach (var (mw, mh) in new[] { (1920, 1040), (2560, 1400), (1280, 680), (400, 300) })
            {
                var taken = new List<(int X, int Y, int W, int H)>();
                for (var i = 0; i < MandatoryVideoPresentation.MaxWindows; i++)
                {
                    var (x, y, w, h) = MandatoryVideoPresentation.FloatingRect(mw, mh, r, MandatoryVideoPresentation.SizeDivisor(size),
                        i == 0 ? MandatoryVideoPresentation.PrimaryMinWidth : MandatoryVideoPresentation.CompanionMinWidth, taken);
                    Assert.True(x >= 0 && y >= 0 && x + w <= mw && y + h <= mh && w > 0 && h > 0, $"{size} {mw}x{mh}: {x},{y} {w}x{h}");
                    taken.Add((x, y, w, h));
                }
            }

        // Room for two small windows side by side: the second never covers the first.
        for (var i = 0; i < 100; i++)
        {
            var first = MandatoryVideoPresentation.FloatingRect(2560, 1400, r, 4, 480, null);
            var second = MandatoryVideoPresentation.FloatingRect(2560, 1400, r, 4, 320, new[] { first });
            var overlapW = Math.Min(first.X + first.W, second.X + second.W) - Math.Max(first.X, second.X);
            var overlapH = Math.Min(first.Y + first.H, second.Y + second.H) - Math.Max(first.Y, second.Y);
            Assert.False(overlapW > 0 && overlapH > 0, $"{first} / {second}");
        }
    }

    // ---- source pins: stops, mute, Test, UI ----

    [Fact]
    public void Every_stop_closes_the_companions_and_they_never_count_as_the_video()
    {
        var main = Source("Services", "Video", "VideoService.cs");
        var companions = Source("Services", "Video", "VideoService.Companions.cs");

        // CloseAll is the funnel every teardown reaches; the floating layer goes before its
        // duplicate-call early return.
        var closeAll = Body(main, "private void CloseAll(");
        var layer = closeAll.IndexOf("CloseFloatingLayer(reason);", StringComparison.Ordinal);
        Assert.True(layer >= 0 && layer < closeAll.IndexOf("lock (_cleanupLock)", StringComparison.Ordinal));
        Assert.Contains("CloseFloatingCompanions(", Body(main, "public void ForceCleanup("));
        Assert.Contains("CloseFloatingCompanions(", Body(main, "private void Cleanup("));
        Assert.Contains("CloseFloatingCompanions(", Body(main, "public void Stop("));
        Assert.Contains("SetCompanionsPaused(true)", Body(main, "private void DoGracePause("));
        Assert.Contains("SetCompanionsPaused(false)", Body(main, "private void ResumeFromGrace("));

        // A companion is tracked in its own list only and never holds the playing state.
        Assert.DoesNotContain("_windows.Add", companions);
        Assert.DoesNotContain("_videoPlaying = true", companions);
        Assert.DoesNotContain("_mediaPlayers.Add", companions);
        Assert.DoesNotContain("Duck(", companions);
        Assert.Contains("if (!IsFloating || !_videoPlaying || _isCleaningUp || _gracePaused) return false;", companions);
    }

    [Fact]
    public void A_LibVLC_window_is_silenced_by_its_own_track_never_by_mute_or_volume()
    {
        var companions = Source("Services", "Video", "VideoService.Companions.cs");
        Assert.Contains("player.SetAudioTrack(-1)", companions);
        Assert.DoesNotContain(".Mute =", companions);
        Assert.DoesNotContain(".Volume =", companions);
        // Every window goes through the frame contract, with a mute delegate.
        Assert.Contains("FloatingVideoFrame.Attach(win, SetPrimaryFocusMuted)", companions);
        Assert.Contains("FloatingVideoFrame.Attach(win);", companions);
        // The frame can only add silence: a companion is loaded with the real volume, unmuted.
        Assert.Contains("muted = eff <= 0,", companions);
        Assert.DoesNotContain("muted = true", companions);
        Assert.Contains("surface.StartMotion(", companions);
        Assert.Contains("surface.Detach()", companions);
    }

    [Fact]
    public void Test_adds_a_floating_window_before_the_stuck_video_dialog()
    {
        var page = Source("Features", "VideoFeatureControl.xaml.cs");
        var click = Body(page, "private void BtnTestVideo_Click(");
        var add = click.IndexOf("if (App.Video.TryAddFloatingTestVideo()) return;", StringComparison.Ordinal);
        var dialog = click.IndexOf("A video appears to be playing", StringComparison.Ordinal);
        Assert.True(add >= 0 && dialog > add);

        var service = Source("Services", "Video", "VideoService.Companions.cs");
        var tryAdd = Body(service, "public bool TryAddFloatingTestVideo(");
        Assert.Contains("!IsFloating", tryAdd);                      // full screen keeps the dialog
        Assert.Contains("if (room <= 0", tryAdd);                    // so does one window only
        Assert.Contains("CloseCompanion(_companions[0]", tryAdd);    // at the limit the oldest makes room
    }

    [Fact]
    public void The_options_show_only_for_a_floating_window_and_every_language_has_the_keys()
    {
        var xaml = Source("Features", "VideoFeatureControl.xaml");
        Assert.Contains("<StackPanel x:Name=\"PanelFloatingOptions\" Visibility=\"Collapsed\">", xaml);
        Assert.True(xaml.IndexOf("x:Name=\"CmbAppearance\"", StringComparison.Ordinal) < xaml.IndexOf("PanelFloatingOptions", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("PanelFloatingOptions", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"FoldMore\"", StringComparison.Ordinal));
        Assert.Contains("PanelFloatingOptions.Visibility = s.MandatoryVideoFloating ? Visibility.Visible : Visibility.Collapsed;",
            Source("Features", "VideoFeatureControl.xaml.cs"));

        var keys = new[] { "setting_video_appearance_size", "tooltip_video_appearance_size", "video_appearance_size_small",
            "video_appearance_size_medium", "video_appearance_size_large", "setting_video_appearance_count", "tooltip_video_appearance_count" };
        var files = Directory.GetFiles(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages"), "*.json");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var key in keys) Assert.Contains("\"" + key + "\":", text);
        }
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    /// <summary>From a member's signature to the next member at the same indent.</summary>
    private static string Body(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        var next = source.IndexOf("\n        p", at + signature.Length, StringComparison.Ordinal);
        return next > at ? source[at..next] : source[at..];
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
