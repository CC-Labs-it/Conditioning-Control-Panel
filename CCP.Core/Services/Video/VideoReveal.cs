using System;
using System.Diagnostics;

namespace ConditioningControlPanel.Services.Video;

/// <summary>One clip's linear fade and delayed controls. The head supplies UI animation frames;
/// decoding another video frame is never required. Dispose before closing or replaying.</summary>
internal sealed class VideoReveal : IDisposable
{
    private readonly double _duration;
    private readonly Action<Action> _requestFrame;
    private readonly Action<double> _picture;
    private readonly Action _controls;
    private readonly Func<double> _now;
    private bool _started, _disposed;
    private double _start;

    public VideoReveal(double duration, Action<Action> requestFrame, Action<double> picture,
        Action controls, Func<double>? now = null)
    {
        _duration = duration;
        _requestFrame = requestFrame;
        _picture = picture;
        _controls = controls;
        var clock = Stopwatch.StartNew();
        _now = now ?? (() => clock.Elapsed.TotalSeconds);
    }

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _start = _now();
        Frame();
    }

    private void Frame()
    {
        if (_disposed) return;
        var opacity = MandatoryVideoPresentation.PictureOpacity(true, _now() - _start, _duration);
        _picture(opacity);
        if (opacity < 1) _requestFrame(Frame);
        else _controls();
    }

    public void Dispose() => _disposed = true;
}
