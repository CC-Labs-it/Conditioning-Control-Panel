using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Floating videos (owner, 2026-10-12): one trigger = one window, "how many" is only the most on
/// screen, every window an equal instance with its own lifetime, no attention checks, size and
/// speed sliders, exactly one sound, a Test button that always adds one window.
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

    // ---- the model: one trigger = one window, "how many" is only the limit ----

    [Theory]
    [InlineData(0, 1, false, "Open")]
    [InlineData(1, 3, false, "Open")]
    [InlineData(2, 3, false, "Open")]
    [InlineData(3, 3, false, "Skip")]
    [InlineData(1, 1, false, "Skip")]
    [InlineData(4, 9, false, "Skip")]
    [InlineData(0, 3, true, "Open")]
    [InlineData(2, 3, true, "Open")]
    [InlineData(3, 3, true, "ReplaceOldest")]
    [InlineData(1, 1, true, "ReplaceOldest")]
    public void A_trigger_opens_one_window_under_the_limit_and_only_Test_replaces_at_it(
        int up, int limit, bool testPress, string expected) =>
        Assert.Equal(expected, MandatoryVideoPresentation.DecideTrigger(up, limit, testPress).ToString());

    [Fact]
    public void One_trigger_opens_exactly_one_window_and_there_is_no_primary()
    {
        var inst = Source("Services", "Video", "VideoService.FloatingInstances.cs");
        var main = Source("Services", "Video", "VideoService.cs");
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video", "VideoService.Companions.cs")));
        Assert.DoesNotContain("Companion", inst);

        // Each door spawns once: no loop fills the screen up to "how many", unless "Pop up
        // together" is on (SpawnTogetherExtras does nothing while it is off).
        Assert.Equal(1, Count(Body(inst, "private bool TryStartFloatingRun("), "SpawnInstance("));
        var add = Body(inst, "private bool AddFloatingInstance(");
        Assert.Equal(1, Count(add, "SpawnInstance("));
        Assert.Contains("if (!FloatingTogether()) return 0;", Body(inst, "private int SpawnTogetherExtras("));
        Assert.Contains("MandatoryVideoPresentation.DecideTrigger(_floaters.Count, FloatingLimit(), replaceOldestAtLimit)", add);
        Assert.DoesNotContain("while (_floaters.Count <", inst);

        // A trigger while windows are up is taken BEFORE the queue and the force-close of the
        // video that is playing, or a second window could never open.
        var trigger = Body(main, "public void TriggerVideo(");
        var hook = trigger.IndexOf("if (TryFloatingTrigger(strictOverride, userInitiated)) return;", StringComparison.Ordinal);
        Assert.True(hook >= 0 && hook < trigger.IndexOf("ForceCleanup();", StringComparison.Ordinal));
        // The first window of a run opens from PlayVideo, after the "already playing" refusal.
        Assert.Contains("if (!isVoutRetry && TryStartFloatingRun(path, strict)) return;", Body(main, "private void PlayVideo("));
        // The scheduler keeps ticking through a floating run.
        Assert.Contains("if (_isRunning && SchedulerMayTrigger && !_triggerInProgress)", main);
        Assert.Contains("private bool SchedulerMayTrigger => !_videoPlaying || (_floatRunOpen && _floaters.Count > 0 && !FloatingTogether());", inst);
        // Strict, Lockdown and Chaos never float, at either door.
        Assert.Contains("if (!FloatingWanted(strict)) return false;", Body(inst, "private bool TryStartFloatingRun("));
        Assert.Contains("if (!FloatingWanted(", Body(inst, "private bool TryFloatingTrigger("));
    }

    [Fact]
    public void Each_window_has_its_own_lifetime_and_only_the_last_ends_the_run()
    {
        var inst = Source("Services", "Video", "VideoService.FloatingInstances.cs");
        var close = Body(inst, "private void CloseInstance(");
        Assert.Contains("_floaters.Remove(c);", close);
        Assert.Contains("if (endRun && _floaters.Count == 0) EndFloatingRun(", close);
        Assert.DoesNotContain("CloseFloatingInstances(", close);          // one window takes nothing with it
        Assert.Contains("CloseInstance(c, \"clip ended\", watchedToEnd: true, animate: true)", inst);
        // The run ends through the ordinary Cleanup: holds lift, the queue slot frees, VideoEnded.
        Assert.Contains("if (_videoPlaying && !_isCleaningUp) Cleanup();", Body(inst, "private void EndFloatingRun("));
        // A clip that played to its end counts as a watched video.
        var credit = Body(inst, "private void CreditInstance(");
        Assert.Contains("TrackVideoWatched(", credit);
        Assert.Contains("VideoWatchCredited?.Invoke(", credit);
        Assert.Contains("if (watchedToEnd) App.Chaster?.Note(\"video\");", credit);
    }

    // ---- no attention checks ----

    [Fact]
    public void A_floating_video_has_no_attention_checks_on_either_engine()
    {
        Assert.True(MandatoryVideoPresentation.AttentionChecksApply(true, floating: false));
        Assert.False(MandatoryVideoPresentation.AttentionChecksApply(true, floating: true));
        Assert.False(MandatoryVideoPresentation.AttentionChecksApply(false, floating: false));

        var main = Source("Services", "Video", "VideoService.cs");
        var browser = Source("Services", "Video", "VideoService.Browser.cs");
        var inst = Source("Services", "Video", "VideoService.FloatingInstances.cs");
        // Every arming site and the verdict ask the one rule; an instance never arms anything.
        Assert.Equal(1, Count(main, "SetupAttention();"));
        Assert.Contains("AttentionChecksApply(App.Settings.Current.AttentionChecksEnabled, IsFloating))\n", main.Replace("\r\n", "\n"));
        Assert.Contains("AttentionChecksApply(settings.AttentionChecksEnabled, IsFloating) && _spawned > 0", main);
        Assert.Equal(1, Count(browser, "SetupAttention();"));
        Assert.Contains("AttentionChecksApply(App.Settings.Current.AttentionChecksEnabled, IsFloating)) SetupAttention();", browser);
        Assert.DoesNotContain("SetupAttention", inst);
        Assert.DoesNotContain("_targets", inst);
        Assert.DoesNotContain("ShowMessage(", inst);
    }

    // ---- options ----

    [Fact]
    public void Defaults_are_one_window_a_third_wide_at_a_lazy_drift()
    {
        var s = new AppSettings();
        Assert.Equal(1, s.MandatoryVideoFloatingCount);
        Assert.Equal(33, s.MandatoryVideoFloatingSizePercent);
        Assert.Equal(35, s.MandatoryVideoFloatingSpeedPercent);
        var a = MandatoryVideoPresentation.FloatingRect(1920, 1040, new Random(3), s.MandatoryVideoFloatingSizePercent);
        Assert.Equal((633, 356), (a.W, a.H));
    }

    [Fact]
    public void The_settings_refuse_values_outside_the_ranges()
    {
        var s = new AppSettings { MandatoryVideoFloatingCount = 9, MandatoryVideoFloatingSizePercent = 90, MandatoryVideoFloatingSpeedPercent = 400 };
        Assert.Equal(4, s.MandatoryVideoFloatingCount);
        Assert.Equal(60, s.MandatoryVideoFloatingSizePercent);
        Assert.Equal(100, s.MandatoryVideoFloatingSpeedPercent);
        s.MandatoryVideoFloatingCount = 0;
        s.MandatoryVideoFloatingSizePercent = 1;
        s.MandatoryVideoFloatingSpeedPercent = 0;
        Assert.Equal(1, s.MandatoryVideoFloatingCount);
        Assert.Equal(15, s.MandatoryVideoFloatingSizePercent);
        Assert.Equal(10, s.MandatoryVideoFloatingSpeedPercent);
    }

    [Theory]
    [InlineData("small", 25)]
    [InlineData("medium", 33)]
    [InlineData("large", 50)]
    public void An_old_saved_size_is_carried_over_once(string old, int percent)
    {
        var s = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>("{\"MandatoryVideoFloatingSize\":\"" + old + "\"}")!;
        Assert.Equal(percent, s.MandatoryVideoFloatingSizePercent);
        // It reads back as nothing, so a later save can never bring the old choice back over the slider.
        Assert.Null(s.MandatoryVideoFloatingSize);
        s.MandatoryVideoFloatingSizePercent = 41;
        var again = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
        Assert.Equal(41, again.MandatoryVideoFloatingSizePercent);
        s.MandatoryVideoFloatingSize = "huge";
        Assert.Equal(41, s.MandatoryVideoFloatingSizePercent);
    }

    [Theory]
    [InlineData(15, 288, 162)]
    [InlineData(25, 480, 270)]
    [InlineData(50, 960, 540)]
    [InlineData(60, 1152, 648)]
    [InlineData(5, 288, 162)]
    public void Size_is_a_percent_of_the_work_area_width(int percent, int w, int h)
    {
        var r = MandatoryVideoPresentation.FloatingRect(1920, 1040, new Random(1), percent);
        Assert.Equal((w, h), (r.W, r.H));
    }

    [Fact]
    public void Every_window_lands_wholly_on_the_screen_and_never_exactly_on_another()
    {
        var r = new Random(5);
        foreach (var percent in new[] { 15, 33, 60 })
            foreach (var (mw, mh) in new[] { (1920, 1040), (2560, 1400), (1280, 680), (400, 300) })
            {
                var taken = new List<(int X, int Y, int W, int H)>();
                for (var i = 0; i < MandatoryVideoPresentation.MaxWindows; i++)
                {
                    var (x, y, w, h) = MandatoryVideoPresentation.FloatingRect(mw, mh, r, percent, taken);
                    Assert.True(x >= 0 && y >= 0 && x + w <= mw && y + h <= mh && w > 0 && h > 0, $"{percent} {mw}x{mh}: {x},{y} {w}x{h}");
                    taken.Add((x, y, w, h));
                }
            }

        // Overlap is allowed, a pile is not: with room to step, a new window is never within a
        // few pixels of one already up, even when the dice land it right there.
        for (var seed = 0; seed < 200; seed++)
        {
            var first = MandatoryVideoPresentation.FloatingRect(1920, 1040, new Random(seed), 33);
            var second = MandatoryVideoPresentation.FloatingRect(1920, 1040, new Random(seed), 33, new[] { first });
            Assert.False(Math.Abs(first.X - second.X) < MandatoryVideoPresentation.PileOffset
                         && Math.Abs(first.Y - second.Y) < MandatoryVideoPresentation.PileOffset, $"{first} / {second}");
        }
    }

    // ---- source pins: stops, mute, Test, UI ----

    [Fact]
    public void Every_stop_closes_every_floating_window()
    {
        var main = Source("Services", "Video", "VideoService.cs");
        var inst = Source("Services", "Video", "VideoService.FloatingInstances.cs");

        // CloseAll is the funnel every teardown reaches; the floating layer goes before its
        // duplicate-call early return.
        var closeAll = Body(main, "private void CloseAll(");
        var layer = closeAll.IndexOf("CloseFloatingLayer(reason);", StringComparison.Ordinal);
        Assert.True(layer >= 0 && layer < closeAll.IndexOf("lock (_cleanupLock)", StringComparison.Ordinal));
        Assert.Contains("CloseFloatingInstances(reason);", Body(inst, "private void CloseFloatingLayer("));
        Assert.Contains("CloseFloatingInstances(", Body(main, "public void ForceCleanup("));
        Assert.Contains("CloseFloatingInstances(", Body(main, "private void Cleanup("));
        Assert.Contains("CloseFloatingInstances(", Body(main, "public void Stop("));
        Assert.Contains("SetFloatingPaused(true)", Body(main, "private void DoGracePause("));
        Assert.Contains("SetFloatingPaused(false)", Body(main, "private void ResumeFromGrace("));
        Assert.Contains("FloatingScreens()", Body(main, "private void ShowGraceOverlays("));

        // A stop closes ALL of them and takes the run away first, so no closing window can
        // start a second Cleanup; nothing is left in the list afterwards.
        var all = Body(inst, "private void CloseFloatingInstances(");
        Assert.True(all.IndexOf("_floatRunOpen = false;", StringComparison.Ordinal) < all.IndexOf("foreach (var c in _floaters.ToList())", StringComparison.Ordinal));
        Assert.Contains("CloseInstance(c, reason, endRun: false, refocus: false);", all);

        // No window opens into a stop, a pause or a run that is over, and an instance is tracked
        // in its own list only.
        Assert.Contains("if (!_floatRunOpen || !_videoPlaying || _isCleaningUp || _gracePaused) return false;", inst);
        Assert.Contains("if (!_videoPlaying) CloseFloatingInstances(", Body(inst, "private async Task InitInstanceAsync("));
        Assert.DoesNotContain("_windows.Add", inst);
        Assert.DoesNotContain("_mediaPlayers.Add", inst);
    }

    [Fact]
    public void A_LibVLC_window_is_silenced_by_its_own_track_never_by_mute_or_volume()
    {
        var inst = Source("Services", "Video", "VideoService.FloatingInstances.cs");
        Assert.Contains("player.SetAudioTrack(-1)", inst);
        Assert.DoesNotContain(".Mute =", inst);
        Assert.DoesNotContain(".Volume =", inst);
        // Every window goes through the frame contract, with a mute delegate.
        Assert.Contains("FloatingVideoFrame.Attach(win, SetPrimaryFocusMuted)", inst);
        Assert.Contains("FloatingVideoFrame.Attach(win);", inst);
        // The frame can only add silence: a window is loaded with the real volume, unmuted.
        Assert.Contains("muted = eff <= 0,", inst);
        Assert.DoesNotContain("muted = true", inst);
        // Speed at spawn and live from the slider; motion starts on the monitor it opened on.
        var adopt = Body(inst, "private void AdoptSurface(");
        Assert.Contains("surface.MotionSpeed = FloatingMotionSpeed();", adopt);
        Assert.Contains("surface.StartMotion(", adopt);
        Assert.Contains("s.MotionSpeed = speed;", Body(inst, "public void RefreshFloatingMotionSpeed("));
        Assert.Contains("surface.Detach()", inst);
    }

    [Fact]
    public void Test_adds_one_floating_window_and_never_asks_in_floating_mode()
    {
        var page = Source("Features", "VideoFeatureControl.xaml.cs");
        var click = Body(page, "private void BtnTestVideo_Click(");
        var add = click.IndexOf("if (App.Video.TryAddFloatingTestVideo()) return;", StringComparison.Ordinal);
        var dialog = click.IndexOf("A video appears to be playing", StringComparison.Ordinal);
        Assert.True(add >= 0 && dialog > add);

        var service = Source("Services", "Video", "VideoService.FloatingInstances.cs");
        var tryAdd = Body(service, "public bool TryAddFloatingTestVideo(");
        Assert.Contains("AddFloatingInstance(replaceOldestAtLimit: true);", tryAdd);   // at the limit the oldest makes room
        Assert.Contains("if (!IsFloating) return false;", tryAdd);                     // full screen keeps the dialog
        Assert.DoesNotContain("room <= 0", tryAdd);                                    // "how many" 1 is no exception
        Assert.Contains("CloseInstance(_floaters[0], \"the oldest makes room\", endRun: false);", service);
    }

    [Fact]
    public void The_options_show_only_for_a_floating_window_and_every_language_has_the_keys()
    {
        var xaml = Source("Features", "VideoFeatureControl.xaml");
        var code = Source("Features", "VideoFeatureControl.xaml.cs");
        Assert.Contains("<StackPanel x:Name=\"PanelFloatingOptions\" Visibility=\"Collapsed\">", xaml);
        Assert.True(xaml.IndexOf("x:Name=\"CmbAppearance\"", StringComparison.Ordinal) < xaml.IndexOf("PanelFloatingOptions", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("PanelFloatingOptions", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"FoldMore\"", StringComparison.Ordinal));
        Assert.Contains("PanelFloatingOptions.Visibility = s.MandatoryVideoFloating ? Visibility.Visible : Visibility.Collapsed;", code);
        // Size and speed are sliders on the contract's ranges; the three-way size choice is gone.
        Assert.Contains("x:Name=\"SliderFloatingSize\" Minimum=\"15\" Maximum=\"60\" Value=\"33\"", xaml);
        Assert.Contains("x:Name=\"SliderFloatingSpeed\" Minimum=\"10\" Maximum=\"100\" Value=\"35\"", xaml);
        Assert.DoesNotContain("CmbFloatingSize", xaml);
        Assert.Contains("{loc:Str video_appearance_no_checks_note}", xaml);
        Assert.Contains("App.Video?.RefreshFloatingMotionSpeed();", Body(code, "private void SliderFloatingSpeed_Changed("));

        var keys = new[] { "setting_video_appearance_size", "tooltip_video_appearance_size", "setting_video_appearance_speed",
            "tooltip_video_appearance_speed", "video_appearance_no_checks_note", "setting_video_appearance_count", "tooltip_video_appearance_count" };
        var gone = new[] { "video_appearance_size_small", "video_appearance_size_medium", "video_appearance_size_large" };
        var files = Directory.GetFiles(SourceRoots.LanguagesDirectory, "*.json");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var key in keys) Assert.Contains("\"" + key + "\":", text);
            foreach (var key in gone) Assert.DoesNotContain("\"" + key + "\":", text);
        }
    }

    private static int Count(string text, string what)
    {
        var n = 0;
        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal)) n++;
        return n;
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
