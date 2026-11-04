// WPF MainWindow.StartStop.cs:805-883 and MainWindow.xaml.cs:608-628.
using System;
using System.ComponentModel;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private DispatcherTimer? _schedulerTimer;
        private DispatcherTimer? _schedulerGrace;
        private SettingsService? _schedulerService;
        private AppSettings? _schedulerSettings;
        private bool _schedulerActive, _schedulerAutoStarted, _schedulerPending, _schedulerManualStop, _schedulerClosed;
        private int _schedulerGeneration;

        // Instance seams let headless tests drive the actual shell lifecycle without starting media.
        internal Func<DateTime> SchedulerNow = () => DateTime.Now;
        internal Func<bool> SchedulerRunning = () => CoreEngine.IsRunning;
        internal Action<Action>? SchedulerStartOverride;
        internal Action? SchedulerStopOverride;
        internal Action? SchedulerMinimizeOverride;
        internal Action<string, string>? SchedulerNoticeOverride;

        private void InitializeScheduler()
        {
            _schedulerService = CoreSettings.Service;
            if (_schedulerService != null) _schedulerService.CurrentReplaced += RebindScheduler;
            RebindScheduler();
            _schedulerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _schedulerTimer.Tick += (_, _) => SchedulerTick();
            _schedulerGrace = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _schedulerGrace.Tick += (_, _) => SchedulerStartup();
            _schedulerGrace.Start();
            Closed += (_, _) => ShutdownScheduler();
        }

        private void RebindScheduler()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RebindScheduler); return; }
            if (_schedulerClosed) return;
            if (_schedulerSettings != null) _schedulerSettings.PropertyChanged -= SchedulerSettingChanged;
            _schedulerSettings = CoreSettings.Current;
            _schedulerSettings.PropertyChanged += SchedulerSettingChanged;
            if (_schedulerActive) SchedulerTick(settingsChange: true);
        }

        private void SchedulerSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(AppSettings.SchedulerEnabled) or nameof(AppSettings.SchedulerStartTime)
                or nameof(AppSettings.SchedulerEndTime) or nameof(AppSettings.SchedulerMonday) or nameof(AppSettings.SchedulerTuesday)
                or nameof(AppSettings.SchedulerWednesday) or nameof(AppSettings.SchedulerThursday) or nameof(AppSettings.SchedulerFriday)
                or nameof(AppSettings.SchedulerSaturday) or nameof(AppSettings.SchedulerSunday)) && !string.IsNullOrEmpty(e.PropertyName)) return;
            void Check() { if (_schedulerActive) SchedulerTick(settingsChange: true); }
            // Disabling cancels a queued start before the portal callback can run. Other edits
            // are posted: the settings panel writes both times and all seven days as a batch.
            if (e.PropertyName == nameof(AppSettings.SchedulerEnabled) && !CoreSettings.Current.SchedulerEnabled
                && Dispatcher.UIThread.CheckAccess()) Check();
            else Dispatcher.UIThread.Post(Check);
        }

        internal void SchedulerStartup()
        {
            if (_schedulerActive || _schedulerGrace == null) return;
            _schedulerGrace.Stop();
            _schedulerActive = true;
            _schedulerTimer?.Start();
            SchedulerTick();
        }

        internal void SchedulerTick(bool settingsChange = false)
        {
            var s = CoreSettings.Current;
            if (!_schedulerActive) return;
            // A newer request or a Core session owns the engine now. Retire only our queued start.
            if (_schedulerPending && (_schedulerGeneration != _engineGen || SchedulerRunning()))
            {
                if (_schedulerGeneration == _engineGen) _engineGen++;
                _schedulerPending = _schedulerAutoStarted = false;
                _schedulerManualStop = true;
            }
            if (!s.SchedulerEnabled)
            {
                // A start can still be waiting on the portal when the schedule is disabled.
                if (_schedulerPending) { _engineGen++; _schedulerPending = _schedulerAutoStarted = false; }
                return;
            }
            bool inWindow = SchedulerTime.InWindow(s, SchedulerNow());
            // WPF edits wait for the timer; retire a queued start if its window ended.
            if (settingsChange) { if (!inWindow && _schedulerPending) { _engineGen++; _schedulerPending = _schedulerAutoStarted = false; } return; }
            if (inWindow && !SchedulerRunning() && !_schedulerAutoStarted && !_schedulerManualStop)
            {
                // WPF's settings-change check does not minimize; startup and timer do.
                if (!settingsChange) SchedulerMinimize();
                SchedulerNotice(Loc.Get("scheduler_notice_active"), Loc.Get("scheduler_notice_started"));
                _schedulerAutoStarted = _schedulerPending = true;
                void Started() => _schedulerPending = false;
                if (SchedulerStartOverride is { } start) start(Started); else StartEngine(Started);
                _schedulerGeneration = _engineGen;
            }
            else if (!inWindow && _schedulerAutoStarted && (SchedulerRunning() || _schedulerPending))
            {
                // Stop also invalidates a start waiting for the Wayland portal.
                SchedulerStop();
                _schedulerAutoStarted = _schedulerPending = _schedulerManualStop = false;
                SchedulerNotice(Loc.Get("scheduler_notice_title"), Loc.Get("scheduler_notice_ended"));
            }
            else if (!inWindow)
                _schedulerAutoStarted = _schedulerPending = _schedulerManualStop = false;
        }

        private void SchedulerMinimize()
        {
            if (SchedulerMinimizeOverride is { } minimize) { minimize(); return; }
            if (Tray == null || !TrayHostPresent()) return;
            Hide();
            HideAvatarTube();
        }

        private void SchedulerNotice(string title, string body)
        {
            if (SchedulerNoticeOverride is { } notice) notice(title, body);
            else Platform.OsNotifications.Show(title, body);
        }

        private void SchedulerStop()
        {
            if (SchedulerStopOverride is { } stop) stop(); else StopEngine();
        }

        internal void NoteSchedulerManualStop()
        {
            if (CoreSettings.Current.SchedulerEnabled && SchedulerTime.InWindow(CoreSettings.Current, SchedulerNow()))
                _schedulerManualStop = true;
            _schedulerPending = false;
        }

        private void ShutdownScheduler()
        {
            _schedulerActive = false;
            _schedulerClosed = true;
            if (_schedulerPending) { if (_schedulerGeneration == _engineGen) _engineGen++; _schedulerPending = false; }
            _schedulerTimer?.Stop();
            _schedulerGrace?.Stop();
            _schedulerGrace = null;
            if (_schedulerService != null) _schedulerService.CurrentReplaced -= RebindScheduler;
            if (_schedulerSettings != null) _schedulerSettings.PropertyChanged -= SchedulerSettingChanged;
            _schedulerSettings = null;
        }
    }
}
