using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal sealed partial class MandatoryVideoOverlay
{
    private readonly List<Border> _entranceCurtains = new();
    private readonly List<Window> _exitCurtains = new();
    private IDisposable? _gentleBackstop;
    internal Func<Action, TimeSpan, IDisposable> ArmGentleBackstop { get; set; } = (fire, delay) => DispatcherTimer.RunOnce(fire, delay);

    // WPF GentleBeforePlay: black until Playing, with a three-second backstop.
    internal void CoverGentleEntrance(Panel panel)
    {
        if (!GentleVisuals.On) return;
        var cover = new Border { Background = Brushes.Black, IsHitTestVisible = false };
        // Build's first child is the picture (fill, scrim, video); keep grace, targets and controls above black.
        panel.Children.Insert(Math.Min(1, panel.Children.Count), cover);
        _entranceCurtains.Add(cover);
        _gentleBackstop ??= ArmGentleBackstop(RevealGentleEntrance, TimeSpan.FromSeconds(3));
    }

    internal bool OnPlayerPlaying(bool current)
    {
        if (!current) return false;
        RevealGentleEntrance();
        return true;
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

    internal IReadOnlyList<Window> ExitCurtains => _exitCurtains;
    internal Func<Window, bool> MakeGentleClickThrough { get; set; } = w => X11Overlay.SetClickThrough(w, true);

    // WPF GentleCurtain: a separate passive window has no playback close handlers.
    internal void CloseGentleSurface(Window window, bool naturalEnd)
    {
        if (!GentleVisuals.On || !naturalEnd) { window.Close(); return; }
        var curtain = new Window
        {
            WindowDecorations = WindowDecorations.None, CanResize = false,
            Topmost = true, ShowInTaskbar = false, ShowActivated = false,
            Focusable = false, IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = window.Position, Width = window.Bounds.Width, Height = window.Bounds.Height,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent, Content = new Border { Background = Brushes.Black },
        };
        curtain.Show();
        // An Avalonia hit-test flag does not pass native input to the desktop.
        // Never retain a fade window on a backend that cannot make it click-through.
        bool passive;
        try { passive = MakeGentleClickThrough(curtain); }
        catch { passive = false; }
        window.Close();
        if (!passive) { curtain.Close(); return; }
        _exitCurtains.Add(curtain);
        curtain.Transitions = FadeTransition(GentleVisuals.FadeSeconds);
        curtain.Opacity = 0;
        DispatcherTimer.RunOnce(() =>
        {
            curtain.Close();
            _exitCurtains.Remove(curtain);
        }, TimeSpan.FromSeconds(GentleVisuals.FadeSeconds));
    }

    internal void OnNaturalVideoEnd()
    {
        OpenQueuedCreditLink();   // WPF VideoService.Browser.cs:674-684: a full-screen press opens at the clip end
        Scheduler.Ended(renderedFrame: FirstFrameMs >= 0);
    }

    private void DropEntrance()
    {
        _gentleBackstop?.Dispose();
        _gentleBackstop = null;
        _entranceCurtains.Clear();
    }

    private void DropExitCurtains()
    {
        foreach (var window in _exitCurtains) window.Close();
        _exitCurtains.Clear();
    }
}
