using System;
using System.IO;
using System.Reflection;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Ponytail debt pass 3 (2026-10-25): high-risk deferrals whose upgrade trigger had fired.</summary>
public sealed class PonytailDebt3Tests
{
    /// <summary>A bindable key whose name differs from its X keysym name used to fall through verbatim,
    /// resolve to keysym 0, and leave the panic key silently dead (X11PanicKey.cs, ledger 2026-10-23 c).</summary>
    [Theory]
    [InlineData("OemBackslash", "less")]
    [InlineData("Oem102", "less")]
    [InlineData("Apps", "Menu")]
    [InlineData("LWin", "Super_L")]
    [InlineData("RWin", "Super_R")]
    [InlineData("Cancel", "Break")]
    [InlineData("MediaPlayPause", "XF86AudioPlay")]
    [InlineData("VolumeMute", "XF86AudioMute")]
    [InlineData("BrowserBack", "XF86Back")]
    [InlineData("LaunchMail", "XF86Mail")]
    [InlineData("Escape", "Escape")]   // unchanged: names X already knows pass through
    public void PanicKeyNamesMapToRealXKeysymNames(string stored, string xName) =>
        Assert.Equal(xName, X11PanicKey.XKeyName(stored));

    /// <summary>WPF LeashServiceApp.AppDayInputs (:33-37): today's conditioning minutes come from the day log,
    /// so a Minutes leash assignment can be met. The head used to report 0 forever.</summary>
    [Fact]
    public void LeashDayInputsReadTodaysConditioningMinutesFromTheDayLog()
    {
        var prop = typeof(AvApp).GetProperty("FeatureDayLog", BindingFlags.Static | BindingFlags.NonPublic)!;
        var saved = prop.GetValue(null);
        var dir = Path.Combine(Path.GetTempPath(), "ccp-leash-minutes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        FeatureDayLogService? log = null;
        try
        {
            log = new FeatureDayLogService(() => new AchievementProgress(), Path.Combine(dir, "feature_day_log.json"));
            var now = DateTime.Now;   // today and tomorrow, so a run that crosses midnight still finds its 25
            foreach (var day in new[] { now, now.AddDays(1) })
                log.Log.GetOrAddDay(day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Cm = 25;
            prop.SetValue(null, log);
            Assert.Equal(25, LeashHead.DayInputs()!.Value.MinutesToday);

            prop.SetValue(null, null);   // no log (construction failed): 0, as WPF
            Assert.Equal(0, LeashHead.DayInputs()!.Value.MinutesToday);
        }
        finally
        {
            prop.SetValue(null, saved);
            log?.Dispose();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
