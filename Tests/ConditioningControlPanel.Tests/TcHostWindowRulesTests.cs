using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The T&amp;C window's pure rules: the init frame, the host frames, the watchdog and the close
/// handshake (CONTRACT.md section 2: answer in 300 ms, force at 800 ms).</summary>
public class TcHostWindowRulesTests
{
    [Fact]
    public void Init_carries_protocol_one_and_the_v2_caps()
    {
        var init = TcHostRules.InitFrame("it", MotionLevel.Full, gentle: false, BuiltInMods.CCPDefaultId,
            TcRules.Caps("full", saveSlot: false));
        Assert.Equal("init", (string?)init["type"]);
        Assert.Equal(1, (int)init["protocol"]!);
        Assert.Equal("it", (string?)init["lang"]);
        Assert.False((bool)init["reduced"]!);
        Assert.Equal("full", (string?)init["motion"]);
        Assert.Equal("default", (string?)init["mod"]);
        var caps = (JObject)init["caps"]!;
        Assert.Equal("full", (string?)caps["edition"]);
        Assert.Equal("desktop", (string?)caps["platform"]);
        Assert.False((bool)caps["saveSlot"]!);
    }

    [Fact]
    public void Later_slices_go_out_off()
    {
        var caps = (JObject)TcHostRules.InitFrame(null, MotionLevel.Full, false, null, TcRules.Caps("small", false))["caps"]!;
        Assert.False((bool)caps["scrolller"]!["consent"]!);
        Assert.False((bool)caps["sparkles"]!);
        Assert.False((bool)caps["toys"]!);
        Assert.False((bool)caps["toysConnected"]!);
        Assert.Equal("circe", (string?)caps["words"]!["bank"]);
        Assert.Null(caps["expo"]);
    }

    [Fact]
    public void Init_does_not_change_the_caps_it_was_given()
    {
        var v2 = TcRules.Caps("small", false);
        TcHostRules.InitFrame("en", MotionLevel.Full, false, null, v2);
        Assert.Null(v2["sparkles"]);
    }

    [Theory]
    [InlineData(MotionLevel.Full, false, "full")]
    [InlineData(MotionLevel.Reduced, true, "reduced")]
    [InlineData(MotionLevel.Off, true, "off")]
    public void Motion_reads_into_reduced(MotionLevel level, bool reduced, string wire)
    {
        var init = TcHostRules.InitFrame("en", level, false, null, TcRules.Caps("small", false));
        Assert.Equal(reduced, (bool)init["reduced"]!);
        Assert.Equal(wire, (string?)init["motion"]);
    }

    [Theory]
    [InlineData(BuiltInMods.BambiSleepId, "bambi")]
    [InlineData(BuiltInMods.SissyHypnoId, "sissy")]
    [InlineData(BuiltInMods.DronificationId, "drone")]
    [InlineData(BuiltInMods.LockedId, "locked")]
    [InlineData(BuiltInMods.CCPDefaultId, "default")]
    [InlineData("community-mod", "default")]
    [InlineData(null, "default")]
    public void Mods_map_to_the_page_ids(string? id, string wire) => Assert.Equal(wire, TcHostRules.ModWire(id));

    [Fact]
    public void Blank_language_reads_as_english()
        => Assert.Equal("en", (string?)TcHostRules.InitFrame(" ", MotionLevel.Full, false, null, TcRules.Caps("small", false))["lang"]);

    [Theory]
    [InlineData(false, 44, 0, "Keep")]
    [InlineData(false, 45, 0, "Close")]
    [InlineData(true, 500, 89, "Keep")]
    [InlineData(true, 0, 90, "Close")]
    public void Watchdog(bool ready, int sinceAny, int sinceBeat, string expected)
        => Assert.Equal(expected, TcHostRules.Watch(ready, TimeSpan.FromSeconds(sinceAny), TimeSpan.FromSeconds(sinceBeat)).ToString());

    [Fact]
    public void Close_handshake_ends_on_exit_done_or_at_800_ms()
    {
        Assert.True(TcHostRules.CloseNow(true, TimeSpan.Zero));
        Assert.False(TcHostRules.CloseNow(false, TimeSpan.FromMilliseconds(300)));
        Assert.False(TcHostRules.CloseNow(false, TimeSpan.FromMilliseconds(799)));
        Assert.True(TcHostRules.CloseNow(false, TimeSpan.FromMilliseconds(800)));
        Assert.Equal(TimeSpan.FromMilliseconds(300), TcHostRules.CloseAnswer);
    }

    [Fact]
    public void Host_frames_follow_the_contract()
    {
        var s = TcHostRules.Suspend(true, "panic");
        Assert.Equal("suspend", (string?)s["type"]);
        Assert.True((bool)s["on"]!);
        Assert.Equal("panic", (string?)s["reason"]);
        Assert.Equal("focus", (string?)TcHostRules.Suspend(false, "whatever")["reason"]);
        var c = TcHostRules.Close("back");
        Assert.Equal("close", (string?)c["type"]);
        Assert.Equal("back", (string?)c["reason"]);
    }

    // ---- source pins: the window is never started in a test ----

    private static string HostSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services", "Tc", "TcHostService.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Tc", "TcHostService.cs"));
    }

    [Fact]
    public void The_window_is_the_shared_host_and_every_frame_passes_the_gate()
    {
        var src = HostSource();
        Assert.Contains("new ChaosWebViewHost(", src);          // ScrolllerWebGate rides along
        Assert.Contains("IsGame = true", src);
        Assert.Contains("TcFrameGate.Check(o)", src);
        Assert.DoesNotContain("EnsureCoreWebView2Async", src);
    }

    [Fact]
    public void Panic_close_never_waits_on_the_page()
    {
        var src = HostSource();
        int start = src.IndexOf("public static void Close()", StringComparison.Ordinal);
        int end = src.IndexOf("public static void RequestClose", start, StringComparison.Ordinal);
        var body = src[start..end];
        Assert.Contains("TcHostRules.Suspend(true, TcHostRules.ReasonPanic)", body);
        Assert.Contains("h.Dispose()", body);
        Assert.DoesNotContain("await", body);
    }

    [Fact]
    public void The_dev_web_root_is_debug_only()
    {
        var src = HostSource();
        int env = src.IndexOf("GetEnvironmentVariable(\"CCP_TC_WEB_ROOT\")", StringComparison.Ordinal);
        Assert.True(env > 0 && env > src.IndexOf("#if DEBUG", StringComparison.Ordinal));
        Assert.True(env < src.IndexOf("#endif", StringComparison.Ordinal));
    }
}
