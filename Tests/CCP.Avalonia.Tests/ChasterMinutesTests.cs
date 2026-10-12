using System.IO;
using System.Reflection;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The idle-day row's eyes (WPF ChasterServiceApp.MinutesFromDayLog): the head's Chaster
/// service reads a day's conditioning minutes from App.FeatureDayLog, and null when there is no log.</summary>
public sealed class ChasterMinutesTests
{
    private static readonly PropertyInfo DayLog = typeof(AvApp).GetProperty("FeatureDayLog", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public void MinutesOn_ReadsTheDayLogsConditioningMinutes_NullWithoutALog()
    {
        var saved = DayLog.GetValue(null);
        var dir = Path.Combine(Path.GetTempPath(), "ccp-chaster-minutes-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var log = new FeatureDayLogService(() => null, Path.Combine(dir, "feature_day_log.json"));
        var chaster = ChasterHead.Create("sandbox", null);
        try
        {
            log.Log.GetOrAddDay("2026-10-01").Cm = 47;
            DayLog.SetValue(null, log);
            Assert.NotNull(chaster.MinutesOn);
            Assert.Equal(47, chaster.MinutesOn!("2026-10-01"));
            Assert.Equal(0, chaster.MinutesOn("2026-10-02")); // a day with no entry had none

            DayLog.SetValue(null, null);
            Assert.Null(chaster.MinutesOn("2026-10-01")); // no log: nobody can tell
        }
        finally
        {
            DayLog.SetValue(null, saved);
            chaster.Dispose();
            log.Dispose();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
