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
        _rampTimer?.Stop();
        _rampTimer = null;
        Ramp.Stop(CoreSettings.Current);
        SessionSettingsJournal.ClearRamp(CorePaths.UserData);
    }

    internal void TickEngineRamp()
    {
        if (Ramp.Tick(CoreSettings.Current, EngineNow(), App.Sessions?.IsRunning == true)) StopEngine();
    }
}
