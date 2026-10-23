using System;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The two player asks of the 7.1.6 features lane.
/// 1. "Pause other effects during Bubble Count" (#1418): off by default, and when on it is the
///    mandatory video's hold (flashes stopped, overlays pinned under the round, bouncing text
///    paused), never a second mechanism.
/// 2. The status line above START / STOP names the running session and its time left.
/// </summary>
public class Features716Tests
{
    // ---- 1. Bubble Count pause -----------------------------------------------------------

    [Fact]
    public void Pause_other_effects_is_off_by_default()
    {
        Assert.False(new AppSettings().BubbleCountPauseEffects);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]   // option off: a round changes nothing
    [InlineData(false, true, false)]   // no round: nothing is held
    [InlineData(false, false, false)]
    public void A_round_holds_the_effects_only_when_the_option_is_on(bool round, bool option, bool expected)
    {
        Assert.Equal(expected, BubbleCountHoldRule.ShouldHold(round, option));
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]  // the hold did not stop them, so it does not start them
    [InlineData(true, false, true, true, false)]  // force-closed by a stop or panic: stay stopped
    [InlineData(true, true, false, true, false)]  // engine no longer running
    [InlineData(true, true, true, false, false)]  // flashes switched off meanwhile
    public void Flashes_come_back_only_after_a_normal_end_on_a_running_engine(
        bool wereRunning, bool endedNormally, bool engineRunning, bool flashEnabled, bool expected)
    {
        Assert.Equal(expected,
            BubbleCountHoldRule.ShouldResumeFlashes(wereRunning, endedNormally, engineRunning, flashEnabled));
    }

    [Fact]
    public void The_hold_reuses_the_mandatory_video_path()
    {
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var service = File.ReadAllText(Path.Combine(app, "Services", "BubbleCountService.cs"));
        // Flashes: the same Stop going in and Start coming out that VideoService uses.
        Assert.Contains("App.Flash?.Stop();", service);
        Assert.Contains("App.Flash?.Start();", service);
        // Bouncing text: the video pause pair.
        Assert.Contains("App.BouncingText?.HoldForFullscreen();", service);
        Assert.Contains("App.BouncingText?.ReleaseFullscreenHold();", service);
        // A force-close (engine stop, panic) releases the hold without restarting anything.
        Assert.Contains("ExitEffectsHold(resumeFlashes: false);", service);

        var bouncing = File.ReadAllText(Path.Combine(app, "Services", "Subliminal", "BouncingTextService.cs"));
        Assert.Contains("HoldForFullscreen() => OnVideoStartedPause(", bouncing);
        Assert.Contains("OnVideoEndedResume(null, EventArgs.Empty);", bouncing);

        // Overlays: the reconciler that pins them under a video takes the round's windows as anchors.
        var overlay = File.ReadAllText(Path.Combine(app, "Services", "Notifications", "OverlayService.cs"));
        Assert.Contains("App.BubbleCount?.HoldsEffects == true", overlay);
        Assert.Contains("EffectsHoldWindowHandles()", overlay);
    }

    [Fact]
    public void The_switch_sits_on_the_Bubble_Count_settings()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Features",
            "BubbleCountFeatureControl.xaml"));
        Assert.Contains("x:Name=\"ChkPauseEffects\"", xaml);
        Assert.Contains("{loc:Str setting_bubble_count_pause_effects}", xaml);
        Assert.Contains("{loc:Str tooltip_bubble_count_pause_effects}", xaml);
    }

    // ---- 2. Session time in the bar ------------------------------------------------------

    private const string Left = "{0} - {1} left";
    private const string Elapsed = "{0} - {1} elapsed";

    [Fact]
    public void Bar_shows_the_session_name_and_the_time_left()
    {
        var line = SessionClockLabel.BarStatus("Deep Dive", TimeSpan.FromMinutes(5), new TimeSpan(0, 24, 9),
            openEnded: false, showCountdown: true, pauseIndicator: "", Left, Elapsed);
        Assert.Equal("Deep Dive - 24:09 left", line);
    }

    [Fact]
    public void Bar_shows_elapsed_time_for_an_open_ended_session()
    {
        var line = SessionClockLabel.BarStatus("Free Run", new TimeSpan(1, 12, 3), TimeSpan.Zero,
            openEnded: true, showCountdown: true, pauseIndicator: "", Left, Elapsed);
        Assert.Equal("Free Run - 72:03 elapsed", line);
    }

    [Fact]
    public void Bar_keeps_the_name_and_drops_the_clock_when_the_countdown_is_hidden()
    {
        var t = new TimeSpan(0, 3, 7);
        Assert.Equal("Deep Dive",
            SessionClockLabel.BarStatus("Deep Dive", t, t, false, showCountdown: false, "", Left, Elapsed));
        Assert.Equal("Deep Dive [PAUSED]",
            SessionClockLabel.BarStatus("Deep Dive", t, t, false, showCountdown: false, " [PAUSED]", Left, Elapsed));
    }

    [Fact]
    public void Bar_carries_the_pause_mark_after_the_clock()
    {
        var t = new TimeSpan(0, 3, 7);
        Assert.Equal("Deep Dive - 03:07 left [PAUSED]",
            SessionClockLabel.BarStatus("Deep Dive", t, t, false, true, " [PAUSED]", Left, Elapsed));
    }

    [Fact]
    public void Bar_trims_a_very_long_session_name()
    {
        var name = new string('x', 80);
        var line = SessionClockLabel.BarStatus(name, TimeSpan.Zero, TimeSpan.FromMinutes(1), false, true, "", Left, Elapsed);
        Assert.StartsWith(new string('x', SessionClockLabel.BarNameMax - 3) + "... - ", line);
        Assert.EndsWith("01:00 left", line);
    }

    [Fact]
    public void Bar_survives_a_broken_translation()
    {
        Assert.Equal("{0} - {1",
            SessionClockLabel.BarStatus("Deep Dive", TimeSpan.Zero, TimeSpan.Zero, false, true, "", "{0} - {1", Elapsed));
    }

    [Fact]
    public void The_bar_line_is_painted_from_the_session_tick_and_never_saves_settings()
    {
        var presets = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow",
            "MainWindow.Presets.cs"));
        var start = presets.IndexOf("private void RefreshBarStatus()", StringComparison.Ordinal);
        Assert.True(start >= 0, "RefreshBarStatus is gone");
        var end = presets.IndexOf("private void OnSessionPhaseChanged", start, StringComparison.Ordinal);
        var body = presets.Substring(start, end - start);
        Assert.Contains("SessionClockLabel.BarStatus(", body);
        Assert.DoesNotContain("Save(", body);

        var tick = presets.Substring(presets.IndexOf("private void OnSessionProgressUpdated", StringComparison.Ordinal));
        tick = tick.Substring(0, tick.IndexOf("private void RefreshBarStatus()", StringComparison.Ordinal));
        Assert.Contains("RefreshBarStatus();", tick);
        Assert.DoesNotContain("Save(", tick);
    }

    // ---- the new keys, in every language file --------------------------------------------

    private static readonly string[] Languages =
        { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static readonly string[] NewKeys =
    {
        "setting_bubble_count_pause_effects",
        "tooltip_bubble_count_pause_effects",
        "label_session_bar_left",
        "label_session_bar_elapsed",
    };

    [Fact]
    public void Every_language_carries_the_new_copy()
    {
        foreach (var lang in Languages)
        {
            var path = Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var key in NewKeys)
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out var value), lang + ".json is missing " + key);
                var text = value.GetString();
                Assert.False(string.IsNullOrWhiteSpace(text), lang + ".json has " + key + " blank");
                Assert.DoesNotContain("!", text);
                Assert.DoesNotContain("—", text);
                Assert.DoesNotContain("–", text);
            }
            foreach (var key in new[] { "label_session_bar_left", "label_session_bar_elapsed" })
            {
                var text = doc.RootElement.GetProperty(key).GetString()!;
                Assert.Contains("{0}", text);
                Assert.Contains("{1}", text);
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
