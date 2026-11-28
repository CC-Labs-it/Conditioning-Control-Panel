using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
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
    internal PixelPoint TargetPosition => IsAnimating ? _origin : _window.Position;
    private double _width, _height, _scaling;
    private PixelPoint _origin;

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
            && double.IsFinite(_window.Width) && _window.Width > 0
            && double.IsFinite(_window.Height) && _window.Height > 0)
        {
            _width = _window.Width;
            _height = _window.Height;
            _origin = _window.Position;
            _scaling = _window.RenderScaling;
            RevealAt(0);
            _clock.Restart();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => RevealAt(_clock.Elapsed.TotalMilliseconds);
            _timer.Start();
        }
        _window.Show();
        if (!_closed) _holdAudio?.Invoke(false);
    }

    // WPF FloatingSpawnLook: the window grows from its centre using cubic ease-out.
    internal void RevealAt(double elapsedMs)
    {
        if (_closed || _width <= 0) return;
        var t = Math.Clamp(elapsedMs / 260, 0, 1);
        var scale = 0.7 + 0.3 * (1 - Math.Pow(1 - t, 3));
        _window.Width = _width * scale;
        _window.Height = _height * scale;
        _window.Position = new PixelPoint(_origin.X + (int)Math.Round((_width - _window.Width) * _scaling / 2),
            _origin.Y + (int)Math.Round((_height - _window.Height) * _scaling / 2));
        if (t >= 1) StopAnimation();
    }

    private void StopAnimation()
    {
        _timer?.Stop();
        _timer = null;
        _clock.Stop();
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
