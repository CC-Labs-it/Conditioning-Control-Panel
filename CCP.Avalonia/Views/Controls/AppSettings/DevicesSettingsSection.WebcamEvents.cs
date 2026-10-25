using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;

public partial class DevicesSettingsSection
{
    private int _debugBlinks, _debugMouths, _debugTongues;
    private string _debugFace = "—";
    private GazeSide? _debugSide;

    private void SubscribeWebcamEvents(bool subscribe)
    {
        var tracker = WebcamTracker.Instance;
        // Attach/detach pairs: navigating Settings must never multiply event counts.
        tracker.OnBlink -= DebugBlink;
        tracker.OnMouthOpen -= DebugMouth;
        tracker.OnTongueOut -= DebugTongue;
        tracker.OnFaceFound -= DebugFaceFound;
        tracker.OnFaceLost -= DebugFaceLost;
        tracker.OnGazeSide -= DebugGaze;
        tracker.StateChanged -= DebugStateChanged;
        if (!subscribe) return;
        tracker.OnBlink += DebugBlink;
        tracker.OnMouthOpen += DebugMouth;
        tracker.OnTongueOut += DebugTongue;
        tracker.OnFaceFound += DebugFaceFound;
        tracker.OnFaceLost += DebugFaceLost;
        tracker.OnGazeSide += DebugGaze;
        tracker.StateChanged += DebugStateChanged;
    }

    internal void ResetWebcamDebug()
    {
        _debugBlinks = _debugMouths = _debugTongues = 0;
        _debugFace = "—";
        _debugSide = null;
        UpdateWebcamCounters();
    }

    private void DebugBlink() { _debugBlinks++; UpdateWebcamCounters(); AppendWebcamDebugLog($"Blink #{_debugBlinks}"); }
    private void DebugMouth() => AppendWebcamDebugLog($"Mouth-open #{++_debugMouths}");
    private void DebugTongue() => AppendWebcamDebugLog($"Tongue-out #{++_debugTongues}");
    private void DebugFaceFound() { _debugFace = "yes"; UpdateWebcamCounters(); AppendWebcamDebugLog("Face FOUND"); }
    private void DebugFaceLost() { _debugFace = "lost"; UpdateWebcamCounters(); AppendWebcamDebugLog("Face LOST"); }
    private void DebugGaze(GazeSide side)
    {
        if (_debugSide == side) return;
        _debugSide = side;
        UpdateWebcamCounters();
        AppendWebcamDebugLog($"Gaze → {side}");
    }
    private void UpdateWebcamCounters() => TxtWebcamDebugCounters.Text = $"Face: {_debugFace} | Blinks: {_debugBlinks} | Gaze: {_debugSide?.ToString() ?? "—"}";
    /// <summary>WPF _onDebugStateChanged's "State → {s}" line; the pill itself is RefreshWebcamStatus.</summary>
    private void DebugStateChanged() => AppendWebcamDebugLog($"State → {WebcamTracker.Instance.State}");
}
