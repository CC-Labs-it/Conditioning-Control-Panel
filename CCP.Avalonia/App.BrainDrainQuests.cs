using System;
using System.Diagnostics;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    private static readonly RunningTimeCredit BrainDrainCredit = new();
    private static DispatcherTimer? _brainDrainQuestTimer;
    private static bool _brainDrainQuestRunning;

    // WPF AchievementService.cs:277-284. The clock runs even without LibVLC.
    internal static void WireBrainDrainQuestMinutes()
    {
        _brainDrainQuestTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _brainDrainQuestTimer.Tick -= OnBrainDrainQuestTick;
        _brainDrainQuestTimer.Tick += OnBrainDrainQuestTick;
        CoreEngine.StartBrainDrain = () =>
        {
            _brainDrainQuestRunning = true;
            _brainDrainQuestTimer.Start();
            Platform.BrainDrainPlayer.Instance?.Start();
        };
        CoreEngine.StopBrainDrain = () =>
        {
            _brainDrainQuestRunning = false;
            _brainDrainQuestTimer.Stop();
            BrainDrainCredit.Sample(false, TimeSpan.Zero);
            Platform.BrainDrainPlayer.Instance?.Stop();
        };
    }

    private static void OnBrainDrainQuestTick(object? sender, EventArgs e) =>
        SampleBrainDrainQuestMinutes(Stopwatch.GetElapsedTime(0));

    internal static void SampleBrainDrainQuestMinutes(TimeSpan now)
    {
        var minutes = BrainDrainCredit.Sample(_brainDrainQuestRunning && CoreEngine.IsRunning
            && CoreSettings.Current.BrainDrainEnabled, now);
        if (minutes > 0) Quests?.TrackBrainDrainMinutes(minutes);
    }
}
