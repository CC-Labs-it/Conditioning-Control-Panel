using System;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Platform.Deeper;

internal sealed class TrackerEnhancementWebcam : IEnhancementWebcam
{
    private readonly WebcamTracker _tracker;
    public TrackerEnhancementWebcam(WebcamTracker tracker)
    {
        _tracker = tracker;
        _tracker.OnGazeMove += p => OnGazeMove?.Invoke(p.X, p.Y);
    }
    public event Action<double, double>? OnGazeMove;
    public event Action? OnBlink { add => _tracker.OnBlink += value; remove => _tracker.OnBlink -= value; }
    public event Action? OnMouthOpen { add => _tracker.OnMouthOpen += value; remove => _tracker.OnMouthOpen -= value; }
    public event Action? OnFaceLost { add => _tracker.OnFaceLost += value; remove => _tracker.OnFaceLost -= value; }
    public event Action? OnFaceFound { add => _tracker.OnFaceFound += value; remove => _tracker.OnFaceFound -= value; }
}
