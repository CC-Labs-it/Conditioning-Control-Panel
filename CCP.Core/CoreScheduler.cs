using System;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The engine scheduler's rules: WPF <c>IsInScheduledTimeWindow</c>, <c>CheckSchedulerOnStartup</c>
    /// and <c>SchedulerTimer_Tick</c> (ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs:794-932)
    /// plus the BtnStart_Click manual-stop flag (:101-111). The head owns the 30 s timer, the 60 s startup
    /// grace, the tray and the engine; this only decides.
    /// </summary>
    public sealed class CoreScheduler
    {
        public enum Step { None, Start, Stop }

        /// <summary>WPF _schedulerAutoStarted: the running engine was started by the schedule.</summary>
        public bool AutoStarted { get; set; }

        /// <summary>WPF _manuallyStoppedDuringSchedule: the user stopped inside the window; no re-start until it ends.</summary>
        public bool ManuallyStopped { get; set; }

        /// <summary>WPF CheckSchedulerOnStartup: start when the app opens inside the window.</summary>
        public Step OnStartup(AppSettings s, DateTime now)
        {
            if (!s.SchedulerEnabled || !InWindow(s, now)) return Step.None;
            AutoStarted = true;
            return Step.Start;
        }

        /// <summary>WPF SchedulerTimer_Tick (every 30 s).</summary>
        public Step OnTick(AppSettings s, DateTime now, bool running)
        {
            if (!s.SchedulerEnabled) return Step.None;
            bool inWindow = InWindow(s, now);
            if (inWindow && !running && !AutoStarted && !ManuallyStopped) { AutoStarted = true; return Step.Start; }
            if (!inWindow && running && AutoStarted) { AutoStarted = false; return Step.Stop; }
            if (!inWindow) { AutoStarted = false; ManuallyStopped = false; }
            return Step.None;
        }

        /// <summary>WPF BtnStart_Click: a manual stop inside an enabled window holds the schedule off;
        /// a manual start clears that.</summary>
        public void OnManualToggle(AppSettings s, DateTime now, bool stopping) =>
            ManuallyStopped = stopping && s.SchedulerEnabled && InWindow(s, now);

        /// <summary>WPF IsInScheduledTimeWindow: an active weekday, start inclusive, end exclusive,
        /// overnight when end &lt; start. Unparseable times fall back to 16:00 / 22:00.</summary>
        public static bool InWindow(AppSettings s, DateTime now)
        {
            bool day = now.DayOfWeek switch
            {
                DayOfWeek.Monday => s.SchedulerMonday,
                DayOfWeek.Tuesday => s.SchedulerTuesday,
                DayOfWeek.Wednesday => s.SchedulerWednesday,
                DayOfWeek.Thursday => s.SchedulerThursday,
                DayOfWeek.Friday => s.SchedulerFriday,
                DayOfWeek.Saturday => s.SchedulerSaturday,
                DayOfWeek.Sunday => s.SchedulerSunday,
                _ => false,
            };
            if (!day) return false;
            if (!SchedulerTime.TryParse(s.SchedulerStartTime, out var start))
            {
                Log.Warning("Scheduler: Could not parse start time '{Time}', using default 16:00", s.SchedulerStartTime);
                start = new TimeSpan(16, 0, 0);
            }
            if (!SchedulerTime.TryParse(s.SchedulerEndTime, out var end))
            {
                Log.Warning("Scheduler: Could not parse end time '{Time}', using default 22:00", s.SchedulerEndTime);
                end = new TimeSpan(22, 0, 0);
            }
            var t = now.TimeOfDay;
            return end < start ? t >= start || t < end : t >= start && t < end;
        }
    }
}
