// WPF MainWindow.ProfileBubble.cs:607-632, 662-692; finite shared-clock reactions.
using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private bool _bubbleReactionsClosed;
    private BeatLoop? _bubbleWobble, _bubbleShimmer;
    private DateTimeOffset? _lastBubbleWobble, _lastBubbleShimmer;

    internal void OnBubbleFlashDisplayed()
    {
        if (_bubbleReactionsClosed) return;
        var now = BeatLoop.Time.GetUtcNow();
        if (_lastBubbleWobble is { } last && (now - last).TotalMilliseconds < 2500) return;
        _lastBubbleWobble = now; // WPF consumes the throttle even when motion is off.
        if (!AmbientFxCanvas.Env.AllowTransitions || Named<Grid>("ProfileBubbleVisual") is not { } visual) return;
        if (visual.RenderTransform is not TransformGroup group || group.Children[1] is not RotateTransform tilt) return;
        _bubbleWobble ??= new BeatLoop(visual, seconds =>
        {
            tilt.Angle = BeatLoop.Keys(seconds, WobbleKeys);
            if (seconds >= 0.480) { tilt.Angle = 0; _bubbleWobble?.Stop(); }
        });
        _bubbleWobble.Stop();
        _bubbleWobble.Start();
    }

    private static readonly (double At, double V)[] WobbleKeys =
        { (0, 0), (0.090, -12), (0.220, 9), (0.340, -5), (0.480, 0) };

    internal void OnBubbleSubliminalDisplayed()
    {
        if (_bubbleReactionsClosed) return;
        var now = BeatLoop.Time.GetUtcNow();
        if (_lastBubbleShimmer is { } last && (now - last).TotalMilliseconds < 4000) return;
        _lastBubbleShimmer = now;
        if (!AmbientFxCanvas.Env.AllowTransitions || Named<Grid>("ProfileBubbleVisual") is not { } visual) return;
        _bubbleShimmer ??= new BeatLoop(visual, seconds =>
        {
            // WPF 300 ms SineEase InOut dip, auto-reversed, FillBehavior.Stop.
            double t = seconds <= 0.300 ? seconds / 0.300 : (0.600 - seconds) / 0.300;
            visual.Opacity = 1 - 0.45 * (1 - Math.Cos(Math.PI * Math.Clamp(t, 0, 1))) / 2;
            if (seconds >= 0.600) { visual.Opacity = 1; _bubbleShimmer?.Stop(); }
        });
        _bubbleShimmer.Stop();
        _bubbleShimmer.Start();
    }

    internal void StopProfileBubbleReactions()
    {
        _bubbleWobble?.Stop();
        _bubbleShimmer?.Stop();
        if (Named<Grid>("ProfileBubbleVisual") is not { } visual) return;
        visual.Opacity = 1;
        if (visual.RenderTransform is TransformGroup group && group.Children[1] is RotateTransform tilt) tilt.Angle = 0;
    }
}
