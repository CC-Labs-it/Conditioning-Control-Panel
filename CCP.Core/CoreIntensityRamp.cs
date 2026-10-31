using System;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Helpers;
using Serilog;
using ConditioningControlPanel.Services;
namespace ConditioningControlPanel
{
    /// <summary>
    /// The plain Intensity Ramp: WPF <c>StartRampTimer</c> / <c>StopRampTimer</c> / <c>RampTimer_Tick</c>
    /// (ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs:619-790). Started and stopped by
    /// <see cref="CoreEngine"/>, so every engine start (button, session, remote) ramps as WPF's
    /// StartEngine does. Linked values are scaled from the snapshot taken at start and restored on stop.
    /// </summary>
    public static class CoreIntensityRamp
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, double> Base = new();
        private static DateTime _start;
        private static Timer? _timer;
        /// <summary>Wall clock; a test steps it.</summary>
        internal static Func<DateTime> Now = () => DateTime.Now;
        /// <summary>WPF's 2 s tick; a test turns it off (Infinite) and calls <see cref="Tick"/> itself.</summary>
        internal static TimeSpan Interval = TimeSpan.FromSeconds(2);
        /// <summary>The head's "ramp finished" (WPF: tray notice + StopEngine). Null: stop the Core engine.</summary>
        public static volatile Action? CompletedProvider;
        public static bool IsRunning { get { lock (Gate) return Base.Count > 0; } }
        /// <summary>WPF StartRampTimer: snapshot the base values, tick every 2 s.</summary>
        public static void Start()
        {
            var s = CoreSettings.Current;
            lock (Gate)
            {
                if (Base.Count > 0) return;
                Base["FlashOpacity"] = s.FlashOpacity;
                Base["SpiralOpacity"] = s.SpiralOpacity;
                Base["PinkFilterOpacity"] = s.PinkFilterOpacity;
                Base["MasterVolume"] = s.MasterVolume;
                Base["SubAudioVolume"] = s.SubAudioVolume;
                Base["BrainDrainBlurStrength"] = s.BrainDrainBlurStrength;
                SessionSettingsJournal.WriteRamp(CorePaths.UserData, SessionSettingsJournal.BuildRamp(Base));
                _start = Now();
                _timer ??= new Timer(_ => CoreDispatch.Post(() => Tick()), null, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(Interval, Interval);
            }
            Log.Information("Ramp timer started - Duration: {Duration}min, Mode: {Mode}, Multiplier: {Mult}x, Range: {Start}%->{End}%",
                s.RampDurationMinutes, s.RampMode, s.SchedulerMultiplier, s.RampStartPercent, s.RampEndPercent);
        }
        /// <summary>WPF StopRampTimer: stop ticking and put every snapshotted value back.</summary>
        public static void Stop()
        {
            Dictionary<string, double> snap;
            lock (Gate)
            {
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                if (Base.Count == 0) return;
                snap = new Dictionary<string, double>(Base);
                Base.Clear();
            }
            var s = CoreSettings.Current;
            s.FlashOpacity = (int)snap["FlashOpacity"];
            s.SpiralOpacity = (int)snap["SpiralOpacity"];
            s.PinkFilterOpacity = (int)snap["PinkFilterOpacity"];
            s.MasterVolume = (int)snap["MasterVolume"];
            s.SubAudioVolume = (int)snap["SubAudioVolume"];
            s.BrainDrainBlurStrength = (int)snap["BrainDrainBlurStrength"];
            SessionSettingsJournal.ClearRamp(CorePaths.UserData);
            Log.Information("Ramp timer stopped - values reset to base");
        }
        /// <summary>WPF RampTimer_Tick. Visual links sit out a running session (it ramps itself, #444);
        /// audio links do not. Ends the run at 100 % when EndSessionOnRampComplete and no session.</summary>
        internal static void Tick()
        {
            var s = CoreSettings.Current;
            double progress;
            Dictionary<string, double> b;
            lock (Gate)
            {
                if (Base.Count == 0) return;
                b = new Dictionary<string, double>(Base);
                progress = Math.Min((Now() - _start).TotalMinutes / s.RampDurationMinutes, 1.0);
            }
            bool sessionActive = CoreSession.IsSessionRunning;
            var mult = RampMath.ResolveFactor(s, progress);
            int Scale(string key, int cap) => (int)Math.Clamp(b[key] * mult, 0, cap);
            if (!sessionActive && s.RampLinkFlashOpacity) s.FlashOpacity = Scale("FlashOpacity", 100);
            if (!sessionActive && s.RampLinkSpiralOpacity) s.SpiralOpacity = Scale("SpiralOpacity", 100);
            if (!sessionActive && s.RampLinkPinkFilterOpacity) s.PinkFilterOpacity = Scale("PinkFilterOpacity", 50);
            if (s.RampLinkMasterAudio) s.MasterVolume = Scale("MasterVolume", 100);
            if (s.RampLinkSubliminalAudio) s.SubAudioVolume = Scale("SubAudioVolume", 100);
            if (!sessionActive && s.RampLinkBrainDrain) s.BrainDrainBlurStrength = Scale("BrainDrainBlurStrength", 100);
            if (progress >= 1.0 && s.EndSessionOnRampComplete && !sessionActive)
            {
                Log.Information("Ramp complete - ending session");
                var done = CompletedProvider;
                if (done != null) done(); else CoreEngine.Stop();
            }
        }
    }
}
