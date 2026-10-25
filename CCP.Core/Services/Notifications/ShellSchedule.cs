using System;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF MainWindow.StartStop scheduler window and manual-stop latches.</summary>
public sealed class ShellSchedule
{
    public bool AutoStarted { get; private set; }
    private bool _manualStop;
    public static bool InWindow(AppSettings s, DateTime now)
    {
        bool day = now.DayOfWeek switch
        {
            DayOfWeek.Monday => s.SchedulerMonday, DayOfWeek.Tuesday => s.SchedulerTuesday,
            DayOfWeek.Wednesday => s.SchedulerWednesday, DayOfWeek.Thursday => s.SchedulerThursday,
            DayOfWeek.Friday => s.SchedulerFriday, DayOfWeek.Saturday => s.SchedulerSaturday,
            _ => s.SchedulerSunday
        };
        if (!day) return false;
        if (!SchedulerTime.TryParse(s.SchedulerStartTime, out var start)) start = TimeSpan.FromHours(16);
        if (!SchedulerTime.TryParse(s.SchedulerEndTime, out var end)) end = TimeSpan.FromHours(22);
        return end < start ? now.TimeOfDay >= start || now.TimeOfDay < end : now.TimeOfDay >= start && now.TimeOfDay < end;
    }
    /// <returns>1 starts, -1 stops, 0 leaves the engine alone.</returns>
    public int Tick(AppSettings s, DateTime now, bool running)
    {
        if (!s.SchedulerEnabled) return 0;
        if (InWindow(s, now))
        {
            if (!running && !AutoStarted && !_manualStop) { AutoStarted = true; return 1; }
        }
        else
        {
            var stop = running && AutoStarted;
            AutoStarted = _manualStop = false;
            if (stop) return -1;
        }
        return 0;
    }
    public void ManualStop(AppSettings s, DateTime now)
    {
        if (s.SchedulerEnabled && InWindow(s, now)) _manualStop = true;
    }
}
