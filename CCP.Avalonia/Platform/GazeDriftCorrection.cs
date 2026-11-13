using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Owns the hook and short gaze history while the webcam correction gates hold.</summary>
internal sealed class GazeDriftCorrection : IDisposable
{
    private readonly WebcamTracker _tracker;
    private readonly AppSettings _settings;
    private readonly Func<Action<int, int>, IDisposable?> _install;
    private readonly Queue<(DateTime At, double X, double Y)> _gaze = new();
    private readonly global::ConditioningControlPanel.Services.GazeDriftCorrection _math = new();
    private IDisposable? _hook;
    private bool _disposed;
    private int _generation;

    internal GazeDriftCorrection(WebcamTracker tracker, AppSettings settings,
        Func<Action<int, int>, IDisposable?>? install = null)
    {
        _tracker = tracker;
        _settings = settings;
        _install = install ?? GazeDriftMouseHook.Start;
        _tracker.StateChanged += Refresh;
        _settings.PropertyChanged += SettingsChanged;
        Refresh();
    }
    private void SettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.WebcamAutoDriftCorrection) or nameof(AppSettings.WebcamConsentGiven)
            or nameof(AppSettings.WebcamConsentVersion))
        {
            if (Dispatcher.UIThread.CheckAccess()) Refresh();
            else Dispatcher.UIThread.Post(Refresh);
        }
    }
    private bool Allowed => !_disposed && _settings.WebcamAutoDriftCorrection && WebcamConsent.IsCurrent(_settings)
        && _tracker.IsRunning && _tracker.Calibration != null;
    internal void Refresh()
    {
        if (Allowed && _hook == null)
        {
            int generation = ++_generation;
            _hook = _install((x, y) => Dispatcher.UIThread.Post(() => Click(x, y, generation)));
            if (_hook != null) { _tracker.OnGazeMove += Gaze; _tracker.OnHeadPose += Pose; }
        }
        else if (!Allowed) Stop();
    }
    private void Gaze(Point p)
    {
        var now = DateTime.UtcNow;
        _gaze.Enqueue((now, p.X, p.Y));
        while (_gaze.Count > 0 && (now - _gaze.Peek().At).TotalMilliseconds > 700) _gaze.Dequeue();
    }
    private void Pose(double yaw, double pitch) => _math.Pose(yaw, pitch, DateTime.UtcNow);
    private void Click(int x, int y, int generation)
    {
        if (generation != _generation || _hook == null || !Allowed) return;
        var result = _math.TryCorrect(true, true, true,
            WebcamCalibrationWindow.IsShowing || WebcamQuickRecalWindow.IsShowing,
            _tracker.Calibration, _gaze.ToArray(), x, y, DateTime.UtcNow);
        if (result is { } r) _tracker.SetRuntimeOffset(r.Offset, r.Persist);
    }
    private void Stop()
    {
        ++_generation;
        _tracker.OnGazeMove -= Gaze;
        _tracker.OnHeadPose -= Pose;
        _hook?.Dispose();
        _hook = null;
        _gaze.Clear();
        _math.Reset();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.StateChanged -= Refresh;
        _settings.PropertyChanged -= SettingsChanged;
        Stop();
    }
}
