using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.Launcher.cs: the panel fades down for 180 ms, up for 260 ms.
    // Rides the window's shared 30 fps frame beat (FrameClock) only while the transition runs.
    private FrameClock _launcherFadeClock = null!;
    private readonly DispatcherTimer _launcherFadeGuard = new(DispatcherPriority.Normal);
    private Action? _launcherFadeThen;
    private double _launcherFadeElapsed, _launcherFadeSeconds, _launcherFadeFrom, _launcherFadeTo;
    private long _launcherFadeLast;
    private bool _launcherFading;

    private void HookLauncherPanelFade()
    {
        _launcherFadeClock = new FrameClock(this) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
        // WPF guard: complete even when render-priority work is starved.
        _launcherFadeGuard.Tick += (_, _) => StepLauncherFade(_launcherFadeSeconds);
        _launcherFadeClock.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            // Use elapsed time without a frame cap: a delayed pump still completes the hide.
            StepLauncherFade((now - _launcherFadeLast) / (double)Stopwatch.Frequency);
            _launcherFadeLast = now;
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty || IsVisible) return;
            // WPF MainWindow.Launcher.cs guard: a hide mid-fade still runs the pending step (opens the launcher).
            StepLauncherFade(_launcherFadeSeconds);
            RestoreLauncherPanelOpacity();
        };
        Closed += (_, _) => RestoreLauncherPanelOpacity();
    }

    internal void ShowFromLauncher()
    {
        bool animate = Env.AllowTransitions && !IsVisible;
        ShowFromTray();
        if (animate) BeginLauncherFade(0, 1, 0.26, null);
    }

    internal void FadeOutForLauncher(Action then)
    {
        if (_windowClosed || _launcherFadeThen != null) return;
        if (!Env.AllowTransitions || !IsVisible) { then(); return; }
        BeginLauncherFade(1, 0, 0.18, then);
    }

    private void BeginLauncherFade(double from, double to, double seconds, Action? then)
    {
        RestoreLauncherPanelOpacity();
        _launcherFadeThen = then;
        _launcherFadeFrom = from;
        _launcherFadeTo = to;
        _launcherFadeElapsed = 0;
        _launcherFadeSeconds = seconds;
        _launcherFading = true;
        if (Named<Grid>("RootGrid") is { } root) root.Opacity = from;
        _launcherFadeLast = Stopwatch.GetTimestamp();
        _launcherFadeGuard.Interval = TimeSpan.FromSeconds(seconds + 0.15);
        _launcherFadeGuard.Start();
        _launcherFadeClock.Start();
    }

    internal void StepLauncherFade(double seconds)
    {
        if (!_launcherFading) return;
        _launcherFadeElapsed += Math.Max(0, seconds);
        double u = Math.Clamp(_launcherFadeElapsed / _launcherFadeSeconds, 0, 1);
        double eased = _launcherFadeTo > _launcherFadeFrom ? 1 - (1 - u) * (1 - u) : u * u;
        if (Named<Grid>("RootGrid") is { } root)
            root.Opacity = Math.Clamp(_launcherFadeFrom + (_launcherFadeTo - _launcherFadeFrom) * eased, 0, 1);
        if (u < 1) return;
        var then = _launcherFadeThen;
        _launcherFadeThen = null;
        _launcherFading = false;
        _launcherFadeClock.Stop();
        _launcherFadeGuard.Stop();
        try { then?.Invoke(); }
        catch (Exception ex) { Log.Error(ex, "[Launcher] step after panel fade failed"); }
        finally { RestoreLauncherPanelOpacity(); }
    }

    private void RestoreLauncherPanelOpacity()
    {
        _launcherFadeClock.Stop();
        _launcherFadeGuard.Stop();
        _launcherFading = false;
        _launcherFadeThen = null;
        if (Named<Grid>("RootGrid") is { } root) root.Opacity = 1;
    }
}
