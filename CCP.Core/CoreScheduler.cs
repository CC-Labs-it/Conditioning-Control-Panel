using System;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using Serilog;
namespace ConditioningControlPanel
{
    public sealed class CoreScheduler
    {
        public enum Step { None, Start, Stop }
        public bool AutoStarted { get; set; }
        public bool ManuallyStopped { get; set; }
        public Step OnStartup(AppSettings s, DateTime now, bool restartHeld = false)
        {
            if (!s.SchedulerEnabled || restartHeld || !InWindow(s, now)) return Step.None;
            AutoStarted = true;
            return Step.Start;
        }
        public Step OnTick(AppSettings s, DateTime now, bool running, bool restartHeld = false)
        {
            if (!s.SchedulerEnabled) return Step.None;
            bool inWindow = InWindow(s, now);
            if (inWindow && !running && !AutoStarted && !ManuallyStopped && !restartHeld) { AutoStarted = true; return Step.Start; }
            if (!inWindow && running && AutoStarted) { AutoStarted = false; return Step.Stop; }
            if (!inWindow) { AutoStarted = false; ManuallyStopped = false; }
            return Step.None;
        }
        public void OnManualToggle(AppSettings s, DateTime now, bool stopping) =>
            ManuallyStopped = stopping && s.SchedulerEnabled && InWindow(s, now);
        public static bool InWindow(AppSettings s, DateTime now)
        {
            bool day = now.DayOfWeek switch
            {
                DayOfWeek.Monday => s.SchedulerMonday, DayOfWeek.Tuesday => s.SchedulerTuesday,
                DayOfWeek.Wednesday => s.SchedulerWednesday, DayOfWeek.Thursday => s.SchedulerThursday,
                DayOfWeek.Friday => s.SchedulerFriday, DayOfWeek.Saturday => s.SchedulerSaturday,
                DayOfWeek.Sunday => s.SchedulerSunday,
                _ => false,
            };
            if (!day) return false;
            if (!SchedulerTime.TryParse(s.SchedulerStartTime, out var start))
            {
                Log.Warning("Scheduler: Could not parse start time '{Time}', using default 16:00", s.SchedulerStartTime); start = new TimeSpan(16, 0, 0);
            }
            if (!SchedulerTime.TryParse(s.SchedulerEndTime, out var end))
            {
                Log.Warning("Scheduler: Could not parse end time '{Time}', using default 22:00", s.SchedulerEndTime); end = new TimeSpan(22, 0, 0);
            }
            var t = now.TimeOfDay;
            return end < start ? t >= start || t < end : t >= start && t < end;
        }
    }
}
