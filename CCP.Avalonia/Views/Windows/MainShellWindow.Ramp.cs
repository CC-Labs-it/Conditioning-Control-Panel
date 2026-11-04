using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private DispatcherTimer? _rampTimer;
    private DateTime _rampStartTime;
    private readonly Dictionary<string, double> _rampBaseValues = new();

    // WPF MainWindow.StartStop.cs:629: snapshot before the first tick and journal for crash recovery.
    internal void StartRampTimer()
    {
        var s = CoreSettings.Current;
        _rampBaseValues.Clear();
        _rampBaseValues[nameof(s.FlashOpacity)] = s.FlashOpacity;
        _rampBaseValues[nameof(s.SpiralOpacity)] = s.SpiralOpacity;
        _rampBaseValues[nameof(s.PinkFilterOpacity)] = s.PinkFilterOpacity;
        _rampBaseValues[nameof(s.MasterVolume)] = s.MasterVolume;
        _rampBaseValues[nameof(s.SubAudioVolume)] = s.SubAudioVolume;
        _rampBaseValues[nameof(s.BrainDrainBlurStrength)] = s.BrainDrainBlurStrength;
        SessionSettingsJournal.WriteRamp(CorePaths.UserData, SessionSettingsJournal.BuildRamp(_rampBaseValues));
        _rampStartTime = DateTime.Now;
        _rampTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _rampTimer.Tick += (_, _) => RampTick((DateTime.Now - _rampStartTime).TotalMinutes / s.RampDurationMinutes, App.Sessions?.IsRunning == true);
        _rampTimer.Start();
    }

    internal void StopRampTimer()
    {
        _rampTimer?.Stop();
        _rampTimer = null;
        if (_rampBaseValues.Count == 0) return;
        SessionSettingsJournal.ApplyRamp(SessionSettingsJournal.BuildRamp(_rampBaseValues), CoreSettings.Current);
        _rampBaseValues.Clear();
        SessionSettingsJournal.ClearRamp(CorePaths.UserData);
    }

    // WPF :698-797: preset sessions own visual ramping and completion; audio still follows this ramp.
    internal void RampTick(double progress, bool sessionActive)
    {
        if (_rampBaseValues.Count == 0) return;
        var s = CoreSettings.Current;
        progress = Math.Min(progress, 1.0);
        var factor = RampMath.ResolveFactor(s, progress);
        int Value(string key, int cap) => (int)Math.Clamp(_rampBaseValues[key] * factor, 0, cap);
        if (!sessionActive && s.RampLinkFlashOpacity) s.FlashOpacity = Value(nameof(s.FlashOpacity), 100);
        if (!sessionActive && s.RampLinkSpiralOpacity) s.SpiralOpacity = Value(nameof(s.SpiralOpacity), 100);
        if (!sessionActive && s.RampLinkPinkFilterOpacity) s.PinkFilterOpacity = Value(nameof(s.PinkFilterOpacity), 50);
        if (s.RampLinkMasterAudio) s.MasterVolume = Value(nameof(s.MasterVolume), 100);
        if (s.RampLinkSubliminalAudio) s.SubAudioVolume = Value(nameof(s.SubAudioVolume), 100);
        if (!sessionActive && s.RampLinkBrainDrain) s.BrainDrainBlurStrength = Value(nameof(s.BrainDrainBlurStrength), 100);
        if (progress >= 1.0 && s.EndSessionOnRampComplete && !sessionActive)
        {
            Platform.OsNotifications.Show(Loc.Get("dialog_session_complete"), Loc.Get("ramp_completion_stopping"));
            StopEngine();
        }
    }
}
