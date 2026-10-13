using System.IO;
using System.Reflection;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The idle-day row stays blind on this head until the conditioning-time tracker is ported:
/// nothing here adds to TotalConditioningMinutes, so the day log reads 0 for every day and WPF's
/// MinutesFromDayLog would charge every day CCP was open as idle (docs/avalonia-decisions.md).</summary>
public sealed class ChasterMinutesTests
{
    private static readonly PropertyInfo DayLog = typeof(AvApp).GetProperty("FeatureDayLog", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public void IdleDayIsNeverCharged_WhileTheDayLogHasNoMinutesSource()
    {
        var saved = DayLog.GetValue(null);
        var dir = Path.Combine(Path.GetTempPath(), "ccp-chaster-minutes-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        // A day log that is up and has seen the day, with the 0 minutes this head can only ever book.
        var log = new FeatureDayLogService(() => null, Path.Combine(dir, "feature_day_log.json"));
        var chaster = ChasterHead.Create("sandbox", null);
        try
        {
            log.Log.GetOrAddDay("2026-10-01").Cm = 0;
            DayLog.SetValue(null, log);
            Assert.Null(chaster.MinutesOn);
            // The day-end judge (ChasterService.JudgeEndedDay) charges exactly this.
            Assert.Equal(0, TabDayEnd.IdleCharge(chaster.MinutesOn?.Invoke("2026-10-01")));
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
