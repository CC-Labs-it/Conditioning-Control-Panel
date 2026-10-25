using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;

// WPF EnsureWebcamDebugSubscribed: one log per gaze-side change, counters survive tab switches.
public partial class DevicesSettingsSection
{
    private int _debugBlinks, _debugMouths;
    private string _debugFace = "—";
    private GazeSide? _debugGaze;
    private void SubscribeDiagnostics(bool subscribe)
    {
        var t = Platform.WebcamTracker.Instance;
        if (subscribe)
        {
            t.StateChanged += DebugState;
            t.OnFaceFound += DebugFaceFound; t.OnFaceLost += DebugFaceLost;
            t.OnBlink += DebugBlink; t.OnMouthOpen += DebugMouth;
            t.OnGazeSide += DebugGaze;
        }
        else
        {
            t.StateChanged -= DebugState;
            t.OnFaceFound -= DebugFaceFound; t.OnFaceLost -= DebugFaceLost;
            t.OnBlink -= DebugBlink; t.OnMouthOpen -= DebugMouth;
            t.OnGazeSide -= DebugGaze;
        }
    }
    internal void ResetDebugCounters()
    {
        _debugBlinks = _debugMouths = 0; _debugFace = "—"; _debugGaze = null;
        PaintDebugCounters();
    }
    private void DebugState() => AppendWebcamDebugLog($"State → {Platform.WebcamTracker.Instance.State}");
    private void DebugFaceFound() { _debugFace = "yes"; PaintDebugCounters(); AppendWebcamDebugLog("Face FOUND"); }
    private void DebugFaceLost() { _debugFace = "lost"; PaintDebugCounters(); AppendWebcamDebugLog("Face LOST"); }
    private void DebugBlink() { _debugBlinks++; PaintDebugCounters(); AppendWebcamDebugLog($"Blink #{_debugBlinks}"); }
    private void DebugMouth() => AppendWebcamDebugLog($"Mouth-open #{++_debugMouths}");
    private void DebugGaze(GazeSide side)
    {
        if (_debugGaze == side) return;
        _debugGaze = side; PaintDebugCounters(); AppendWebcamDebugLog($"Gaze → {side}");
    }
    private void PaintDebugCounters() => TxtWebcamDebugCounters.Text =
        $"Face: {_debugFace} | Blinks: {_debugBlinks} | Gaze: {_debugGaze?.ToString() ?? "—"}";
}
