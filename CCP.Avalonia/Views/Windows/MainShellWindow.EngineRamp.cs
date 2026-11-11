// WPF MainWindow.StartStop.cs:619-815. Head timer, shared arithmetic and restore journal.
using System;
using Avalonia.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal readonly IntensityRampRun Ramp = new();
    internal Func<DateTime> EngineNow = () => DateTime.Now;
    private DispatcherTimer? _rampTimer;

    internal void StartEngineRamp()
    {
        if (!CoreSettings.Current.IntensityRampEnabled || Ramp.IsActive) return;
        Ramp.Start(CoreSettings.Current, EngineNow());
        SessionSettingsJournal.WriteRamp(CorePaths.UserData, SessionSettingsJournal.BuildRamp(Ramp.Bases));
        _rampTimer = new DispatcherTimer { Interval = IntensityRampRun.TickInterval };
        _rampTimer.Tick += (_, _) => TickEngineRamp();
        _rampTimer.Start();
    }

    internal void StopEngineRamp()
    {
        var was = Ramp.IsActive;
        _rampTimer?.Stop();
        _rampTimer = null;
        Ramp.Stop(CoreSettings.Current);
        if (was) RefreshRampAudio(); // WPF StartStop.cs:654 only refreshes captured values
        SessionSettingsJournal.ClearRamp(CorePaths.UserData);
    }

    private static void RefreshRampAudio()
    {
        Views.Controls.AppSettings.AudioSettingsBinder.RaiseChanged();
        Platform.LayeredAudio.Instance?.SetMasterVolumeLive();
        Views.Overlays.MandatoryVideoOverlay.Instance.UpdateVolume();
    }

    internal void TickEngineRamp()
    {
        var s = CoreSettings.Current;
        bool sessionActive = App.Sessions?.IsRunning == true;
        bool complete = Ramp.Tick(s, EngineNow(), sessionActive);
        RefreshRampAudio(); // WPF StartStop.cs:749-754 and :676-680 drive SliderMaster_Changed.
        // WPF OverlayService.cs:780-809 repaints pink and spiral every 500 ms; OnSessionTick covers sessions.
        if (!sessionActive && (s.RampLinkPinkFilterOpacity || s.RampLinkSpiralOpacity))
        {
            Views.Overlays.PinkFilterOverlay.Refresh(this);
            Views.Overlays.SpiralOverlay.Refresh(this);
        }
        if (!complete) return;
        Platform.OsNotifications.Show(ConditioningControlPanel.Localization.Loc.Get("dialog_session_complete"),
            ConditioningControlPanel.Localization.Loc.Get("ramp_complete_stopping")); // WPF :785, before stop
        StopEngine();
    }
}
