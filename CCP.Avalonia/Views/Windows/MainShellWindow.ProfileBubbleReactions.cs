// WPF MainWindow.ProfileBubble.cs: flash wobble and subliminal shimmer.
using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private BeatLoop? _bubbleWobble, _bubbleShimmer;
    private long? _bubbleLastWobble, _bubbleLastShimmer;
    private bool _bubbleReactionsClosed;
    private static readonly (double At, double V)[] BubbleWobbleKeys =
    {
        (0, 0), (0.09, -12), (0.22, 9), (0.34, -5), (0.48, 0),
    };

    private void OnBubbleFeatureUsed(string feature)
    {
        // The display sources can run on a worker. Every throttle and visual write is UI-owned.
        Dispatcher.UIThread.Post(() =>
        {
            if (_bubbleReactionsClosed) return;
            bool flash = feature == MemorySignalWriter.FeatureFlash;
            if (!flash && feature != MemorySignalWriter.FeatureSubliminal) return;
            ref long? last = ref (flash ? ref _bubbleLastWobble : ref _bubbleLastShimmer);
            if (last is { } stamp && BeatLoop.Time.GetElapsedTime(stamp).TotalMilliseconds < (flash ? 2500 : 4000)) return;
            last = BeatLoop.Time.GetTimestamp(); // WPF consumes the throttle even with motion off.
            if (!AmbientFxCanvas.Env.AllowTransitions || Named<Grid>("ProfileBubbleVisual") is not { } visual) return;
            if (flash && visual.RenderTransform is TransformGroup group && group.Children[1] is RotateTransform tilt)
            {
                _bubbleWobble ??= new BeatLoop(visual, seconds =>
                {
                    tilt.Angle = BeatLoop.Keys(seconds, BubbleWobbleKeys);
                    if (seconds >= 0.48) _bubbleWobble?.Stop();
                });
                _bubbleWobble.Stop();
                _bubbleWobble.Start();
            }
            else if (!flash)
            {
                _bubbleShimmer ??= new BeatLoop(visual, seconds =>
                {
                    // WPF: SineEaseInOut 1 -> .55 in 300ms, auto-reversed.
                    visual.Opacity = seconds >= 0.6 ? 1 : 1 - 0.45 * BeatLoop.Breath(seconds, 0.3);
                    if (seconds >= 0.6) _bubbleShimmer?.Stop();
                });
                _bubbleShimmer.Stop();
                _bubbleShimmer.Start();
            }
        });
    }

    private void CleanupBubbleReactions()
    {
        _bubbleReactionsClosed = true;
        _bubbleWobble?.Stop();
        _bubbleShimmer?.Stop();
        if (Named<Grid>("ProfileBubbleVisual") is { } visual)
        {
            visual.Opacity = 1;
            if (visual.RenderTransform is TransformGroup group && group.Children[1] is RotateTransform tilt) tilt.Angle = 0;
        }
    }
}
