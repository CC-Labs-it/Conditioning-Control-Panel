using System;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Services/Deeper/WebcamEnhancementEvents.cs:15: a subscription, never a camera start.
internal sealed class DeeperWebcamEvents : IEnhancementWebcam, IDisposable
{
    private readonly WebcamTracker _tracker;
    private volatile bool _disposed;
    public event Action? OnBlink;
    public event Action? OnMouthOpen;
    public event Action<double, double>? OnGazeMove;
    public event Action? OnFaceLost;
    public event Action? OnFaceFound;

    internal DeeperWebcamEvents(WebcamTracker tracker)
    {
        _tracker = tracker;
        tracker.OnBlink += Blink;
        tracker.OnMouthOpen += Mouth;
        tracker.OnGazeMove += Gaze;
        tracker.OnFaceLost += Lost;
        tracker.OnFaceFound += Found;
    }

    private void Send(Action action)
    {
        void Deliver() { if (!_disposed) action(); }
        if (Dispatcher.UIThread.CheckAccess()) Deliver();
        else Dispatcher.UIThread.Post(Deliver);
    }
    private void Blink() => Send(() => OnBlink?.Invoke());
    private void Mouth() => Send(() => OnMouthOpen?.Invoke());
    private void Gaze(Point p) => Send(() => OnGazeMove?.Invoke(p.X, p.Y));
    private void Lost() => Send(() => OnFaceLost?.Invoke());
    private void Found() => Send(() => OnFaceFound?.Invoke());

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.OnBlink -= Blink;
        _tracker.OnMouthOpen -= Mouth;
        _tracker.OnGazeMove -= Gaze;
        _tracker.OnFaceLost -= Lost;
        _tracker.OnFaceFound -= Found;
    }
}
