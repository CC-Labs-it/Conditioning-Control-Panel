// Arcademy heartbeat foundation. WPF ArcademyHostService.cs:575-585,619-634,5935-5980.
// Registration and page init are deliberately left for the host slice.
using System;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Fyp;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        internal static Func<DateTime> ArcademyUtcNow = () => DateTime.UtcNow;
        internal static bool ArcademyRelaunchedOnce;
        private DispatcherTimer? _arcademyWatch;
        private DateTime _arcademyLastBeat;
        private bool _arcademyClassActive, _arcademyRecovering;
        internal bool ArcademyWatchRunning => _arcademyWatch?.IsEnabled == true;

        private void StartArcademyWatch()
        {
            _arcademyLastBeat = ArcademyUtcNow();
            _arcademyWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _arcademyWatch.Tick += (_, _) => CheckArcademyHeartbeat(ArcademyUtcNow());
            _arcademyWatch.Start();
        }

        private void StopArcademyWatch()
        {
            _arcademyWatch?.Stop();
            _arcademyWatch = null;
        }

        partial void HandleArcademyMessage(JObject message, ref bool handled)
        {
            switch ((string?)message["type"])
            {
                case "ready":
                case "heartbeat":
                case "pong":
                    _arcademyLastBeat = ArcademyUtcNow();
                    break;
                case "library-remove":
                    FypRules.RemoveLibrarySub(CoreSettings.Current, (string?)message["name"] ?? (string?)message["sub"]);
                    Post(new { type = "library", subLibrary = FypRules.BuildLibraryPayload(CoreSettings.Current) });
                    handled = true;
                    break;
                case "class-started":
                    _arcademyClassActive = true;
                    handled = true;
                    break;
                case "class-left":
                case "class-ended":
                    _arcademyClassActive = false;
                    handled = true;
                    break;
            }
        }

        // WPF polls only after ready, strictly after 12 seconds in class or 20 on campus.
        internal string CheckArcademyHeartbeat(DateTime now)
        {
            if (_closing || _shellExiting || !IsReady) return "idle";
            if (_arcademyRecovering) return "recovering";
            if ((now - _arcademyLastBeat).TotalSeconds <= (_arcademyClassActive ? 12 : 20)) return "ok";
            _arcademyRecovering = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing || _shellExiting) return;
                bool retry = !ArcademyRelaunchedOnce;
                if (retry) ArcademyRelaunchedOnce = true;
                Close();
                if (retry)
                {
                    try { LaunchWindow(Spec); }
                    catch (Exception ex) { Log.Warning(ex, "Arcademy recovery launch failed"); }
                }
            });
            return "recovering";
        }
    }
}
