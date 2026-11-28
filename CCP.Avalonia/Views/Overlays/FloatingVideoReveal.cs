using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
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
    private BeatLoop? _loop;
    internal bool IsAnimating => _loop != null;
    internal Func<Window, PixelRect[]?, bool, bool> InputRegionWriter { get; set; } =
        (window, rects, sync) => X11Overlay.SetWindowInputRegion(window, rects, sync);
    private bool _inputRegionChanged;
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
            if (_clip != null)
            {
                // Shared frame beat; parked until Show. Start re-steps t = 0, already written above.
                _loop = new BeatLoop(frame, t => { if (t > 0) RevealAt(t * 1000); });
                _loop.Start();
            }
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
        if (t >= 1) { StopAnimation(); return; }
        // X11 and Win32 input regions are physical pixels; the clip is in DIPs. Before Show,
        // use the chosen screen's scale and synchronize the region before the window is mapped.
        var scaling = _window.IsVisible ? _window.RenderScaling
            : _window.Screens.ScreenFromPoint(_window.Position)?.Scaling ?? _window.RenderScaling;
        var rect = InputRect(_clip.Rect, scaling);
        _inputRegionChanged = true;
        try
        {
            if (!InputRegionWriter(_window, new[] { rect }, !_window.IsVisible)) StopAnimation();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "VideoService: floating input region failed");
            StopAnimation();
        }
    }

    internal static PixelRect InputRect(Rect clip, double scaling)
    {
        if (!double.IsFinite(scaling) || scaling <= 0) scaling = 1;
        var left = (int)Math.Ceiling(clip.X * scaling);
        var top = (int)Math.Ceiling(clip.Y * scaling);
        var right = (int)Math.Floor(clip.Right * scaling);
        var bottom = (int)Math.Floor(clip.Bottom * scaling);
        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private void StopAnimation()
    {
        _loop?.Stop();
        _loop = null;
        if (_inputRegionChanged)
        {
            _inputRegionChanged = false;
            try { InputRegionWriter(_window, null, false); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "VideoService: floating input restore failed"); }
        }
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
