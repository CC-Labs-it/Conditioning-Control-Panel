using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>A floating LibVLC window has no pixels, input region or sound until reveal,
/// permitted by a copied frame or the playing timeout.</summary>
internal sealed class FloatingVideoReveal
{
    private readonly Window _window;
    private readonly Action<bool>? _holdAudio;
    private bool _presented, _closed;
    private readonly bool _animate;
    private DispatcherTimer? _timer;
    private readonly Stopwatch _clock = new();
    internal bool IsAnimating => _timer != null;
    private double _width, _height;
    private Control? _frame;
    private Geometry? _previousClip;
    private RectangleGeometry? _clip;

    public FloatingVideoReveal(Window window, Action<bool>? holdAudio = null, bool animate = false)
    {
        _window = window;
        _animate = animate;
        _holdAudio = holdAudio;
        _holdAudio?.Invoke(true);
    }

    public void Present()
    {
        if (_closed || _presented) return;
        _presented = true;
        if (_animate && AmbientFxCanvas.Env.Level == MotionLevel.Full
            && _window.Content is Control frame
            && double.IsFinite(_window.Width) && _window.Width > 0
            && double.IsFinite(_window.Height) && _window.Height > 0)
        {
            _width = _window.Width;
            _height = _window.Height;
            _frame = frame;
            _previousClip = frame.Clip;
            _clip = new RectangleGeometry();
            frame.Clip = _clip;
            RevealAt(0);
            _clock.Restart();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => RevealAt(_clock.Elapsed.TotalMilliseconds);
            _timer.Start();
        }
        _window.Show();
        if (!_closed) _holdAudio?.Invoke(false);
    }

    // WPF FloatingSpawnLook: reveal the centre without resizing the window or its picture.
    internal void RevealAt(double elapsedMs)
    {
        if (_closed || _clip == null) return;
        var t = Math.Clamp(elapsedMs / 260, 0, 1);
        var scale = 0.7 + 0.3 * (1 - Math.Pow(1 - t, 3));
        var width = _width * scale;
        var height = _height * scale;
        _clip.Rect = new Rect((_width - width) / 2, (_height - height) / 2, width, height);
        if (t >= 1) StopAnimation();
    }

    private void StopAnimation()
    {
        _timer?.Stop();
        _timer = null;
        _clock.Stop();
        if (_frame != null) _frame.Clip = _previousClip;
        _frame = null;
        _clip = null;
        _previousClip = null;
    }

    /// <summary>The clip ended: never present, keep the audio held; the overlay closes the window its own way.</summary>
    public void Cancel()
    {
        _closed = true;
        StopAnimation();
        _holdAudio?.Invoke(true);
    }

    public void Close()
    {
        _closed = true;
        StopAnimation();
        _holdAudio?.Invoke(true);
        _window.Close();
    }
}
