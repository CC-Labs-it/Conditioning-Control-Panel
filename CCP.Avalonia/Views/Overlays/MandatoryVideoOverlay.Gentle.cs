using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal sealed partial class MandatoryVideoOverlay
{
    private readonly List<Border> _entranceCurtains = new();
    private readonly List<Window> _exitCurtains = new();
    private IDisposable? _gentleBackstop;
    private bool _naturalEnd;

    // WPF GentleBeforePlay: black until the first picture, with a three-second backstop.
    internal void CoverGentleEntrance(Panel panel)
    {
        if (!GentleVisuals.On) return;
        var cover = new Border { Background = Brushes.Black, IsHitTestVisible = false };
        panel.Children.Add(cover);
        _entranceCurtains.Add(cover);
        _gentleBackstop ??= DispatcherTimer.RunOnce(RevealGentleEntrance, TimeSpan.FromSeconds(3));
    }

    internal void RevealGentleEntrance()
    {
        _gentleBackstop?.Dispose();
        _gentleBackstop = null;
        foreach (var cover in _entranceCurtains)
        {
            cover.Transitions = FadeTransition(GentleVisuals.FadeSeconds);
            cover.Opacity = 0;
        }
        _entranceCurtains.Clear();
    }

    private static Transitions FadeTransition(double seconds) => new()
    {
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromSeconds(seconds) },
    };

    // Reuse the window as a black curtain after releasing the video. Forced cleanup always drops it.
    internal void CloseGentleSurface(Window window, bool naturalEnd)
    {
        if (!GentleVisuals.On || !naturalEnd) { window.Close(); return; }
        window.Content = new Border { Background = Brushes.Black };
        window.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        window.Background = Brushes.Transparent;
        window.IsHitTestVisible = false;
        window.Transitions = FadeTransition(GentleVisuals.FadeSeconds);
        _exitCurtains.Add(window);
        window.Opacity = 0;
        DispatcherTimer.RunOnce(() =>
        {
            _exitCurtains.Remove(window);
            window.Close();
        }, TimeSpan.FromSeconds(GentleVisuals.FadeSeconds));
    }

    internal void DropGentleCurtains()
    {
        _gentleBackstop?.Dispose();
        _gentleBackstop = null;
        _entranceCurtains.Clear();
        foreach (var window in _exitCurtains) window.Close();
        _exitCurtains.Clear();
    }
}
