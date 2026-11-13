using System;
using System.Diagnostics;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Overlays;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    private static readonly RunningTimeCredit BrainDrainCredit = new();
    private static DispatcherTimer? _brainDrainQuestTimer;
    private static int _brainDrainOverlayStopGeneration;

    // WPF AchievementService.cs:277-284. The clock runs even without LibVLC.
    internal static void WireBrainDrainQuestMinutes()
    {
        _brainDrainQuestTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _brainDrainQuestTimer.Tick -= OnBrainDrainQuestTick;
        _brainDrainQuestTimer.Tick += OnBrainDrainQuestTick;
        BrainDrainCredit.Sample(false, TimeSpan.Zero);
        _brainDrainOverlayStopGeneration = RemoteCommands.OverlayStopGeneration;
        _brainDrainQuestTimer.Start();
        CoreEngine.StartBrainDrain = () =>
        {
            Platform.BrainDrainPlayer.Instance?.Start();
        };
        CoreEngine.StopBrainDrain = () =>
        {
            BrainDrainCredit.Sample(false, TimeSpan.Zero);
            Platform.BrainDrainPlayer.Instance?.Stop();
        };
    }

    internal static void StopBrainDrainQuestMinutes()
    {
        _brainDrainQuestTimer?.Stop();
        BrainDrainCredit.Sample(false, TimeSpan.Zero);
    }

    private static bool OverlayRunningWithoutPulse => !RemoteCommands.OverlaysStopped &&
        (RemoteCommands.OverlayRunning || (Sessions?.IsPaused != true &&
         (CoreEngine.IsVisualRun || Sessions?.IsRunning == true)));

    private static bool _pulseStartedOverlay;
    internal static void NoteBrainDrainPulseStart() => _pulseStartedOverlay = !OverlayRunningWithoutPulse;

    internal static void ResetBrainDrainQuestSampleAfterPulse()
    {
        // WPF AutonomyService.cs:572-575,1602-1604: a pulse that started the overlay service stops it
        // again, even after a controller joined mid-pulse (RemoteControlService.cs:1207 found it running).
        if (_pulseStartedOverlay && RemoteCommands.OverlayRunning && !CoreEngine.IsRunning) RemoteCommands.StopOverlays();
        _pulseStartedOverlay = false;
        if (!OverlayRunningWithoutPulse) BrainDrainCredit.Sample(false, TimeSpan.Zero);
    }

    private static void OnBrainDrainQuestTick(object? sender, EventArgs e) =>
        SampleBrainDrainQuestMinutes(Stopwatch.GetElapsedTime(0));

    internal static void SampleBrainDrainQuestMinutes(TimeSpan now)
    {
        var stopGeneration = RemoteCommands.OverlayStopGeneration;
        if (stopGeneration != _brainDrainOverlayStopGeneration)
        {
            BrainDrainCredit.Sample(false, now);
            _brainDrainOverlayStopGeneration = stopGeneration;
        }
        // WPF SessionEngine.cs:530,582,1628; RemoteControlService.cs:587,1204.
        var overlayRunning = PinkFilterOverlay.PulseHold || OverlayRunningWithoutPulse;
        var minutes = BrainDrainCredit.Sample(overlayRunning && CoreSettings.Current.BrainDrainEnabled, now);
        if (minutes > 0) Quests?.TrackBrainDrainMinutes(minutes);
    }
}
