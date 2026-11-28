using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>T&amp;C toy pulses (after Just Drop) and the live <c>setting</c> frames.</summary>
public class TcFeelRulesTests
{
    [Theory]
    [InlineData(nameof(AppSettings.GentleVisuals), "gentle")]
    [InlineData(nameof(AppSettings.MotionLevel), "motion")]
    [InlineData(nameof(AppSettings.RemoteMediaConsented), "media.remoteConsent")]
    [InlineData(nameof(AppSettings.FypOnlineConsented), "media.remoteConsent")]
    [InlineData(nameof(AppSettings.MediaSource), "media.remoteConsent")]
    [InlineData(nameof(AppSettings.OfflineMode), "media.remoteConsent")]
    [InlineData(nameof(AppSettings.PanicKey), null)]
    [InlineData(null, null)]
    public void App_settings_map_to_the_contract_keys(string? property, string? key)
        => Assert.Equal(key, TcFeelRules.KeyFor(property));

    [Fact]
    public void The_setting_frame()
    {
        var f = TcFeelRules.SettingFrame("motion", "reduced");
        Assert.Equal("setting", (string?)f["type"]);
        Assert.Equal("motion", (string?)f["key"]);
        Assert.Equal("reduced", (string?)f["value"]);
        Assert.True((bool)TcFeelRules.SettingFrame("gentle", true)["value"]!);
    }

    [Theory]
    [InlineData(150, false, 150)]
    [InlineData(150, true, 300)]
    [InlineData(2000, true, 3000)]
    [InlineData(5000, false, 3000)]
    [InlineData(-1, false, 0)]
    public void Pulse_length(int ms, bool buttplug, int expected)
        => Assert.Equal(expected, TcFeelRules.DurationFor(ms, buttplug));

    [Theory]
    [InlineData(0.4, 150, true)]
    [InlineData(0.0, 150, false)]
    [InlineData(0.4, 0, false)]
    public void Only_a_real_pulse_plays(double level, int ms, bool expected)
        => Assert.Equal(expected, TcFeelRules.Plays(level, ms));

    [Fact]
    public void Toys_in_init_follow_the_host()
    {
        var caps = TcHostRules.InitFrame("en", MotionLevel.Full, false, null, TcRules.Caps("small", false), toys: true, toysConnected: true)["caps"]!;
        Assert.True((bool)caps["toys"]!);
        Assert.True((bool)caps["toysConnected"]!);
        var none = TcHostRules.InitFrame("en", MotionLevel.Full, false, null, TcRules.Caps("small", false), toys: false, toysConnected: true)["caps"]!;
        Assert.False((bool)none["toysConnected"]!);
    }

    private static string Src(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services", "Tc")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Tc", file));
    }

    [Fact]
    public void Pulses_sit_behind_the_access_gate_and_stop_on_close_and_suspend()
    {
        var feel = Src("TcHostService.Feel.cs");
        Assert.Contains("HapticsAccess.IsOpen", feel);
        Assert.Contains("PulseLimit.Accept(", feel);
        var host = Src("TcHostService.cs");
        int close = host.IndexOf("public static void Close()", StringComparison.Ordinal);
        int closeEnd = host.IndexOf("public static void RequestClose", close, StringComparison.Ordinal);
        Assert.Contains("StopPulse();", host[close..closeEnd]);
        Assert.Contains("WatchFeel(false);", host[close..closeEnd]);
        int card = host.IndexOf("private static void OpenTierCard()", StringComparison.Ordinal);
        Assert.Contains("StopPulse();", host[card..host.IndexOf("private static void OnWindowActivated", card, StringComparison.Ordinal)]);
    }
}
