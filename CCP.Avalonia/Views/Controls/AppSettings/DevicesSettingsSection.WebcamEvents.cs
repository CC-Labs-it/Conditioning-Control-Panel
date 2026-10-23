using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;

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
        tracker.OnEyesClosedLong -= DebugEyesClosed;
        tracker.OnLongStare -= DebugStare;
        tracker.OnFaceFound -= DebugFaceFound;
        tracker.OnFaceLost -= DebugFaceLost;
        tracker.OnGazeSide -= DebugGaze;
        tracker.StateChanged -= DebugState;
        if (!subscribe) return;
        tracker.OnBlink += DebugBlink;
        tracker.OnMouthOpen += DebugMouth;
        tracker.OnTongueOut += DebugTongue;
        tracker.OnEyesClosedLong += DebugEyesClosed;
        tracker.OnLongStare += DebugStare;
        tracker.OnFaceFound += DebugFaceFound;
        tracker.OnFaceLost += DebugFaceLost;
        tracker.OnGazeSide += DebugGaze;
        tracker.StateChanged += DebugState;
        DebugState();
    }

    private void DebugBlink() { _debugBlinks++; UpdateWebcamCounters(); AppendWebcamDebugLog($"Blink #{_debugBlinks}"); }
    private void DebugMouth() => AppendWebcamDebugLog($"Mouth-open #{++_debugMouths}");
    private void DebugTongue() => AppendWebcamDebugLog($"Tongue-out #{++_debugTongues}");
    private void DebugEyesClosed() => AppendWebcamDebugLog("Eyes closed long");
    private void DebugStare(global::Avalonia.Point _) => AppendWebcamDebugLog("Long stare");
    private void DebugFaceFound() { _debugFace = "yes"; UpdateWebcamCounters(); DebugState(); AppendWebcamDebugLog("Face FOUND"); }
    private void DebugFaceLost() { _debugFace = "lost"; UpdateWebcamCounters(); DebugState(); AppendWebcamDebugLog("Face LOST"); }
    private void DebugGaze(GazeSide side)
    {
        if (_debugSide == side) return;
        _debugSide = side;
        UpdateWebcamCounters();
        AppendWebcamDebugLog($"Gaze → {side}");
    }
    private void UpdateWebcamCounters() => TxtWebcamDebugCounters.Text = $"Face: {_debugFace} | Blinks: {_debugBlinks} | Gaze: {_debugSide?.ToString() ?? "—"}";
    private void DebugState()
    {
        RefreshWebcamStartLabel();
        var tracker = WebcamTracker.Instance;
        string key = tracker.IsStarting ? "rf_webcam_starting"
            : tracker.IsRunning ? (_debugFace == "lost" ? "rf_webcam_face_lost" : "rf_webcam_tracking")
            : tracker.LastError != null && !tracker.StartWasStopped ? "rf_webcam_error" : "rf_webcam_stopped";
        TxtWebcamDebugStatus.Text = Loc.Get(key);
    }
}
