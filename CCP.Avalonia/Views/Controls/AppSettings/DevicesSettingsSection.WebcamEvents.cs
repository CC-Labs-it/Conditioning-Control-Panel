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
        DebugState();
    }

    private void DebugBlink() { _debugBlinks++; UpdateWebcamCounters(); AppendWebcamDebugLog($"Blink #{_debugBlinks}"); }
    private void DebugMouth() => AppendWebcamDebugLog($"Mouth-open #{++_debugMouths}");
    private void DebugTongue() => AppendWebcamDebugLog($"Tongue-out #{++_debugTongues}");
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
    /// <summary>WPF _onDebugStateChanged's "State → {s}" line. This tracker raises StateChanged on
    /// start/stop only, so WPF's Tracking/FaceLost hops on face loss are not logged (Missing in the row).</summary>
    private void DebugStateChanged()
    {
        DebugState();
        var t = WebcamTracker.Instance;
        AppendWebcamDebugLog($"State → {(t.IsStarting ? "Starting" : t.IsRunning ? "Tracking" : t.LastError != null && !t.StartWasStopped ? "Error" : "Stopped")}");
    }

    private void DebugState()
    {
        RefreshWebcamStartLabel();
        var tracker = WebcamTracker.Instance;
        string key = tracker.IsStarting ? "rf_webcam_starting"
            : tracker.IsRunning ? (_debugFace == "lost" ? "rf_webcam_face_lost" : "rf_webcam_tracking")
            : tracker.LastError != null && !tracker.StartWasStopped ? "rf_webcam_error" : "rf_webcam_stopped";
        // Bind the key, never set .Text: the {loc:Str} binding would put "Stopped" back on a language change.
        TxtWebcamDebugStatus.Bind(global::Avalonia.Controls.TextBlock.TextProperty,
            (global::Avalonia.Data.Binding)new global::ConditioningControlPanel.Avalonia.Localization.StrExtension(key).ProvideValue(null!));
    }
}
