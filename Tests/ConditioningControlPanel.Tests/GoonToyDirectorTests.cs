using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1429: the Goon host told every page "no toy" and dropped toy cues, with a
/// toy connected in CCP. The page now hears the real state and a cue reaches the toy, clamped.</summary>
public class GoonToyDirectorTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]   // haptics on, nothing connected
    [InlineData(false, true, false)]   // connected, master toggle off
    [InlineData(false, false, false)]
    public void The_page_hears_a_toy_only_when_one_can_play(bool enabled, bool connected, bool ready)
        => Assert.Equal(ready, GoonToyDirector.Ready(enabled, connected));

    [Fact]
    public void The_host_no_longer_answers_no_toy_to_every_page()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "GoonGame", "GoonHostService.cs"));
        Assert.DoesNotContain("haptics = false", src);
        Assert.Contains("haptics = GoonToyDirector.ReadyNow", src);
        Assert.Contains("GoonToyDirector.OnPattern(o)", src);
        Assert.Contains("\"haptics-state\"", src);
    }

    [Fact]
    public void A_cue_is_clamped_to_the_cap_it_carries()
    {
        var cue = GoonToyDirector.Parse(JObject.Parse("{\"intensity\":0.9,\"cap\":0.4,\"durationMs\":5000,\"pattern\":\"wave\"}"));
        Assert.NotNull(cue);
        Assert.Equal(0.4, cue!.Value.Level, 3);
        Assert.Equal(5000, cue.Value.Ms);
        Assert.Equal(VibrationMode.Wave, cue.Value.Mode);
    }

    [Fact]
    public void A_cue_with_no_cap_gets_the_default_cap()
    {
        var cue = GoonToyDirector.Parse(JObject.Parse("{\"intensity\":5}"));
        Assert.Equal(GoonToyDirector.DefaultCap, cue!.Value.Level, 3);
        Assert.Equal(GoonToyDirector.SustainMs, cue.Value.Ms);          // durationMs 0 = a sustained bed
        Assert.Equal(VibrationMode.Constant, cue.Value.Mode);
    }

    [Theory]
    [InlineData("{\"intensity\":0.8,\"cap\":0}")]            // the players agreed on zero
    [InlineData("{\"intensity\":0}")]
    [InlineData("{\"intensity\":\"1\"}")]                    // not a number
    [InlineData("{}")]
    public void A_cue_that_asks_for_nothing_plays_nothing(string json)
        => Assert.Null(GoonToyDirector.Parse(JObject.Parse(json)));

    [Fact]
    public void A_duration_stays_inside_the_wire_limits()
    {
        Assert.Equal(GoonToyDirector.MaxMs, GoonToyDirector.Parse(JObject.Parse("{\"intensity\":0.5,\"durationMs\":99999999}"))!.Value.Ms);
        Assert.Equal(GoonToyDirector.MinMs, GoonToyDirector.Parse(JObject.Parse("{\"intensity\":0.5,\"durationMs\":3}"))!.Value.Ms);
        Assert.Equal(GoonToyDirector.SustainMs, GoonToyDirector.Parse(JObject.Parse("{\"intensity\":0.5,\"durationMs\":-4}"))!.Value.Ms);
    }

    [Theory]
    [InlineData("pulse", VibrationMode.Pulse)]
    [InlineData("WAVE", VibrationMode.Wave)]
    [InlineData("fireworks", VibrationMode.Heartbeat)]
    [InlineData("earthquake", VibrationMode.Earthquake)]
    [InlineData("anything else", VibrationMode.Constant)]
    [InlineData(null, VibrationMode.Constant)]
    public void A_pattern_name_maps_to_a_known_mode(string? name, VibrationMode mode)
        => Assert.Equal(mode, GoonToyDirector.ModeFor(name));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services", "GoonGame")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
