using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private CancellationTokenSource? _deeperPulse;
    private IDisposable? _deeperPulseDelay;

    private void InitializeDeeperPulse()
    {
        Opened += (_, _) =>
        {
            // Opened fires on every Show (tray round trip): never orphan an armed delay, it roots the window.
            _deeperPulseDelay?.Dispose();
            _deeperPulseDelay = null;
            if (CoreSettings.Current.EnableDeeper && !CoreSettings.Current.HasSeenDeeperTab)
                _deeperPulseDelay = DispatcherTimer.RunOnce(StartDeeperTabPulse, TimeSpan.FromMilliseconds(1200));
        };
        Closed += (_, _) => StopDeeperTabPulse();
    }

    internal async void StartDeeperTabPulse()
    {
        if (_deeperPulse != null || !IsVisible || !CoreSettings.Current.EnableDeeper
            || CoreSettings.Current.HasSeenDeeperTab || Platform.StartupLadder.IsQuiet
            || NavDoorForTab(CurrentTab) == NavDoorForTab("deeper")
            || CoreSettings.Current.MotionLevel == MotionLevel.Off || Named<Button>("DoorPlay") is not { } door) return;
        using var cancellation = new CancellationTokenSource();
        _deeperPulse = cancellation;
        try
        {
            var duration = CoreSettings.Current.MotionLevel == MotionLevel.Reduced ? 350 : 700;
            // Four opacity dips, each out and back; no permanent composition loop.
            for (var beat = 0; beat < 4; beat++)
                await new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(duration * 2), Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1d) } },
                        new KeyFrame { Cue = new Cue(.5), Setters = { new Setter(OpacityProperty, .35d) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } }
                    }
                }.RunAsync(door, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_deeperPulse, cancellation)) _deeperPulse = null;
            door.Opacity = 1;
        }
    }

    internal void StopDeeperTabPulse()
    {
        _deeperPulseDelay?.Dispose();
        _deeperPulseDelay = null;
        _deeperPulse?.Cancel();
        if (Named<Button>("DoorPlay") is { } door) door.Opacity = 1;
    }
}
